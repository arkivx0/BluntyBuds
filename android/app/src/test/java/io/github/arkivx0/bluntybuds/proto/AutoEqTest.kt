package io.github.arkivx0.bluntybuds.proto

import org.junit.Assert.*
import org.junit.Test
import java.io.File

class AutoEqTest {
    private fun fixture(n: String) = File("../../tools/autoeq/testdata/$n").readText()
    private fun f(vararg x: Float) = x
    private val profiles by lazy { AutoEq.parseProfiles(File("src/main/assets/autoeq_profiles.tsv").readText()) }

    @Test fun parsesParametric() {
        val fl = AutoEq.parseParametric(fixture("NC9 ParametricEQ.txt"))
        assertEquals(10, fl.size)
        assertEquals(AutoEq.Filter("LS", 105.0, -5.2, 0.70), fl[0])
        assertEquals(AutoEq.Filter("HS", 10000.0, -13.4, 0.70), fl[5])
        assertEquals("PK", fl[1].kind)
    }
    @Test fun parametricSkipsOffAndUnknownAndThrowsOnEmpty() {
        val fl = AutoEq.parseParametric("Preamp: -3 dB\r\nFilter 1: OFF PK Fc 100 Hz Gain 9 dB Q 1\r\nFilter 2: ON XX Fc 1 Hz Gain 1 dB Q 1\r\nFilter 3: ON PK Fc 800 Hz Gain 6 dB Q 1.2\r\n")
        assertEquals(1, fl.size)
        assertThrows(IllegalArgumentException::class.java) { AutoEq.parseParametric("hello") }
    }
    @Test fun parsesGraphicAndInterpolates() {
        val p = AutoEq.parseGraphic("GraphicEQ: 1000 2; 100 0; 10000 -2")
        assertEquals(3, p.size); assertEquals(100.0, p[0].first, 0.0)
        assertEquals(1.0, AutoEq.graphicAt(p, Math.sqrt(100.0 * 1000)), 1e-6)
        assertEquals(0.0, AutoEq.graphicAt(p, 20.0), 0.0); assertEquals(-2.0, AutoEq.graphicAt(p, 20000.0), 0.0)
        assertThrows(IllegalArgumentException::class.java) { AutoEq.parseGraphic("Filter 1: ON PK") }
    }
    @Test fun biquadPeakAtCentre() = assertEquals(6.0, AutoEq.biquadDb("PK", 1000.0, 6.0, 1.0, 1000.0), 1e-3)
    @Test fun quantizes() {
        assertEquals(0.5f, AutoEq.quantize(0.25)); assertEquals(0f, AutoEq.quantize(-0.25)); assertEquals(-0.5f, AutoEq.quantize(-0.26))
        assertEquals(12f, AutoEq.quantize(13.0)); assertEquals(-12f, AutoEq.quantize(-40.0))
    }
    // Expected values come from the reference implementation tools/autoeq/autoeq_fit.py.
    @Test fun fitsFlat() = assertArrayEquals(FloatArray(10), AutoEq.fitText("GraphicEQ: 20 0; 1000 0; 20000 0"), 0f)
    @Test fun fitsSinglePeak() = assertArrayEquals(f(-6.5f, -5f, -5.5f, -5.5f, -5.5f, 0f, -6f, -6.5f, -6.5f, -7f),
        AutoEq.fitText("Preamp: -3 dB\nFilter 1: ON PK Fc 800 Hz Gain 6 dB Q 1.2\nFilter 2: OFF PK Fc 100 Hz Gain 9 dB Q 1\n"), 0f)
    @Test fun fitClampsToRange() = assertArrayEquals(f(0f, 0f, -4.5f, -12f, -12f, -12f, -12f, -12f, -12f, -12f),
        AutoEq.fitText("Preamp: 0 dB\r\nFilter 1: ON LSC Fc 105 Hz Gain 30 dB Q 0.7\r\n"), 0f)
    @Test fun fitsParametricFixture() = assertArrayEquals(f(-10.5f, -6.5f, -4.5f, -7.5f, -3.5f, 0f, -4.5f, -9f, -7f, -12f),
        AutoEq.fitText(fixture("NC9 ParametricEQ.txt")), 0f)
    @Test fun graphicFixtureMatchesBuiltInProfile() =
        assertArrayEquals(AutoEq.forModel(profiles, "NC9")!!.gains, AutoEq.fitText(fixture("NC9 GraphicEQ.txt")), 0f)
    @Test fun fitWithoutPreampUsesMeanThenHeadroom() = assertArrayEquals(f(-6f, -5.5f, -6f, -6f, -6f, 0f, -6f, -6f, -6f, -6.5f),
        AutoEq.fitText("Filter 1: ON PK Fc 800 Hz Gain 6 dB Q 1.2\n"), 0f)
    @Test fun fitHonorsLowPreamp() = assertArrayEquals(f(-10f, -4f, -5f, -6f, -6.5f, -1f, -8f, -8.5f, -9.5f, -11f),
        AutoEq.fitText("Preamp: -12 dB\nFilter 1: ON PK Fc 800 Hz Gain 6 dB Q 1.2\n"), 0f)
    @Test fun parsesPreamp() {
        assertEquals(-5.6, AutoEq.parsePreamp(fixture("NC9 ParametricEQ.txt"))!!, 0.0)
        assertNull(AutoEq.parsePreamp("Filter 1: ON PK Fc 800 Hz Gain 6 dB Q 1.2"))
    }
    @Test fun headroomShiftsDownOnlyWhenBoosting() {
        assertArrayEquals(doubleArrayOf(-3.0, 0.0, -5.0), AutoEq.headroom(doubleArrayOf(2.0, 5.0, 0.0)), 0.0)
        assertArrayEquals(doubleArrayOf(-1.0, -2.0), AutoEq.headroom(doubleArrayOf(-1.0, -2.0)), 0.0)
    }
    @Test fun importedFilesNeverBoost() {
        for (t in listOf(fixture("NC9 ParametricEQ.txt"), fixture("NC9 GraphicEQ.txt"), "Preamp: 6 dB\nFilter 1: ON PK Fc 100 Hz Gain 9 dB Q 1\n", "GraphicEQ: 20 10; 1000 12; 20000 8"))
            assertTrue(AutoEq.fitText(t).max() <= 0f)
    }
    @Test fun suggestOnlyConfidentForMatchingChip() {
        val ps = listOf(AutoEq.Profile("NC9", "m", FloatArray(10), "BES"), AutoEq.Profile("T6", "m", FloatArray(10)))
        val bes = Models.byMark("1A0E")!!; val wuqi = Models.byMark("6B")!!
        assertTrue(AutoEq.suggest(ps, bes).second)
        assertFalse(AutoEq.suggest(ps, wuqi).second); assertNotNull(AutoEq.suggest(ps, wuqi).first)
        assertFalse(AutoEq.suggest(ps, Models.detect("TOZO NC9")).second)   // name only: revision unknown
        assertFalse(AutoEq.suggest(ps, Models.byMark("86")!!).second)       // measured chip unknown
        assertNull(AutoEq.suggest(ps, DeviceModel.UNKNOWN).first)
        assertFalse(AutoEq.suggest(profiles, bes).second)                   // built-in table: no measured chip known yet
        assertEquals("AutoEQ · NC9 (may differ by hardware revision)", AutoEq.suggestLabel(ps[0], false))
        assertEquals("AutoEQ · NC9 (this model)", AutoEq.suggestLabel(ps[0], true))
    }
    @Test fun builtInProfiles() {
        assertTrue(profiles.isNotEmpty())
        for (p in profiles) { assertEquals(10, p.gains.size); p.gains.forEach { assertTrue(it in -12f..0f) }; assertEquals(0f, p.gains.max(), 0f) }
        assertNotNull(AutoEq.forModel(profiles, "nc9")); assertNull(AutoEq.forModel(profiles, "NC9Pro")); assertNull(AutoEq.forModel(profiles, null))
    }
    @Test fun parsesProfileTsv() {
        val p = AutoEq.parseProfiles("# c\nX1\tsrc\t1,2,3,4,5,6,7,8,9,10\r\nbad line\nY\ts\t1,2\n")
        assertEquals(1, p.size); assertEquals("X1", p[0].model); assertEquals(10f, p[0].gains[9]); assertNull(p[0].chip)
        assertEquals("BES", AutoEq.parseProfiles("A\ts\t1,2,3,4,5,6,7,8,9,10\tBES\n")[0].chip)
    }
}
