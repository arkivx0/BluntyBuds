package io.github.arkivx0.bluntybuds.proto

/** Which wire protocol the earbuds speak. */
enum class Protocol { A, J }

/**
 * Touch actions. [a] = byte used by protocol A,
 * [j] = value used by protocol J.
 */
enum class TouchAction(val a: Int, val j: Int, val label: String) {
    NONE(0, 0, "None"),
    PLAY_PAUSE(1, 8, "Play / Pause"),
    NEXT(2, 6, "Next track"),
    PREVIOUS(3, 7, "Previous track"),
    ASSISTANT(4, 160, "Voice assistant"),
    VOLUME_UP(5, 1, "Volume +"),
    VOLUME_DOWN(6, 2, "Volume -"),
    GAME_MODE(7, 100, "Toggle Low-Latency"),
    ANC_SWITCH(8, 3, "Switch noise mode"),
    ANC_OFF(10, 10, "Noise off");

    companion object {
        fun fromA(b: Int): TouchAction? = entries.firstOrNull { it.a == b }
        fun fromJ(v: Int): TouchAction? = entries.firstOrNull { it.j == v }
    }
}

/** Gesture slots. Index = position in the protocol-A 5-byte per-side array (slot 4 is unknown and is preserved). */
enum class Gesture(val slot: Int, val label: String) {
    SINGLE(0, "Single tap"), DOUBLE(1, "Double tap"), TRIPLE(2, "Triple tap"), LONG(3, "Long press")
}

/**
 * Per-side touch map, stored as protocol-A action bytes (5 slots per side). A value of -1 means
 * "unknown / not reported" and is never sent to protocol-J devices.
 */
data class TouchMap(val left: IntArray, val right: IntArray) {
    init { require(left.size == 5 && right.size == 5) }
    fun copyMap() = TouchMap(left.copyOf(), right.copyOf())
    override fun equals(other: Any?) = other is TouchMap && left.contentEquals(other.left) && right.contentEquals(other.right)
    override fun hashCode() = left.contentHashCode() * 31 + right.contentHashCode()

    companion object {
        /** Factory defaults  (non-ANC model). */
        fun defaults() = TouchMap(intArrayOf(1, 3, 4, 6, 0), intArrayOf(1, 2, 4, 5, 0))
    }
}

/** Default 10-band EQ grid (centre frequencies and Q). */
object EqGrid {
    val FREQS = intArrayOf(20, 50, 100, 200, 400, 800, 1600, 3200, 6400, 12800)
    val QS = floatArrayOf(0.5f, 0.6f, 0.7f, 0.8f, 1.0f, 1.2f, 1.4f, 1.6f, 1.8f, 2.0f)
    const val MIN_DB = -12f
    const val MAX_DB = 12f
}

sealed class Event {
    /**
     * One battery report. [snapshot] replaces both sides (a missing side is not in use).
     * A partial report only changes the sides it names.
     */
    data class Battery(
        val left: Int?, val right: Int?,
        val chargingLeft: Boolean = false, val chargingRight: Boolean = false,
        val snapshot: Boolean = false, val reportsLeft: Boolean = true, val reportsRight: Boolean = true
    ) : Event()
    /** Charging-case battery. [level] null = not reported (case closed or not supported). */
    data class CaseBattery(val level: Int?, val charging: Boolean = false) : Event()
    data class Firmware(val left: String, val right: String?) : Event()
    data class Game(val on: Boolean) : Event()
    data class Touch(val map: TouchMap) : Event()
    data class Eq(val gains: List<Float>) : Event()
    data class Balance(val value: Int) : Event()
    data class Ack(val cmd: Int, val ok: Boolean) : Event()
    data class Anc(val mode: Int) : Event()
    data class AncLevel(val level: Int) : Event()
    data class Raw(val cmd: Int, val payload: ByteArray) : Event()
}

internal fun ByteArray.hex() = joinToString("") { "%02X".format(it) }

/** Battery percent text. A side that was not reported is omitted, so one earbud does not show a stale partner. */
object BatteryText {
    /** 0–100 is a percent. The high bit plus a percent of 0–100 means charging. 0xFF and other out-of-range values mean that earbud is absent. */
    fun parse(raw: Int): Pair<Int?, Boolean> {
        val b = raw and 0xFF
        if (b == 0xFF) return null to false
        if (b > 100 && b and 0x80 != 0) {
            val p = b and 0x7F
            if (p <= 100) return p to true
        }
        if (b > 100) return null to false
        return b to false
    }

    fun format(left: Int?, right: Int?, chargingLeft: Boolean = false, chargingRight: Boolean = false): String {
        fun one(name: String, p: Int?, c: Boolean) = p?.let { "$name $it%" + if (c) " charging" else "" }
        return listOfNotNull(one("L", left, chargingLeft), one("R", right, chargingRight)).joinToString("  ").ifEmpty { "—" }
    }

