using System.Globalization;
using System.Text.RegularExpressions;
namespace BluntyBuds.Proto;

/// <summary>
/// AutoEQ support: parses AutoEQ ParametricEQ.txt / GraphicEQ.txt files and fits the 10 device bands to them.
/// Built-in profiles come from AutoEq (github.com/jaakkopasanen/AutoEq, MIT, (c) 2018-2022 Jaakko Pasanen),
/// pre-fitted at dev time by tools/autoeq/gen_autoeq.py into autoeq_profiles.tsv. Port of tools/autoeq/autoeq_fit.py.
/// </summary>
public static class AutoEq
{
    public sealed record Filter(string Kind, double Fc, double Gain, double Q);
    /// <summary>A built-in profile. Chip is the chip family of the measured unit when known (optional 4th TSV column).</summary>
    public sealed record Profile(string Model, string Source, float[] Gains, string? Chip = null);
    const double Fs = 48000, Step = 0.5;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>RBJ-cookbook peaking (PK), low-shelf (LS) or high-shelf (HS) magnitude in dB at f.</summary>
    public static double BiquadDb(string kind, double fc, double gain, double q, double f)
    {
        double a = Math.Pow(10, gain / 40), w0 = 2 * Math.PI * fc / Fs, cw = Math.Cos(w0), sw = Math.Sin(w0), al = sw / (2 * q);
        double b0, b1, b2, a0, a1, a2;
        if (kind == "PK") { b0 = 1 + al * a; b1 = -2 * cw; b2 = 1 - al * a; a0 = 1 + al / a; a1 = -2 * cw; a2 = 1 - al / a; }
        else
        {
            double sa = 2 * Math.Sqrt(a) * al;
            if (kind == "LS")
            {
                b0 = a * ((a + 1) - (a - 1) * cw + sa); b1 = 2 * a * ((a - 1) - (a + 1) * cw); b2 = a * ((a + 1) - (a - 1) * cw - sa);
                a0 = (a + 1) + (a - 1) * cw + sa; a1 = -2 * ((a - 1) + (a + 1) * cw); a2 = (a + 1) + (a - 1) * cw - sa;
            }
            else
            {
                b0 = a * ((a + 1) + (a - 1) * cw + sa); b1 = -2 * a * ((a - 1) + (a + 1) * cw); b2 = a * ((a + 1) + (a - 1) * cw - sa);
                a0 = (a + 1) - (a - 1) * cw + sa; a1 = 2 * ((a - 1) - (a + 1) * cw); a2 = (a + 1) - (a - 1) * cw - sa;
            }
        }
        double w = 2 * Math.PI * f / Fs, c1 = Math.Cos(w), s1 = Math.Sin(w), c2 = Math.Cos(2 * w), s2 = Math.Sin(2 * w);
        double nr = b0 + b1 * c1 + b2 * c2, ni = -(b1 * s1 + b2 * s2), dr = a0 + a1 * c1 + a2 * c2, di = -(a1 * s1 + a2 * s2);
        return 10 * Math.Log10((nr * nr + ni * ni) / (dr * dr + di * di));
    }

    const string Num = @"([-+]?\d+(?:\.\d+)?)";
    static readonly Regex FilterRx = new(@"^\s*Filter\s*\d*\s*:\s*(ON|OFF)\s+([A-Z]+)\s+Fc\s+" + Num + @"\s*Hz\s+Gain\s+" + Num + @"\s*dB(?:\s+Q\s+" + Num + ")?", RegexOptions.IgnoreCase);

