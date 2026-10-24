package io.github.arkivx0.bluntybuds.proto

/**
 * Protocol A: vendor GATT protocol. Frame: cmd(2, BE) len(1) payload sum(payload, 1).
 * Write char 4157b611-..., notify char 4157b612-... (UUID suffix d129-aeb5-859b-d547e30ffcb1).
 * Frame: cmd_hi cmd_lo len payload[len] checksum, checksum = (sum of payload bytes) & 0xFF.
 * A request with no payload is sent as cmd_hi cmd_lo 00 00.
 * cmd_hi: 0x00 get, 0x10 set, 0x20 unsolicited notify (0x01/0x11/0x21 = charging case).
 */
object ProtoA {
    const val GET_FW_VERSION = 0x0001
    const val GET_BATTERY = 0x0002
    const val GET_BUTTONS = 0x0003
    const val SET_BUTTONS = 0x1003
    const val GET_GAME = 0x0006
    const val SET_GAME = 0x1006
    const val GET_EQ = 0x000B
    const val SET_EQ = 0x100B
    const val RESET_EQ = 0x100C
    const val GET_BALANCE = 0x0026
    const val SET_BALANCE = 0x1026
    const val NOTIFY_CONN = 0x200D
    const val NOTIFY_BATTERY = 0x200E
    const val NOTIFY_GAME = 0x200F
    const val NOTIFY_EQ = 0x2012
    const val NOTIFY_BALANCE = 0x201D
    const val SET_ANC_ONOFF = 0x1004
    const val SET_HT_ONOFF = 0x1005
    const val GET_ANC_MODE = 0x0030
    const val NOTIFY_ANC_MODE = 0x2030
    const val NOTIFY_HT = 0x2010
    const val NOTIFY_ANC_ONOFF = 0x2011
    const val SET_CUSTOM_ANC_ONOFF = 0x100E
    const val GET_CUSTOM_ANC_LEVEL = 0x000F
    const val SET_CUSTOM_ANC_LEVEL = 0x100F
    const val ANC_LEVEL_MIN = 1
    const val ANC_LEVEL_MAX = 10

    const val WRITE_UUID_PREFIX = "4157b611"
    const val NOTIFY_UUID_PREFIX = "4157b612"
    const val UUID_SUFFIX = "-d129-aeb5-859b-d547e30ffcb1"

    fun frame(cmd: Int, payload: ByteArray = ByteArray(0)): ByteArray {
        require(cmd in 0..0xFFFF && payload.size <= 255)
        val out = ByteArray(payload.size + 4)
        out[0] = (cmd shr 8).toByte(); out[1] = cmd.toByte(); out[2] = payload.size.toByte()
        payload.copyInto(out, 3)
        out[out.size - 1] = payload.sum().toByte()
        return out
    }

    private fun ByteArray.sum(): Int = fold(0) { a, b -> a + b } and 0xFF

    /** Returns (cmd, payload) or null if length/checksum do not match. */
    fun parseFrame(raw: ByteArray): Pair<Int, ByteArray>? {
        if (raw.size < 4) return null
        val len = raw[2].toInt() and 0xFF
        if (len + 4 != raw.size) return null
        val payload = raw.copyOfRange(3, 3 + len)
        if ((payload.sum() and 0xFF) != (raw[raw.size - 1].toInt() and 0xFF)) return null
        val cmd = ((raw[0].toInt() and 0xFF) shl 8) or (raw[1].toInt() and 0xFF)
        return cmd to payload
    }

    fun get(cmd: Int) = frame(cmd)
    /** Noise mode: off = 1004{0}, ANC = 1004{1}, transparency = 1005{1}. */
    fun setAnc(m: AncMode): ByteArray = when (m) {
        AncMode.OFF -> frame(SET_ANC_ONOFF, byteArrayOf(0))
        AncMode.ANC -> frame(SET_ANC_ONOFF, byteArrayOf(1))
        AncMode.TRANSPARENCY -> frame(SET_HT_ONOFF, byteArrayOf(1))
        AncMode.CUSTOM -> frame(SET_CUSTOM_ANC_ONOFF, byteArrayOf(1))
        else -> throw IllegalArgumentException("unsupported $m")
    }
    /** Custom ANC depth, UI level 1-10; device byte = level*2. */
    fun setAncLevel(level: Int) = frame(SET_CUSTOM_ANC_LEVEL, byteArrayOf((level.coerceIn(ANC_LEVEL_MIN, ANC_LEVEL_MAX) * 2).toByte()))
    fun ancLevelFromRaw(raw: Int) = (raw / 2).coerceIn(ANC_LEVEL_MIN, ANC_LEVEL_MAX)
    fun setGame(on: Boolean) = frame(SET_GAME, byteArrayOf(if (on) 1 else 0))
    /** Balance: a single signed byte, UI range -11..+11. */
    fun setBalance(v: Int) = frame(SET_BALANCE, byteArrayOf(v.coerceIn(-11, 11).toByte()))
    fun resetEq() = frame(RESET_EQ)

