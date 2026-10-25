using BluntyBuds.Proto;
using Xunit;

public class ProtoTests
{
    static byte[] H(string s) => Convert.FromHexString(s.Replace(" ", ""));

    [Fact] public void A_EmptyGet() { Assert.Equal(H("00020000"), ProtoA.Get(ProtoA.GetBattery)); Assert.Equal(H("00010000"), ProtoA.Get(ProtoA.GetFwVersion)); }
    [Fact] public void A_Game() { Assert.Equal(H("1006010101"), ProtoA.SetGameMode(true)); Assert.Equal(H("1006010000"), ProtoA.SetGameMode(false)); }
    [Fact] public void A_ChecksumWrap() { var f = ProtoA.Frame(0x1003, H("FFFF02")); Assert.Equal(0, f[^1]); Assert.NotNull(ProtoA.ParseFrame(f)); }
    [Fact] public void A_Eq()
    {
        var g = new float[] { 1, -2.5f, 0, 0, 0, 0, 0, 0, 0, 12 };
        var p = ProtoA.EqPayload(g);
        Assert.Equal(21, p.Length); Assert.Equal(10, p[0]); Assert.Equal(unchecked((byte)-25), p[1]); Assert.Equal(120, p[9]);
        Assert.Equal(5, p[10]); Assert.Equal(14, p[16]); Assert.Equal(20, p[19]); Assert.Equal(1, p[20]);
        var f = ProtoA.SetEqGains(g); Assert.Equal(new byte[] { 0x10, 0x0B, 21 }, f[..3]);
    }
    [Fact] public void A_Balance() { Assert.Equal(H("102601FDFD"), ProtoA.SetBalanceValue(-3)); Assert.Equal(H("1026010B0B"), ProtoA.SetBalanceValue(99)); }
    [Fact] public void A_TouchPayload() => Assert.Equal(H("02030406000102040500"), ProtoA.TouchPayload(new TouchMap(new[] { 2, -1, 4, 6, 0 }, new[] { 1, 2, 4, 5, 0 })));
    [Fact] public void A_DecodeBatteryVersion()
    {
        Assert.Equal(new Event.Battery(80, 75, Snapshot: true), ProtoA.Decode(ProtoA.Frame(0x0002, new byte[] { 80, 75 })));
        Assert.Equal(new Event.Battery(50, null, Snapshot: true), ProtoA.Decode(ProtoA.Frame(0x200E, new byte[] { 50 })));
        Assert.Equal(new Event.Battery(80, 75, ChargingLeft: true, Snapshot: true), ProtoA.Decode(ProtoA.Frame(0x0002, new byte[] { 0xD0, 75 })));
        Assert.Equal(new Event.Battery(null, 40, Snapshot: true), ProtoA.Decode(ProtoA.Frame(0x0002, new byte[] { 0xFF, 40 })));
        Assert.Equal("R 40%", BatteryText.Format(null, 40));
        Assert.Equal("L 80% charging", BatteryText.Format(80, null, chargingLeft: true));
        Assert.Equal(40, BatteryText.Lowest(null, 40));
        Assert.Null(BatteryText.Lowest(null, null));
    }
    [Fact] public void SoloAndCase()
    {
        Assert.Equal("L", BatteryText.Solo(82, null));
        Assert.Equal("R", BatteryText.Solo(null, 40));
        Assert.Null(BatteryText.Solo(80, 75));
        Assert.Null(BatteryText.Solo(null, null));
        Assert.Equal("L 82%", BatteryText.Format(82, null));
        Assert.Equal("Case 60%", BatteryText.Case(60));
        Assert.Null(BatteryText.Case(null));
        Assert.Equal(new Event.CaseBattery(60), ProtoA.Decode(ProtoA.Frame(0x0102, new byte[] { 60 })));
        Assert.Equal(new Event.CaseBattery(null), ProtoA.Decode(ProtoA.Frame(0x210E, new byte[] { 0xFF })));
        Assert.Equal(new Event.Firmware("1.2.3", "1.2.4"), ProtoA.Decode(ProtoA.Frame(0x0001, H("010203010204"))));
        Assert.Equal(new Event.Firmware("2.0.200", null), ProtoA.Decode(ProtoA.Frame(0x0001, H("0200C8"))));
    }
    [Fact] public void A_DecodeOthers()
    {
        var t = (Event.Touch)ProtoA.Decode(ProtoA.Frame(0x0003, H("01030406000102040500")))!;
        Assert.Equal(TouchMap.Defaults(), t.Map);
        var eq = (Event.Eq)ProtoA.Decode(ProtoA.Frame(0x000B, H("0AF60000000000000014")))!;
        Assert.Equal(new[] { 1f, -1f, 0, 0, 0, 0, 0, 0, 0, 2f }, eq.Gains);
        Assert.Equal(new Event.Game(true), ProtoA.Decode(ProtoA.Frame(0x0006, H("01"))));
        Assert.Equal(new Event.Balance(-2), ProtoA.Decode(ProtoA.Frame(0x0026, H("FE"))));
        Assert.Equal(new Event.Ack(0x1006, false), ProtoA.Decode(ProtoA.Frame(0x1006, H("02"))));
    }
    [Fact] public void A_BadFrames() { Assert.Null(ProtoA.Decode(H("000202504B00"))); Assert.Null(ProtoA.Decode(H("000205504B9B"))); Assert.Null(ProtoA.Decode(H("0002"))); }

