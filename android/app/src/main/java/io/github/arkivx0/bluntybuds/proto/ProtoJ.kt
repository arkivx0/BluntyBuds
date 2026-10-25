package io.github.arkivx0.bluntybuds.proto

/**
 * Protocol J.
 * GATT service 0000faa0-..., write char faa1, notify char faa2.
 * Request: AA cmd len payload checksum; checksum = sum of all preceding bytes (incl. AA) mod 256.
 * Reply:   A5 cmd len payload checksum (several replies may arrive in one notification).
 */
object ProtoJ {
    const val BATTERY_LEFT = 0x14
    const val BATTERY_RIGHT = 0x15
    const val CONN_STATUS_NOTIFY = 0x16
    const val DEVICE_INFO = 0x17
    const val CONN_STATUS = 0x18
    const val GAME_SET = 0x12
    const val GAME_GET = 0x19
    const val BUTTON_SET = 0x38
    const val BUTTON_GET = 0x39
    const val EQ_SET = 0x41
    const val EQ_GET = 0x42
    const val VERSION = 0x66
    /** Gets sent on connect/refresh, in the 1.4.4 order. */
    val STATUS_QUERIES = listOf(BATTERY_LEFT, BATTERY_RIGHT, VERSION, GAME_GET, BUTTON_GET, EQ_GET)

    const val SERVICE = "0000faa0-0000-1000-8000-00805f9b34fb"
    const val WRITE = "0000faa1-0000-1000-8000-00805f9b34fb"
    const val NOTIFY = "0000faa2-0000-1000-8000-00805f9b34fb"

    /** Protocol-J key ids. */
    private val KEY_TO_SLOT = mapOf(1 to (true to 0), 2 to (false to 0), 3 to (true to 1), 4 to (false to 1),
        5 to (true to 3), 6 to (false to 3), 7 to (true to 2), 8 to (false to 2))
    const val SKIP = 209

    fun frame(cmd: Int, payload: ByteArray = ByteArray(0)): ByteArray {
        require(cmd in 0..0xFF && payload.size <= 255)
        val out = ByteArray(payload.size + 4)
        out[0] = 0xAA.toByte(); out[1] = cmd.toByte(); out[2] = payload.size.toByte()
        payload.copyInto(out, 3)
        var s = 0
        for (i in 0 until out.size - 1) s += out[i].toInt() and 0xFF
        out[out.size - 1] = (s % 256).toByte()
        return out
    }

    fun get(cmd: Int) = frame(cmd)
    fun setGame(on: Boolean) = frame(GAME_SET, byteArrayOf(if (on) 1 else 0))

    private fun le16(v: Int) = byteArrayOf(v.toByte(), (v shr 8).toByte())

    /** EQ set: per band freq, gain*10+120, Q*10, type=1 — each uint16 little-endian. */
    fun eqPayload(gains: FloatArray, freqs: IntArray = EqGrid.FREQS, qs: FloatArray = EqGrid.QS): ByteArray {
        require(gains.size == freqs.size && qs.size == freqs.size)
        val out = java.io.ByteArrayOutputStream()
        for (i in freqs.indices) {
            out.write(le16(freqs[i]))
            out.write(le16(Math.round(gains[i].coerceIn(EqGrid.MIN_DB, EqGrid.MAX_DB) * 10f) + 120))
            out.write(le16(Math.round(qs[i] * 10f)))
            out.write(le16(1))
        }
        return out.toByteArray()
    }
    fun setEq(gains: FloatArray) = frame(EQ_SET, eqPayload(gains))

    /** Button setting: (key, action) byte pairs; keys whose action is unknown are skipped. */
    fun touchPayload(map: TouchMap): ByteArray {
        val out = java.io.ByteArrayOutputStream()
        for (key in 1..8) {
            val (left, slot) = KEY_TO_SLOT.getValue(key)
            val av = if (left) map.left[slot] else map.right[slot]
            val action = TouchAction.fromA(av) ?: continue
            out.write(key); out.write(action.j)
        }
        return out.toByteArray()
    }
    fun setTouch(map: TouchMap) = frame(BUTTON_SET, touchPayload(map))

    /** Splits a notification into A5 frames: (cmd, payload, checksumOk). */
    fun splitFrames(raw: ByteArray): List<Triple<Int, ByteArray, Boolean>> {
        val res = ArrayList<Triple<Int, ByteArray, Boolean>>()
        var i = 0
        while (i + 3 < raw.size) {
            if ((raw[i].toInt() and 0xFF) != 0xA5) { i++; continue }
            val cmd = raw[i + 1].toInt() and 0xFF
            val len = raw[i + 2].toInt() and 0xFF
            val end = i + 3 + len
            if (end >= raw.size) break
            val payload = raw.copyOfRange(i + 3, end)
            var s = 0
            for (k in i until end) s += raw[k].toInt() and 0xFF
            res.add(Triple(cmd, payload, (s % 256) == (raw[end].toInt() and 0xFF)))
            i = end + 1
        }
        return res
    }

    fun decode(raw: ByteArray): List<Event> = splitFrames(raw).map { (cmd, p, _) -> decodePayload(cmd, p) }

    fun decodePayload(cmd: Int, p: ByteArray): Event = when (cmd) {
        BATTERY_LEFT -> if (p.size == 1) {
            val (pct, ch) = BatteryText.parse(p[0].toInt())
            Event.Battery(pct, null, chargingLeft = ch, reportsRight = false)
        } else Event.Raw(cmd, p)
        BATTERY_RIGHT -> if (p.size == 1) {
            val (pct, ch) = BatteryText.parse(p[0].toInt())
            Event.Battery(null, pct, chargingRight = ch, reportsLeft = false)
        } else Event.Raw(cmd, p)
        GAME_GET -> if (p.size == 1) Event.Game(p[0].toInt() == 1) else Event.Raw(cmd, p)
        GAME_SET, BUTTON_SET, EQ_SET -> Event.Ack(cmd, p.size == 1 && p[0].toInt() == 1)
        BUTTON_GET -> {
            val left = IntArray(5) { -1 }; val right = IntArray(5) { -1 }
            var k = 0
            while (k + 1 < p.size) {
                val key = p[k].toInt() and 0xFF; val act = p[k + 1].toInt() and 0xFF
                val slot = KEY_TO_SLOT[key]
                val ta = TouchAction.fromJ(act)
                if (slot != null && ta != null) { if (slot.first) left[slot.second] = ta.a else right[slot.second] = ta.a }
                k += 2
            }
            Event.Touch(TouchMap(left, right))
        }
        EQ_GET -> {
            val n = p.size / 8
            if (n == 0) Event.Raw(cmd, p) else Event.Eq((0 until n).map { i ->
                val o = i * 8
                val g = (p[o + 2].toInt() and 0xFF) or ((p[o + 3].toInt() and 0xFF) shl 8)
                (g - 120) / 10f
            })
        }
        VERSION -> if (p.size >= 5) Event.Firmware(ver(p[1], p[2]), ver(p[3], p[4])) else Event.Raw(cmd, p)
        else -> Event.Raw(cmd, p)
    }

    /** Firmware reply "A56605": hex "ABCD" -> "A.B.CD", each part hex -> decimal. */
    private fun ver(b1: Byte, b2: Byte): String {
        val x = b1.toInt() and 0xFF
        return "${x shr 4}.${x and 0xF}.${b2.toInt() and 0xFF}"
    }
}
