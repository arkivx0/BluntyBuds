package io.github.arkivx0.bluntybuds.proto

import org.junit.Assert.*
import org.junit.Test

private fun h(s: String) = s.replace(" ", "").chunked(2).map { it.toInt(16).toByte() }.toByteArray()

class ProtoATest {
    @Test fun emptyGetIsFourBytes() {
        assertArrayEquals(h("00 02 00 00"), ProtoA.get(ProtoA.GET_BATTERY))
        assertArrayEquals(h("00 01 00 00"), ProtoA.get(ProtoA.GET_FW_VERSION))
    }
    @Test fun setGameFrame() {
        assertArrayEquals(h("10 06 01 01 01"), ProtoA.setGame(true))
        assertArrayEquals(h("10 06 01 00 00"), ProtoA.setGame(false))
    }
    @Test fun checksumWraps() {
        val f = ProtoA.frame(0x1003, h("FF FF 02"))
        assertEquals(0x00.toByte(), f.last()) // 0xFF+0xFF+0x02 = 0x200 -> 0x00
        assertNotNull(ProtoA.parseFrame(f))
    }
    @Test fun eqPayloadLayout() {
        val g = floatArrayOf(1f, -2.5f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 12f)
        val p = ProtoA.eqPayload(g)
        assertEquals(21, p.size)
        assertEquals(10.toByte(), p[0]); assertEquals((-25).toByte(), p[1]); assertEquals(120.toByte(), p[9])
        assertEquals(5.toByte(), p[10]); assertEquals(20.toByte(), p[19]); assertEquals(1.toByte(), p[20])
        val f = ProtoA.setEq(g)
        assertEquals(0x10.toByte(), f[0]); assertEquals(0x0B.toByte(), f[1]); assertEquals(21.toByte(), f[2])
    }
    @Test fun balanceClampAndSign() {
        assertArrayEquals(h("10 26 01 FD FD"), ProtoA.setBalance(-3))
        assertArrayEquals(h("10 26 01 0B 0B"), ProtoA.setBalance(99))
    }
    @Test fun touchPayloadDefaultsForUnknown() {
        val m = TouchMap(intArrayOf(2, -1, 4, 6, 0), intArrayOf(1, 2, 4, 5, 0))
        assertArrayEquals(h("02 03 04 06 00 01 02 04 05 00"), ProtoA.touchPayload(m))
    }
    @Test fun decodeBattery() {
        assertEquals(Event.Battery(80, 75, snapshot = true), ProtoA.decode(ProtoA.frame(0x0002, byteArrayOf(80, 75))))
        assertEquals(Event.Battery(50, null, snapshot = true), ProtoA.decode(ProtoA.frame(0x200E, byteArrayOf(50))))
        assertEquals(Event.Battery(80, 75, chargingLeft = true, snapshot = true), ProtoA.decode(ProtoA.frame(0x0002, byteArrayOf(0xD0.toByte(), 75))))
        assertEquals(Event.Battery(null, 40, snapshot = true), ProtoA.decode(ProtoA.frame(0x0002, byteArrayOf(0xFF.toByte(), 40))))
        assertEquals("R 40%", BatteryText.format(null, 40))
        assertEquals("L 80% charging", BatteryText.format(80, null, chargingLeft = true))
        assertEquals(40, BatteryText.lowest(null, 40))
        assertNull(BatteryText.lowest(null, null))
    }
    @Test fun decodeVersion() {
        assertEquals(Event.Firmware("1.2.3", "1.2.4"), ProtoA.decode(ProtoA.frame(0x0001, h("01 02 03 01 02 04"))))
        assertEquals(Event.Firmware("2.0.200", null), ProtoA.decode(ProtoA.frame(0x0001, h("02 00 C8"))))
    }
    @Test fun decodeTouchEqGameBalance() {
        val t = ProtoA.decode(ProtoA.frame(0x0003, h("01 03 04 06 00 01 02 04 05 00"))) as Event.Touch
        assertEquals(TouchMap.defaults(), t.map)
        val t8 = ProtoA.decode(ProtoA.frame(0x0003, h("01 03 04 06 01 02 04 05"))) as Event.Touch
        assertEquals(-1, t8.map.left[4]); assertEquals(5, t8.map.right[3])
        val e = ProtoA.decode(ProtoA.frame(0x000B, h("0A F6 00 00 00 00 00 00 00 14"))) as Event.Eq
        assertEquals(listOf(1f, -1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 2f), e.gains)
        assertEquals(Event.Game(true), ProtoA.decode(ProtoA.frame(0x0006, h("01"))))
        assertEquals(Event.Balance(-2), ProtoA.decode(ProtoA.frame(0x0026, h("FE"))))
    }
    @Test fun badFramesRejected() {
        assertNull(ProtoA.decode(h("00 02 02 50 4B 00")))  // bad checksum
        assertNull(ProtoA.decode(h("00 02 05 50 4B 9B")))  // bad length
        assertNull(ProtoA.decode(h("00 02")))
    }
    @Test fun ackStatus() {
        assertEquals(Event.Ack(0x1006, true), ProtoA.decode(ProtoA.frame(0x1006, h("00"))))
        assertEquals(Event.Ack(0x1006, false), ProtoA.decode(ProtoA.frame(0x1006, h("02"))))
    }
}

