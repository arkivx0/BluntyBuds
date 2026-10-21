package io.github.arkivx0.bluntybuds.proto

/**
 * Vendor advertisement parser.
 * Manufacturer-specific AD (type 0xFF) content: classicMac[6] id[1] then "WR" batch[1] or "WOER".
 * The device "mark" is id hex, plus batch hex when the AD carries one (e.g. "BE"+"0D" = "BE0D").
 */
object AdParser {
    data class VendorAd(val classicMac: String, val mark: String)

    fun parse(scanRecord: ByteArray?): VendorAd? {
        if (scanRecord == null) return null
        var i = 0
        while (i < scanRecord.size) {
            val len = scanRecord[i].toInt() and 0xFF
            if (len == 0 || i + len >= scanRecord.size) break
            val type = scanRecord[i + 1].toInt() and 0xFF
            if (type == 0xFF) {
                val data = scanRecord.copyOfRange(i + 2, i + 1 + len)
                parseManufacturer(data)?.let { return it }
            }
            i += len + 1
        }
        return null
    }

    /** [data] = manufacturer AD payload (bytes after the 0xFF type byte, company-id bytes included). */
    fun parseManufacturer(data: ByteArray): VendorAd? {
        if (data.size < 9) return null
        val mac = (0 until 6).joinToString(":") { "%02X".format(data[it]) }
        val id = "%02X".format(data[6])
        if (data[7] == 'W'.code.toByte() && data[8] == 'R'.code.toByte()) {
            val batch = if (data.size >= 10) "%02X".format(data[9]) else ""
            return VendorAd(mac, id + batch)
        }
        if (data.size >= 11 && data[7] == 'W'.code.toByte() && data[8] == 'O'.code.toByte() &&
            data[9] == 'E'.code.toByte() && data[10] == 'R'.code.toByte()) return VendorAd(mac, id)
        return null
    }

    /** A1 id codes seen in adverts (JX family, JL = JieLi, ZK = Bluetrum). */
    val A1_MARKS = setOf("61", "BE0D", "050E", "0B0E", "010E", "020E", "040E", "190E", "1B0E", "D90D")
    private val J_HINT = setOf("61", "BE0D", "050E", "0B0E", "72", "52", "55", "62", "66", "81", "88", "92")

    /** Weak hint only; the GATT service check at connect time is authoritative. */
    fun protocolHint(mark: String): Protocol = if (mark.uppercase() in J_HINT) Protocol.J else Protocol.A
}
