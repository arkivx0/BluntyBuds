package io.github.arkivx0.bluntybuds.proto

import kotlin.math.*

/**
 * AutoEQ support: parses AutoEQ ParametricEQ.txt / GraphicEQ.txt files and fits the 10 device bands to them.
 * Built-in profiles come from AutoEq (github.com/jaakkopasanen/AutoEq, MIT, (c) 2018-2022 Jaakko Pasanen),
 * pre-fitted at dev time by tools/autoeq/gen_autoeq.py into assets/autoeq_profiles.tsv. Port of tools/autoeq/autoeq_fit.py.
 */
object AutoEq {
    data class Filter(val kind: String, val fc: Double, val gain: Double, val q: Double)
    class Profile(val model: String, val source: String, val gains: FloatArray)
    private const val FS = 48000.0
    private const val STEP = 0.5

    fun biquadDb(kind: String, fc: Double, gain: Double, q: Double, f: Double): Double {
        val a = 10.0.pow(gain / 40); val w0 = 2 * PI * fc / FS; val cw = cos(w0); val sw = sin(w0); val al = sw / (2 * q)
        val b0: Double; val b1: Double; val b2: Double; val a0: Double; val a1: Double; val a2: Double
        if (kind == "PK") { b0 = 1 + al * a; b1 = -2 * cw; b2 = 1 - al * a; a0 = 1 + al / a; a1 = -2 * cw; a2 = 1 - al / a }
        else {
            val sa = 2 * sqrt(a) * al
            if (kind == "LS") {
                b0 = a * ((a + 1) - (a - 1) * cw + sa); b1 = 2 * a * ((a - 1) - (a + 1) * cw); b2 = a * ((a + 1) - (a - 1) * cw - sa)
                a0 = (a + 1) + (a - 1) * cw + sa; a1 = -2 * ((a - 1) + (a + 1) * cw); a2 = (a + 1) + (a - 1) * cw - sa
            } else {
                b0 = a * ((a + 1) + (a - 1) * cw + sa); b1 = -2 * a * ((a - 1) + (a + 1) * cw); b2 = a * ((a + 1) + (a - 1) * cw - sa)
                a0 = (a + 1) - (a - 1) * cw + sa; a1 = 2 * ((a - 1) - (a + 1) * cw); a2 = (a + 1) - (a - 1) * cw - sa
            }
        }
        val w = 2 * PI * f / FS; val c1 = cos(w); val s1 = sin(w); val c2 = cos(2 * w); val s2 = sin(2 * w)
        val nr = b0 + b1 * c1 + b2 * c2; val ni = -(b1 * s1 + b2 * s2); val dr = a0 + a1 * c1 + a2 * c2; val di = -(a1 * s1 + a2 * s2)
        return 10 * log10((nr * nr + ni * ni) / (dr * dr + di * di))
    }

    private const val NUM = """([-+]?\d+(?:\.\d+)?)"""
    private val FILTER_RX = Regex("""^\s*Filter\s*\d*\s*:\s*(ON|OFF)\s+([A-Z]+)\s+Fc\s+$NUM\s*Hz\s+Gain\s+$NUM\s*dB(?:\s+Q\s+$NUM)?""", RegexOption.IGNORE_CASE)
    private val GRAPHIC_RX = Regex("""GraphicEQ\s*:(.*)""", setOf(RegexOption.IGNORE_CASE, RegexOption.DOT_MATCHES_ALL))

    fun parseParametric(text: String): List<Filter> {
        val list = ArrayList<Filter>()
        for (line in text.split('\n')) {
            val m = FILTER_RX.find(line) ?: continue
            if (!m.groupValues[1].equals("ON", true)) continue
            val kind = when (m.groupValues[2].uppercase()) { "PK", "PEQ" -> "PK"; "LSC", "LS", "LSQ" -> "LS"; "HSC", "HS", "HSQ" -> "HS"; else -> null } ?: continue
            val q = m.groups[5]?.value?.toDouble() ?: 0.707
            list += Filter(kind, m.groupValues[3].toDouble(), m.groupValues[4].toDouble(), q)
        }
        if (list.isEmpty()) throw IllegalArgumentException("No AutoEQ filters found")
        return list
    }