class ProtoJTest {
    @Test fun knownFramesObservedOnDevice() {
        // battery request: "AA1400"+chk, chk = (AA+14+00)%256 = BE
        assertArrayEquals(h("AA 14 00 BE"), ProtoJ.get(ProtoJ.BATTERY_LEFT))
        assertArrayEquals(h("AA 15 00 BF"), ProtoJ.get(ProtoJ.BATTERY_RIGHT))
        assertArrayEquals(h("AA 19 00 C3"), ProtoJ.get(ProtoJ.GAME_GET))
        assertArrayEquals(h("AA 42 00 EC"), ProtoJ.get(ProtoJ.EQ_GET))
        assertArrayEquals(h("AA 66 00 10"), ProtoJ.get(ProtoJ.VERSION))
        assertArrayEquals(h("AA 12 01 01 BE"), ProtoJ.setGame(true))
        assertArrayEquals(h("AA 12 01 00 BD"), ProtoJ.setGame(false))
    }
    @Test fun eqPayload() {
        val g = FloatArray(10); g[0] = 3f; g[9] = -12f
        val p = ProtoJ.eqPayload(g)
        assertEquals(80, p.size)
        // band0: freq 20 (14 00), gain 3dB -> 150 (96 00), Q 0.5 -> 5 (05 00), type 1 (01 00)
        assertArrayEquals(h("14 00 96 00 05 00 01 00"), p.copyOfRange(0, 8))
        // band9: freq 12800 (00 32), gain -12 -> 0 (00 00), Q 2.0 -> 20 (14 00)
        assertArrayEquals(h("00 32 00 00 14 00 01 00"), p.copyOfRange(72, 80))
        val f = ProtoJ.setEq(g)
        assertEquals(0x50.toByte(), f[2]); assertEquals(84, f.size)
    }
    @Test fun touchPayloadKeyOrder() {
        val p = ProtoJ.touchPayload(TouchMap.defaults())
        // key1 Lsingle play(8), key2 Rsingle play(8), key3 Ldouble prev(7), key4 Rdouble next(6),
        // key5 Llong vol-(2), key6 Rlong vol+(1), key7 Ltriple assistant(160), key8 Rtriple assistant(160)
        assertArrayEquals(h("01 08 02 08 03 07 04 06 05 02 06 01 07 A0 08 A0"), p)
        val skip = TouchMap(intArrayOf(-1, -1, -1, -1, 0), intArrayOf(1, -1, -1, -1, 0))
        assertArrayEquals(h("02 08"), ProtoJ.touchPayload(skip))
    }
    @Test fun splitMultipleRepliesInOneNotification() {
        val raw = h("A5 14 01 50 0A" + "A5 15 01 4B 06")
        val ev = ProtoJ.decode(raw)
        assertEquals(listOf(Event.Battery(80, null, reportsRight = false), Event.Battery(null, 75, reportsLeft = false)), ev)
        val fr = ProtoJ.splitFrames(raw)
        assertTrue(fr.all { it.third })
    }
    @Test fun decodeVersionTouchEqGame() {
        assertEquals(Event.Firmware("1.2.3", "1.2.4"), ProtoJ.decode(h("A5 66 05 01 12 03 12 04 00"))[0])
        val t = ProtoJ.decode(h("A5 39 10 01 08 02 08 03 07 04 06 05 02 06 01 07 A0 08 A0 00"))[0] as Event.Touch
        assertEquals(TouchMap.defaults().left.take(4), t.map.left.take(4))
        assertEquals(TouchMap.defaults().right.take(4), t.map.right.take(4))
        val eq = ProtoJ.decode(h("A5 42 10 14 00 96 00 05 00 01 00 32 00 6E 00 06 00 01 00 00"))[0] as Event.Eq
        assertEquals(listOf(3f, -1f), eq.gains)
        assertEquals(Event.Game(true), ProtoJ.decode(h("A5 19 01 01 00"))[0])
        assertEquals(Event.Ack(0x41, true), ProtoJ.decode(h("A5 41 01 01 00"))[0])
    }
    @Test fun truncatedIgnored() {
        assertTrue(ProtoJ.decode(h("A5 14 01")).isEmpty())
    }
}

