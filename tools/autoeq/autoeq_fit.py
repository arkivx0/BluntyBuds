"""Shared AutoEQ parsing and 10-band fitting (reference implementation).

The C# (windows/BluntyBuds.Proto/AutoEq.cs) and Kotlin (android/.../proto/AutoEq.kt)
ports must give the same results; the unit tests in both apps check this.
"""
import math, re

FREQS = [20, 50, 100, 200, 400, 800, 1600, 3200, 6400, 12800]
QS = [0.5, 0.6, 0.7, 0.8, 1.0, 1.2, 1.4, 1.6, 1.8, 2.0]
MIN_DB, MAX_DB, STEP = -12.0, 12.0, 0.5
FS = 48000.0


def biquad_db(kind, fc, gain, q, f):
    """Magnitude (dB) at f of an RBJ-cookbook peaking / low-shelf / high-shelf filter."""
    a = 10 ** (gain / 40)
    w0 = 2 * math.pi * fc / FS
    cw, sw = math.cos(w0), math.sin(w0)
    al = sw / (2 * q)
    if kind == "PK":
        b0, b1, b2 = 1 + al * a, -2 * cw, 1 - al * a
        a0, a1, a2 = 1 + al / a, -2 * cw, 1 - al / a
    else:
        sa = 2 * math.sqrt(a) * al
        if kind == "LS":
            b0 = a * ((a + 1) - (a - 1) * cw + sa); b1 = 2 * a * ((a - 1) - (a + 1) * cw); b2 = a * ((a + 1) - (a - 1) * cw - sa)
            a0 = (a + 1) + (a - 1) * cw + sa; a1 = -2 * ((a - 1) + (a + 1) * cw); a2 = (a + 1) + (a - 1) * cw - sa
        else:  # HS
            b0 = a * ((a + 1) + (a - 1) * cw + sa); b1 = -2 * a * ((a - 1) + (a + 1) * cw); b2 = a * ((a + 1) + (a - 1) * cw - sa)
            a0 = (a + 1) - (a - 1) * cw + sa; a1 = 2 * ((a - 1) - (a + 1) * cw); a2 = (a + 1) - (a - 1) * cw - sa
    w = 2 * math.pi * f / FS
    c1, s1, c2, s2 = math.cos(w), math.sin(w), math.cos(2 * w), math.sin(2 * w)
    nr, ni = b0 + b1 * c1 + b2 * c2, -(b1 * s1 + b2 * s2)
    dr, di = a0 + a1 * c1 + a2 * c2, -(a1 * s1 + a2 * s2)
    return 10 * math.log10((nr * nr + ni * ni) / (dr * dr + di * di))


NUM = r"([-+]?\d+(?:\.\d+)?)"


def parse_parametric(text):
    """AutoEQ ParametricEQ.txt -> list of (kind, fc, gain, q). Preamp is ignored (levels are normalized)."""
    filters = []
    for line in text.splitlines():
        m = re.match(r"\s*Filter\s*\d*\s*:\s*(ON|OFF)\s+([A-Z]+)\s+Fc\s+" + NUM + r"\s*Hz\s+Gain\s+" + NUM + r"\s*dB(?:\s+Q\s+" + NUM + ")?", line, re.I)
        if not m or m.group(1).upper() != "ON":
            continue
        t = m.group(2).upper()
        kind = {"PK": "PK", "PEQ": "PK", "LSC": "LS", "LS": "LS", "LSQ": "LS", "HSC": "HS", "HS": "HS", "HSQ": "HS"}.get(t)
        if kind is None:
            continue
        q = float(m.group(5)) if m.group(5) else 0.707
        filters.append((kind, float(m.group(3)), float(m.group(4)), q))
    if not filters:
        raise ValueError("no filters")
    return filters


def parse_graphic(text):
    """AutoEQ GraphicEQ.txt ('GraphicEQ: f g; f g; ...') -> sorted list of (f, g)."""
    m = re.search(r"GraphicEQ\s*:(.*)", text, re.I | re.S)
    if not m:
        raise ValueError("not a GraphicEQ file")
    pts = []
    for part in m.group(1).split(";"):
        xs = part.split()
        if len(xs) >= 2:
            pts.append((float(xs[0]), float(xs[1])))
    pts.sort()
    if len(pts) < 2:
        raise ValueError("no points")
    return pts


def graphic_at(pts, f):
    if f <= pts[0][0]:
        return pts[0][1]
    if f >= pts[-1][0]:
        return pts[-1][1]
    for (f0, g0), (f1, g1) in zip(pts, pts[1:]):
        if f0 <= f <= f1:
            t = (math.log(f) - math.log(f0)) / (math.log(f1) - math.log(f0))
            return g0 + t * (g1 - g0)


def parametric_at(filters, f):
    return sum(biquad_db(k, fc, g, q, f) for k, fc, g, q in filters)


def device_db(gains, f):
    return sum(biquad_db("PK", FREQS[j], gains[j], QS[j], f) for j in range(10) if gains[j] != 0)


def fit(target):
    """target: 10 dB values at FREQS. Returns 10 gains so the device's peaking bands
    hit the target (minus its mean) at the band centres, clamped and quantized."""
    mean = sum(target) / 10
    t = [x - mean for x in target]
    g = [max(MIN_DB, min(MAX_DB, x)) for x in t]
    for _ in range(60):
        r = [device_db(g, f) for f in FREQS]
        g = [max(MIN_DB, min(MAX_DB, g[i] + 0.5 * (t[i] - r[i]))) for i in range(10)]
    return [quantize(x) for x in g]


def quantize(x):
    x = max(MIN_DB, min(MAX_DB, x))
    q = math.floor(x / STEP + 0.5) * STEP  # round half up, same in all ports
    return 0.0 if q == 0 else q


def fit_text(text):
    """Auto-detect GraphicEQ or ParametricEQ text and fit."""
    if re.search(r"GraphicEQ\s*:", text, re.I):
        pts = parse_graphic(text)
        return fit([graphic_at(pts, f) for f in FREQS])
    fl = parse_parametric(text)
    return fit([parametric_at(fl, f) for f in FREQS])