    static readonly Regex PreampRx = new(@"^\s*Preamp\s*:\s*" + Num + @"\s*dB", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    /// <summary>The "Preamp: -5.6 dB" line of a ParametricEQ.txt, or null.</summary>
    public static double? ParsePreamp(string text) { var m = PreampRx.Match(text); return m.Success ? double.Parse(m.Groups[1].Value, Inv) : null; }

    public static List<Filter> ParseParametric(string text)
    {
        var list = new List<Filter>();
        foreach (var line in text.Split('\n'))
        {
            var m = FilterRx.Match(line);
            if (!m.Success || !m.Groups[1].Value.Equals("ON", StringComparison.OrdinalIgnoreCase)) continue;
            string? kind = m.Groups[2].Value.ToUpperInvariant() switch { "PK" or "PEQ" => "PK", "LSC" or "LS" or "LSQ" => "LS", "HSC" or "HS" or "HSQ" => "HS", _ => null };
            if (kind is null) continue;
            double q = m.Groups[5].Success ? double.Parse(m.Groups[5].Value, Inv) : 0.707;
            list.Add(new(kind, double.Parse(m.Groups[3].Value, Inv), double.Parse(m.Groups[4].Value, Inv), q));
        }
        if (list.Count == 0) throw new FormatException("No AutoEQ filters found");
        return list;
    }

    public static List<(double F, double G)> ParseGraphic(string text)
    {
        var m = Regex.Match(text, @"GraphicEQ\s*:(.*)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!m.Success) throw new FormatException("Not a GraphicEQ file");
        var pts = new List<(double, double)>();
        foreach (var part in m.Groups[1].Value.Split(';'))
        {
            var xs = part.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (xs.Length >= 2) pts.Add((double.Parse(xs[0], Inv), double.Parse(xs[1], Inv)));
        }
        pts.Sort();
        if (pts.Count < 2) throw new FormatException("No GraphicEQ points");
        return pts;
    }

    public static double GraphicAt(List<(double F, double G)> p, double f)
    {
        if (f <= p[0].F) return p[0].G;
        if (f >= p[^1].F) return p[^1].G;
        for (int i = 0; i + 1 < p.Count; i++)
            if (p[i].F <= f && f <= p[i + 1].F)
                return p[i].G + (Math.Log(f) - Math.Log(p[i].F)) / (Math.Log(p[i + 1].F) - Math.Log(p[i].F)) * (p[i + 1].G - p[i].G);
        return p[^1].G;
    }

    public static double ParametricAt(List<Filter> fl, double f) { double s = 0; foreach (var x in fl) s += BiquadDb(x.Kind, x.Fc, x.Gain, x.Q, f); return s; }

    public static double DeviceDb(double[] g, double f) { double s = 0; for (int j = 0; j < 10; j++) if (g[j] != 0) s += BiquadDb("PK", EqGrid.Freqs[j], g[j], EqGrid.Qs[j], f); return s; }

    static double Clamp(double x) => Math.Clamp(x, EqGrid.MinDb, EqGrid.MaxDb);

    /// <summary>Round half up to the 0.5 dB UI step and clamp to the device range.</summary>
    public static float Quantize(double x) { var q = Math.Floor(Clamp(x) / Step + 0.5) * Step; return q == 0 ? 0f : (float)q; }

    /// <summary>Shift all bands down by the largest positive gain so no band boosts (avoids clipping).</summary>
    public static double[] Headroom(double[] g) { double top = g.Max(); return top > 0 ? g.Select(x => x - top).ToArray() : (double[])g.Clone(); }

    /// <summary>Fit the device's peaking bands to a target (dB at each band centre, level-normalized to its mean unless
    /// normalizeMean is false), then shift down so the highest band is &lt;= 0 dB, clamp and quantize.</summary>
    public static float[] Fit(double[] target, bool normalizeMean = true)
    {
        double mean = normalizeMean ? target.Average() : 0;
        var t = target.Select(x => x - mean).ToArray();
        var g = t.Select(Clamp).ToArray();
        for (int it = 0; it < 60; it++)
        {
            var r = EqGrid.Freqs.Select(f => DeviceDb(g, f)).ToArray();
            g = Enumerable.Range(0, 10).Select(i => Clamp(g[i] + 0.5 * (t[i] - r[i]))).ToArray();
        }
        return Headroom(g).Select(Quantize).ToArray();
    }

    /// <summary>Fit an AutoEQ GraphicEQ.txt or ParametricEQ.txt file (auto-detected).</summary>
    public static float[] FitText(string text)
    {
        if (Regex.IsMatch(text, @"GraphicEQ\s*:", RegexOptions.IgnoreCase)) { var p = ParseGraphic(text); return Fit(EqGrid.Freqs.Select(f => GraphicAt(p, f)).ToArray()); }
        var fl = ParseParametric(text);
        var pre = ParsePreamp(text);
        if (pre is null) return Fit(EqGrid.Freqs.Select(f => ParametricAt(fl, f)).ToArray());
        // Honor the file's preamp (absolute levels), then still enforce max <= 0 dB.
        return Fit(EqGrid.Freqs.Select(f => ParametricAt(fl, f) + pre.Value).ToArray(), normalizeMean: false);
    }

    public static List<Profile> ParseProfiles(string tsv)
    {
        var list = new List<Profile>();
        foreach (var raw in tsv.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#') continue;
            var c = line.Split('\t');
            if (c.Length is not (3 or 4)) continue;
            var g = c[2].Split(',').Select(s => float.Parse(s, Inv)).ToArray();
            if (g.Length == 10) list.Add(new(c[0], c[1], g, c.Length == 4 && c[3].Length > 0 ? c[3] : null));
        }
        return list;
    }

    static List<Profile>? _profiles;
    /// <summary>Built-in profiles (embedded autoeq_profiles.tsv).</summary>
    public static IReadOnlyList<Profile> Profiles
    {
        get
        {
            if (_profiles is null)
            {
                using var s = typeof(AutoEq).Assembly.GetManifestResourceStream("BluntyBuds.Proto.autoeq_profiles.tsv");
                _profiles = s is null ? new() : ParseProfiles(new StreamReader(s).ReadToEnd());
            }
            return _profiles;
        }
    }

    /// <summary>Profile for an exact model name (e.g. "NC9"; "NC9Pro" does not match).</summary>
    public static Profile? For(string? model) => model is null ? null : Profiles.FirstOrDefault(p => p.Model.Equals(model, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Profile to suggest for a connected model. One model name (e.g. NC9) can cover several hardware revisions
    /// (BES, WuQi, Bluetrum...), so Confident is true only when the device's chip family is known exactly and matches
    /// the chip of the measured unit. Unconfident matches are listed but never auto-selected.
    /// </summary>
    public static (Profile? Profile, bool Confident) Suggest(DeviceModel m, IReadOnlyList<Profile>? profiles = null)
    {
        if (m.IsUnknown) return (null, false);
        var p = (profiles ?? Profiles).FirstOrDefault(x => x.Model.Equals(m.Name, StringComparison.OrdinalIgnoreCase));
        if (p is null) return (null, false);
        bool chipKnown = m.Chip.Length > 0 && m.Chip != "?" && !m.Chip.Contains('/');
        return (p, chipKnown && p.Chip is not null && p.Chip.Equals(m.Chip, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Preset-list label for the suggested profile.</summary>
    public static string SuggestLabel(Profile p, bool confident) => "AutoEQ · " + p.Model + (confident ? " (this model)" : " (may differ by hardware revision)");

    public const string Attribution = "AutoEQ profiles: AutoEq by Jaakko Pasanen, github.com/jaakkopasanen/AutoEq (MIT License, Copyright (c) 2018-2022 Jaakko Pasanen). Measurements by their respective sources (e.g. Rtings).";
}