class AdParserTest {
    @Test fun parsesWrWithBatch() {
        // flags AD, then manufacturer AD: len 0x0B, type FF, mac(6) id BE 'W' 'R' 0D
        val rec = h("02 01 06" + "0B FF 11 22 33 44 55 66 BE 57 52 0D" + "05 09 54 4F 5A 4F")
        val ad = AdParser.parse(rec)!!
        assertEquals("11:22:33:44:55:66", ad.classicMac); assertEquals("BE0D", ad.mark)
        assertTrue(ad.mark in AdParser.A1_MARKS)
        assertEquals(Protocol.J, AdParser.protocolHint(ad.mark))
    }
    @Test fun parsesWithoutBatchAndWoer() {
        assertEquals("61", AdParser.parse(h("0A FF 01 02 03 04 05 06 61 57 52"))!!.mark)
        assertEquals("45", AdParser.parse(h("0C FF 01 02 03 04 05 06 45 57 4F 45 52"))!!.mark)
        assertNull(AdParser.parse(h("05 FF 4C 00 01 02")))
        assertNull(AdParser.parse(h("FF FF")))
        assertEquals(Protocol.A, AdParser.protocolHint("010E"))
    }
    @Test fun ancEncodeDecode() {
        assertArrayEquals(h("1004010000"), ProtoA.setAnc(AncMode.OFF))
        assertArrayEquals(h("1004010101"), ProtoA.setAnc(AncMode.ANC))
        assertArrayEquals(h("1005010101"), ProtoA.setAnc(AncMode.TRANSPARENCY))
        assertArrayEquals(h("00300000"), ProtoA.get(ProtoA.GET_ANC_MODE))
        assertEquals(Event.Anc(2), ProtoA.decode(h("003002020002")))
        assertEquals(Event.Anc(1), ProtoA.decode(h("2030010101")))
        assertEquals(Event.Anc(2), ProtoA.decode(h("2010010101")))
    }
    @Test fun modelDetect() {
        assertEquals(DeviceModel.NC9, Models.detect("TOZO NC9"))
        assertEquals(DeviceModel.A1, Models.detect("TOZO-A1"))
        assertEquals("NC9", Models.detect(null, "B1").name)
        assertEquals("A1", Models.detect("", "BE0D").name); assertEquals(Protocol.J, Models.detect("", "BE0D").hint)
        assertTrue(Models.detect("Speaker").isUnknown)
        assertEquals("T6S", Models.detect("TOZO T6S").name)
        assertEquals("T6", Models.detect("TOZO-T6").name)
        assertEquals("NC9Pro", Models.detect("TOZO NC9 Pro").name)
        assertEquals("AeroSound6Pro", Models.detect("TOZO AeroSound 6 Pro").name)
        assertTrue(Models.detect("Random T60 speaker").isUnknown)
        assertFalse(Models.detect("TOZO T6").tested); assertTrue(Models.detect("TOZO NC9").tested)
        val g = Models.detect("whatever", "D40D")
        assertEquals("NC20", g.name); assertTrue(g.f.anc); assertEquals(Protocol.A, g.hint)
        assertEquals(Protocol.J, Models.detect(null, "060E").hint); assertFalse(Models.detect(null, "060E").f.anc)
        assertTrue(Models.detect(null, "0F0E").f.ancLevel); assertFalse(Models.detect(null, "0C0E").f.ancLevel)
        assertNull(Models.byMark("12")); assertNull(Models.byMark("1609"))
        assertFalse(Models.detect("TOZO T6").f.game)
        assertTrue(Models.features(DeviceModel.NC9).anc); assertFalse(Models.features(DeviceModel.A1).anc)
    }
    @Test fun ancLevel() {
        assertArrayEquals(h("100F011414"), ProtoA.setAncLevel(10))
        assertArrayEquals(h("100F010202"), ProtoA.setAncLevel(0))
        assertArrayEquals(h("100E010101"), ProtoA.setAnc(AncMode.CUSTOM))
        assertEquals(Event.AncLevel(3), ProtoA.decode(h("000F010606")))
        assertTrue(Models.features(DeviceModel.NC9).ancLevel); assertFalse(Models.features(DeviceModel.A1).ancLevel)
    }
}