    /** Lowest present earbud. Null when neither side is in use. */
    fun lowest(left: Int?, right: Int?) = listOfNotNull(left, right).minOrNull()

    /** Solo Mode: "L" or "R" when exactly one earbud is in use, null when both or neither are. */
    fun solo(left: Int?, right: Int?): String? = when {
        left != null && right == null -> "L"
        left == null && right != null -> "R"
        else -> null
    }

    /** The active earbud only, e.g. "L 82%" or "R 40% charging". Both present: "L 82%  R 75%". */
    fun active(left: Int?, right: Int?, chargingLeft: Boolean = false, chargingRight: Boolean = false) = format(left, right, chargingLeft, chargingRight)

    /** Small case figure, or null when the case level is unknown. */
    fun case(level: Int?, charging: Boolean = false) = level?.let { "Case $it%" + if (charging) " charging" else "" }
}

/** ANC modes as used by protocol A (GET 0x0030 payload[0]). */
enum class AncMode(val code: Int, val label: String) {
    OFF(0, "Off"), ANC(1, "Quiet"), TRANSPARENCY(2, "Aware"),
    WIND(3, "Wind"), LEISURE(4, "Leisure"), CUSTOM(5, "Custom"), ADAPTIVE(6, "Adaptive");
    companion object { fun of(c: Int) = values().firstOrNull { it.code == c } }
}

/** A detected model: display name, optional ad mark, protocol hint and the feature set to show. */
data class DeviceModel(val name: String, val mark: String?, val chip: String, val hint: Protocol?, val f: Models.Features, val tested: Boolean) {
    val isUnknown get() = this == UNKNOWN
    override fun toString() = name
    companion object {
        val UNKNOWN = DeviceModel("Earbuds", null, "?", null, Models.Features(false, true, true, true, true), false)
        /** Hand-verified on real hardware (name-only detection). */
        val A1 = DeviceModel("A1", null, "?", null, Models.Features(false, true, true, true, true), true)
        val NC9 = DeviceModel("NC9", null, "?", null, Models.Features(true, true, true, true, true, ancLevel = true), true)
    }
}

/** Model detection + per-model features (see ModelTable.kt). */
object Models {
    data class Features(val anc: Boolean, val game: Boolean, val eq: Boolean, val touch: Boolean, val balance: Boolean, val ancLevel: Boolean = false)
    private val TESTED = setOf("A1", "NC9")
    private fun row(r: ModelRow) = DeviceModel(r.name, r.mark, r.chip, r.proto, Features(r.anc, r.game, r.eq, r.touch, r.balance, r.ancLevel), r.name in TESTED)
    private val namesLongestFirst by lazy { MODEL_TABLE.map { it.name }.distinct().sortedByDescending { it.length } }
    val supportedNames get() = namesLongestFirst.sorted()

    fun byMark(mark: String?): DeviceModel? =
        if (mark.isNullOrEmpty()) null else MODEL_TABLE.firstOrNull { it.mark.equals(mark, true) }?.let(::row)

    /** Family from the BT name; features = intersection of all variants (A1/NC9 use hardware-verified sets). */
    fun byName(name: String?): DeviceModel? {
        if (name.isNullOrBlank()) return null
        val n = name.replace(" ", "").replace("-", "").replace("_", "")
        for (fam in namesLongestFirst) {
            val f = fam.replace(" ", "")
            var i = n.indexOf(f, ignoreCase = true)
            while (i >= 0) {
                val leftOk = i == 0 || !n[i - 1].isLetterOrDigit() || n.substring(0, i).endsWith("TOZO", true)
                val rightOk = i + f.length == n.length || !n[i + f.length].isLetterOrDigit()
                if (leftOk && rightOk) return family(fam)
                i = n.indexOf(f, i + 1, ignoreCase = true)
            }
        }
        return null
    }

    fun family(fam: String): DeviceModel {
        if (fam.equals("A1", true)) return DeviceModel.A1
        if (fam.equals("NC9", true)) return DeviceModel.NC9
        val v = MODEL_TABLE.filter { it.name.equals(fam, true) }
        val p = v.map { it.proto }.distinct()
        return DeviceModel(v[0].name, null, v.map { it.chip }.distinct().joinToString("/"), p.singleOrNull(),
            Features(v.all { it.anc }, v.all { it.game }, v.all { it.eq }, v.all { it.touch }, v.all { it.balance }, v.all { it.ancLevel }), fam in TESTED)
    }

    fun detect(name: String?, mark: String? = null): DeviceModel = byMark(mark) ?: byName(name) ?: DeviceModel.UNKNOWN
    fun features(m: DeviceModel) = m.f
}