    fun parseGraphic(text: String): List<Pair<Double, Double>> {
        val m = GRAPHIC_RX.find(text) ?: throw IllegalArgumentException("Not a GraphicEQ file")
        val pts = m.groupValues[1].split(';').mapNotNull { p ->
            val xs = p.trim().split(Regex("\\s+")); if (xs.size >= 2) xs[0].toDouble() to xs[1].toDouble() else null
        }.sortedWith(compareBy({ it.first }, { it.second }))
        if (pts.size < 2) throw IllegalArgumentException("No GraphicEQ points")
        return pts
    }

    fun graphicAt(p: List<Pair<Double, Double>>, f: Double): Double {
        if (f <= p.first().first) return p.first().second
        if (f >= p.last().first) return p.last().second
        for (i in 0 until p.size - 1) {
            val (f0, g0) = p[i]; val (f1, g1) = p[i + 1]
            if (f0 <= f && f <= f1) return g0 + (ln(f) - ln(f0)) / (ln(f1) - ln(f0)) * (g1 - g0)
        }
        return p.last().second
    }

    fun parametricAt(fl: List<Filter>, f: Double) = fl.sumOf { biquadDb(it.kind, it.fc, it.gain, it.q, f) }
    fun deviceDb(g: DoubleArray, f: Double): Double {
        var s = 0.0
        for (j in 0 until 10) if (g[j] != 0.0) s += biquadDb("PK", EqGrid.FREQS[j].toDouble(), g[j], EqGrid.QS[j].toDouble(), f)
        return s
    }
    private fun clamp(x: Double) = x.coerceIn(EqGrid.MIN_DB.toDouble(), EqGrid.MAX_DB.toDouble())

    /** Round half up to the 0.5 dB UI step and clamp to the device range. */
    fun quantize(x: Double): Float { val q = floor(clamp(x) / STEP + 0.5) * STEP; return if (q == 0.0) 0f else q.toFloat() }

    /** Fit the device's peaking bands to a target (dB at each band centre, level-normalized to its mean). */
    fun fit(target: DoubleArray): FloatArray {
        val mean = target.average()
        val t = DoubleArray(10) { target[it] - mean }
        var g = DoubleArray(10) { clamp(t[it]) }
        repeat(60) {
            val r = DoubleArray(10) { deviceDb(g, EqGrid.FREQS[it].toDouble()) }
            g = DoubleArray(10) { clamp(g[it] + 0.5 * (t[it] - r[it])) }
        }
        return FloatArray(10) { quantize(g[it]) }
    }

    /** Fit an AutoEQ GraphicEQ.txt or ParametricEQ.txt file (auto-detected). */
    fun fitText(text: String): FloatArray {
        if (Regex("""GraphicEQ\s*:""", RegexOption.IGNORE_CASE).containsMatchIn(text)) {
            val p = parseGraphic(text); return fit(DoubleArray(10) { graphicAt(p, EqGrid.FREQS[it].toDouble()) })
        }
        val fl = parseParametric(text)
        return fit(DoubleArray(10) { parametricAt(fl, EqGrid.FREQS[it].toDouble()) })
    }

    fun parseProfiles(tsv: String): List<Profile> = tsv.split('\n').mapNotNull { raw ->
        val line = raw.trimEnd('\r')
        if (line.isEmpty() || line[0] == '#') return@mapNotNull null
        val c = line.split('\t'); if (c.size != 3) return@mapNotNull null
        val g = c[2].split(',').map { it.toFloat() }
        if (g.size == 10) Profile(c[0], c[1], g.toFloatArray()) else null
    }

    /** Exact model-name match ("NC9Pro" does not get the NC9 profile). */
    fun forModel(profiles: List<Profile>, model: String?) = model?.let { m -> profiles.firstOrNull { it.model.equals(m, true) } }

    const val ATTRIBUTION = "AutoEQ profiles: AutoEq by Jaakko Pasanen, github.com/jaakkopasanen/AutoEq (MIT License, Copyright (c) 2018-2022 Jaakko Pasanen). Measurements by their respective sources (e.g. Rtings)."
}