    [Fact] public void J_KnownFrames()
    {
        Assert.Equal(H("AA1400BE"), ProtoJ.Get(ProtoJ.BatteryLeft));
        Assert.Equal(H("AA1500BF"), ProtoJ.Get(ProtoJ.BatteryRight));
        Assert.Equal(H("AA1900C3"), ProtoJ.Get(ProtoJ.GameGet));
        Assert.Equal(H("AA4200EC"), ProtoJ.Get(ProtoJ.EqGet));
        Assert.Equal(H("AA660010"), ProtoJ.Get(ProtoJ.Version));
        Assert.Equal(H("AA120101BE"), ProtoJ.SetGame(true));
    }
    [Fact] public void J_Eq()
    {
        var g = new float[10]; g[0] = 3; g[9] = -12;
        var p = ProtoJ.EqPayload(g);
        Assert.Equal(80, p.Length);
        Assert.Equal(H("1400960005000100"), p[..8]);
        Assert.Equal(H("0032000014000100"), p[72..80]);
        Assert.Equal(84, ProtoJ.SetEq(g).Length);
    }
    [Fact] public void J_Touch()
    {
        Assert.Equal(H("01080208030704060502060107A008A0"), ProtoJ.TouchPayload(TouchMap.Defaults()));
        Assert.Equal(H("0208"), ProtoJ.TouchPayload(new TouchMap(new[] { -1, -1, -1, -1, 0 }, new[] { 1, -1, -1, -1, 0 })));
    }
    [Fact] public void J_Decode()
    {
        var ev = ProtoJ.Decode(H("A51401500A" + "A515014B06"));
        Assert.Equal(new Event[] { new Event.Battery(80, null, ReportsRight: false), new Event.Battery(null, 75, ReportsLeft: false) }, ev);
        Assert.All(ProtoJ.SplitFrames(H("A51401500AA515014B06")), f => Assert.True(f.ChecksumOk));
        Assert.Equal(new Event.Firmware("1.2.3", "1.2.4"), ProtoJ.Decode(H("A566050112031204 00"))[0]);
        var t = (Event.Touch)ProtoJ.Decode(H("A53910 01080208030704060502060107A008A0 00"))[0];
        Assert.Equal(TouchMap.Defaults().Left[..4], t.Map.Left[..4]); Assert.Equal(TouchMap.Defaults().Right[..4], t.Map.Right[..4]);
        var eq = (Event.Eq)ProtoJ.Decode(H("A54210 1400960005000100 32006E0006000100 00"))[0];
        Assert.Equal(new[] { 3f, -1f }, eq.Gains);
        Assert.Equal(new Event.Game(true), ProtoJ.Decode(H("A5190101 00"))[0]);
        Assert.Equal(new Event.Ack(0x41, true), ProtoJ.Decode(H("A5410101 00"))[0]);
        Assert.Empty(ProtoJ.Decode(H("A51401")));
    }
    [Fact] public void Ad_Parse()
    {
        var ad = AdParser.Parse(H("020106" + "0BFF112233445566BE57520D" + "0509544F5A4F"))!;
        Assert.Equal("11:22:33:44:55:66", ad.ClassicMac); Assert.Equal("BE0D", ad.Mark);
        Assert.Contains(ad.Mark, AdParser.A1Marks); Assert.Equal(Protocol.J, AdParser.ProtocolHint(ad.Mark));
        Assert.Equal("61", AdParser.Parse(H("0AFF010203040506615752"))!.Mark);
        Assert.Equal("45", AdParser.Parse(H("0CFF01020304050645574F4552"))!.Mark);
        Assert.Null(AdParser.Parse(H("05FF4C000102"))); Assert.Null(AdParser.Parse(H("FFFF")));
        Assert.Equal(Protocol.A, AdParser.ProtocolHint("010E"));
    }
    [Fact] public void Anc_Encode_Decode()
    {
        Assert.Equal(H("10040100 00"), ProtoA.SetAnc(AncMode.Off));
        Assert.Equal(H("10040101 01"), ProtoA.SetAnc(AncMode.Anc));
        Assert.Equal(H("10050101 01"), ProtoA.SetAnc(AncMode.Transparency));
        Assert.Equal(H("00300000"), ProtoA.Get(ProtoA.GetAncMode));
        Assert.Equal(new Event.Anc(2), ProtoA.Decode(H("0030020200 02")));
        Assert.Equal(new Event.Anc(1), ProtoA.Decode(H("20300101 01")));
        Assert.Equal(new Event.Anc(2), ProtoA.Decode(H("20100101 01")));
    }
    [Fact] public void Model_Detect()
    {
        Assert.Equal(DeviceModel.NC9, Models.Detect("TOZO NC9"));
        Assert.Equal(DeviceModel.A1, Models.Detect("TOZO-A1"));
        Assert.Equal("NC9", Models.Detect(null, "B1").Name); Assert.True(Models.Detect(null, "B1").F.Anc);
        Assert.Equal("A1", Models.Detect("", "BE0D").Name);
        Assert.Equal(Protocol.J, Models.Detect("", "BE0D").Hint);
        Assert.True(Models.Detect("Speaker").IsUnknown);
    }