    /** EQ set payload: 10 x gain*10, 10 x Q*10, save flag. */
    fun eqPayload(gains: FloatArray, qs: FloatArray = EqGrid.QS, save: Boolean = true): ByteArray {
        require(gains.size == 10 && qs.size == 10)
        val b = ByteArray(21)
        for (i in 0 until 10) b[i] = Math.round(gains[i].coerceIn(EqGrid.MIN_DB, EqGrid.MAX_DB) * 10f).toByte()
        for (i in 0 until 10) b[10 + i] = Math.round(qs[i] * 10f).toByte()
        b[20] = if (save) 1 else 0
        return b
    }
    fun setEq(gains: FloatArray, qs: FloatArray = EqGrid.QS, save: Boolean = true) = frame(SET_EQ, eqPayload(gains, qs, save))

    /** 5 left bytes then 5 right bytes. Unknown (-1) slots fall back to defaults. */
    fun touchPayload(map: TouchMap): ByteArray {
        val d = TouchMap.defaults()
        return ByteArray(10) { i ->
            val v = if (i < 5) map.left[i] else map.right[i - 5]
            val dv = if (i < 5) d.left[i] else d.right[i - 5]
            (if (v < 0) dv else v).toByte()
        }
    }
    fun setTouch(map: TouchMap) = frame(SET_BUTTONS, touchPayload(map))

    /** Protocol A sends both buds in one packet. One byte is the left bud only; two or more use the first two. */
    private fun decodeBattery(p: ByteArray): Event? {
        if (p.isEmpty()) return null
        val (l, cl) = BatteryText.parse(p[0].toInt())
        if (p.size == 1) return Event.Battery(l, null, chargingLeft = cl, snapshot = true)
        val (r, cr) = BatteryText.parse(p[1].toInt())
        return Event.Battery(l, r, chargingLeft = cl, chargingRight = cr, snapshot = true)
    }

    fun decode(raw: ByteArray): Event? {
        val (cmd, p) = parseFrame(raw) ?: return null
        return decodePayload(cmd, p)
    }

    fun decodePayload(cmd: Int, p: ByteArray): Event = when (cmd) {
        GET_BATTERY, NOTIFY_BATTERY -> decodeBattery(p)?.let { it } ?: Event.Raw(cmd, p)
        GET_FW_VERSION -> when (p.size) {
            3 -> Event.Firmware(ver(p, 0), null)
            6 -> Event.Firmware(ver(p, 0), ver(p, 3))
            else -> Event.Raw(cmd, p)
        }
        GET_GAME, NOTIFY_GAME -> if (p.isNotEmpty()) Event.Game(p[0].toInt() != 0) else Event.Raw(cmd, p)
        GET_BUTTONS -> decodeTouch(p)?.let { Event.Touch(it) } ?: Event.Raw(cmd, p)
        GET_EQ, NOTIFY_EQ -> if (p.size in 1..20) Event.Eq(p.take(minOf(10, p.size)).map { it / 10f }) else Event.Raw(cmd, p)
        GET_CUSTOM_ANC_LEVEL -> if (p.isNotEmpty()) Event.AncLevel(ancLevelFromRaw(p[0].toInt() and 0xFF)) else Event.Raw(cmd, p)
        GET_ANC_MODE, NOTIFY_ANC_MODE -> if (p.isNotEmpty()) Event.Anc(p[0].toInt() and 0xFF) else Event.Raw(cmd, p)
        NOTIFY_ANC_ONOFF -> if (p.isNotEmpty()) Event.Anc(if (p[0].toInt() != 0) 1 else 0) else Event.Raw(cmd, p)
        NOTIFY_HT -> if (p.isNotEmpty()) Event.Anc(if (p[0].toInt() != 0) 2 else 0) else Event.Raw(cmd, p)
        GET_BALANCE, NOTIFY_BALANCE -> if (p.isNotEmpty()) Event.Balance(p[0].toInt()) else Event.Raw(cmd, p)
        else -> if ((cmd shr 8) == 0x10) Event.Ack(cmd, !(p.isNotEmpty() && p[0].toInt() == 2)) else Event.Raw(cmd, p)
    }

    private fun ver(p: ByteArray, o: Int) = "${p[o].toInt() and 0xFF}.${p[o + 1].toInt() and 0xFF}.${p[o + 2].toInt() and 0xFF}"

    /** Touch reply: even length -> first half left, second half right. */
    private fun decodeTouch(p: ByteArray): TouchMap? {
        if (p.isEmpty() || p.size % 2 == 1) return null
        val half = p.size / 2
        fun side(off: Int) = IntArray(5) { i -> if (i < half) p[off + i].toInt() else -1 }
        return TouchMap(side(0), side(half))
    }
}