    [Fact] public void Model_Table()
    {
        // name families, longest match first
        Assert.Equal("T6S", Models.Detect("TOZO T6S").Name);
        Assert.Equal("T6", Models.Detect("TOZO-T6").Name);
        Assert.Equal("NC9Pro", Models.Detect("TOZO NC9 Pro").Name);
        Assert.Equal("NC20Pro", Models.Detect("TOZO-NC20Pro").Name);
        Assert.Equal("AeroSound6Pro", Models.Detect("TOZO AeroSound 6 Pro").Name);
        Assert.True(Models.Detect("Random T60 speaker").IsUnknown); // no partial-token match
        Assert.False(Models.Detect("TOZO T6").Tested);
        Assert.True(Models.Detect("TOZO NC9").Tested);
        // mark wins over name and gives the exact variant
        var g = Models.Detect("whatever", "D40D");
        Assert.Equal("NC20", g.Name); Assert.True(g.F.Anc); Assert.Equal(Protocol.A, g.Hint); Assert.False(g.Tested);
        Assert.Equal(Protocol.J, Models.Detect(null, "060E").Hint);
        Assert.False(Models.Detect(null, "060E").F.Anc);          // Protocol J: no ANC implemented
        Assert.True(Models.Detect(null, "0F0E").F.AncLevel);      // ANC_V1 -> custom level
        Assert.False(Models.Detect(null, "0C0E").F.AncLevel);     // ANC_V2 -> no level
        // Airoha (LD_*) models and charging cases are not in the table
        Assert.Null(Models.ByMark("12"));
        Assert.Null(Models.ByMark("1609"));
        // conservative family features: T6 variants disagree on game -> hidden
        Assert.False(Models.Detect("TOZO T6").F.Game);
        Assert.DoesNotContain(Models.SupportedNames, n => n.Length == 0);
        Assert.True(Models.For(DeviceModel.NC9).Anc); Assert.False(Models.For(DeviceModel.A1).Anc);
    }
    [Fact] public void AncLevel()
    {
        Assert.Equal(H("100F01 14 14"), ProtoA.SetAncLevel(10));
        Assert.Equal(H("100F01 02 02"), ProtoA.SetAncLevel(0));
        Assert.Equal(H("100E0101 01"), ProtoA.SetAnc(AncMode.Custom));
        Assert.Equal(new Event.AncLevel(3), ProtoA.Decode(H("000F0106 06")));
        Assert.Equal(new Event.AncLevel(1), ProtoA.Decode(H("000F0100 00")));
        Assert.True(Models.For(DeviceModel.NC9).AncLevel); Assert.False(Models.For(DeviceModel.A1).AncLevel);
    }

    [Fact] public void A_StatusQueriesNeverTouchCase()
    {
        foreach (var (anc, lvl) in new[] { (false, false), (true, false), (true, true) })
        {
            var q = ProtoA.StatusQueries(anc, lvl);
            Assert.All(q, c => Assert.Equal(0x00, c >> 8));
            Assert.DoesNotContain(ProtoA.GetCaseBattery, q);
            Assert.Contains(ProtoA.GetBattery, q);
            Assert.Equal(anc, q.Contains(ProtoA.GetAncMode));
            Assert.Equal(lvl, q.Contains(ProtoA.GetCustomAncLevel));
        }
    }

    [Fact] public void EverySupportedModelHasTenEqBandsThatEncode()
    {
        Assert.NotEmpty(Models.SupportedNames);
        Assert.Equal(10, EqGrid.Freqs.Length);
        foreach (var n in Models.SupportedNames)
        {
            var m = Models.Detect(n);
            Assert.False(m == DeviceModel.Unknown, n);
            var g = Enumerable.Repeat(1.5f, EqGrid.Freqs.Length).ToArray();
            var f = m.Hint == Protocol.J ? ProtoJ.SetEq(g) : ProtoA.SetEqGains(g);
            Assert.True(f.Length > EqGrid.Freqs.Length, n);
        }
    }

    /// <summary>Golden frames produced by the 1.4.4 code (commit 7c9fe04) for the same gains.</summary>
    [Fact] public void EqWireMatches144()
    {
        { var g = new float[] { -9.5f,-9f,-7f,-8.5f,-5.5f,0f,-4.5f,-9f,-5.5f,-12f };
          Assert.Equal(new[] { "100B15A1A6BAABC900D3A6C988050607080A0C0E10121401B4" }, EqWire.Frames(Protocol.A, g).Select(Convert.ToHexString));
          Assert.Equal(new[] { "AA4150140019000500010032001E00060001006400320007000100C800230008000100900141000A000100200378000C00010040064B000E000100800C1E001000010000194100120001000032000014000100EB" }, EqWire.Frames(Protocol.J, g).Select(Convert.ToHexString)); }
        { var g = new float[] { 3f,1.5f,0f,-2f,-0.5f,12f,6f,-12f,2.5f,0f };
          Assert.Equal(new[] { "100B151E0F00ECFB783C881900050607080A0C0E10121401DE" }, EqWire.Frames(Protocol.A, g).Select(Convert.ToHexString));
          Assert.Equal(new[] { "AA4150140096000500010032008700060001006400780007000100C800640008000100900173000A0001002003F0000C0001004006B4000E000100800C0000100001000019910012000100003278001400010015" }, EqWire.Frames(Protocol.J, g).Select(Convert.ToHexString)); }
    }
}
