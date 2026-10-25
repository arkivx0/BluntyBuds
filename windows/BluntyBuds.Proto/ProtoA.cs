namespace BluntyBuds.Proto;

/// <summary>Protocol A (vendor GATT protocol). Frame: cmdHi cmdLo len payload sum(payload)&amp;0xFF. Empty request = cmd 00 00.</summary>
public static class ProtoA
{
    /// <summary>Charging case (cmd_hi 0x01 / 0x21). Never sent: on some earbuds a case request stalls their command
    /// handler, so every later set (noise, EQ, ...) is ignored. Only unsolicited case notifies are decoded.</summary>
    public const int GetCaseBattery = 0x0102, NotifyCaseBattery = 0x210E;
    /// <summary>Gets sent on connect/refresh. Earbud commands only (cmd_hi 0x00), never charging-case ones.</summary>
    public static IReadOnlyList<int> StatusQueries(bool anc, bool ancLevel)
    {
        var l = new List<int> { GetBattery, GetFwVersion, GetGame, GetButtons, GetEq, GetBalance };
        if (anc) l.Add(GetAncMode);
        if (ancLevel) l.Add(GetCustomAncLevel);
        return l;
    }
    public const int GetFwVersion = 0x0001, GetBattery = 0x0002, GetButtons = 0x0003, SetButtons = 0x1003,
        GetGame = 0x0006, SetGame = 0x1006, GetEq = 0x000B, SetEq = 0x100B, ResetEq = 0x100C,
        GetBalance = 0x0026, SetBalance = 0x1026, NotifyBattery = 0x200E, NotifyGame = 0x200F, NotifyEq = 0x2012, NotifyBalance = 0x201D,
        SetAncOnOff = 0x1004, SetHtOnOff = 0x1005, GetAncMode = 0x0030, NotifyAncMode = 0x2030, NotifyHt = 0x2010, NotifyAncOnOff = 0x2011,
        SetCustomAncOnOff = 0x100E, GetCustomAncLevel = 0x000F, SetCustomAncLevel = 0x100F;
    public const int AncLevelMin = 1, AncLevelMax = 10;
    public const string WriteUuidPrefix = "4157b611", NotifyUuidPrefix = "4157b612";

    static int Sum(ReadOnlySpan<byte> p) { int s = 0; foreach (var b in p) s += (sbyte)b; return s & 0xFF; }

    public static byte[] Frame(int cmd, byte[]? payload = null)
    {
        payload ??= Array.Empty<byte>();
        if (cmd is < 0 or > 0xFFFF || payload.Length > 255) throw new ArgumentOutOfRangeException(nameof(cmd));
        var o = new byte[payload.Length + 4];
        o[0] = (byte)(cmd >> 8); o[1] = (byte)cmd; o[2] = (byte)payload.Length;
        payload.CopyTo(o, 3);
        o[^1] = (byte)Sum(payload);
        return o;
    }

    public static (int Cmd, byte[] Payload)? ParseFrame(byte[] raw)
    {
        if (raw.Length < 4) return null;
        int len = raw[2];
        if (len + 4 != raw.Length) return null;
        var p = raw[3..(3 + len)];
        if (Sum(p) != raw[^1]) return null;
        return ((raw[0] << 8) | raw[1], p);
    }

    public static byte[] Get(int cmd) => Frame(cmd);
    /// <summary>Noise mode: off = 1004{0}, ANC = 1004{1}, transparency = 1005{1}.</summary>
    public static byte[] SetAnc(AncMode m) => m switch
    {
        AncMode.Off => Frame(SetAncOnOff, new byte[] { 0 }),
        AncMode.Anc => Frame(SetAncOnOff, new byte[] { 1 }),
        AncMode.Transparency => Frame(SetHtOnOff, new byte[] { 1 }),
        AncMode.Custom => Frame(SetCustomAncOnOff, new byte[] { 1 }),
        _ => throw new ArgumentOutOfRangeException(nameof(m))
    };
    /// <summary>Custom ANC depth, UI level 1-10; the device byte is level*2.</summary>
    public static byte[] SetAncLevel(int level) => Frame(SetCustomAncLevel, new[] { (byte)(Math.Clamp(level, AncLevelMin, AncLevelMax) * 2) });
    public static int AncLevelFromRaw(int raw) => Math.Clamp(raw / 2, AncLevelMin, AncLevelMax);
    public static byte[] SetGameMode(bool on) => Frame(SetGame, new[] { (byte)(on ? 1 : 0) });
    public static byte[] SetBalanceValue(int v) => Frame(SetBalance, new[] { (byte)(sbyte)Math.Clamp(v, -11, 11) });

    public static byte[] EqPayload(float[] gains, float[]? qs = null, bool save = true)
    {
        qs ??= EqGrid.Qs;
        if (gains.Length != 10 || qs.Length != 10) throw new ArgumentException("10 bands");
        var b = new byte[21];
        for (int i = 0; i < 10; i++) b[i] = (byte)(sbyte)MathF.Round(Math.Clamp(gains[i], EqGrid.MinDb, EqGrid.MaxDb) * 10f, MidpointRounding.AwayFromZero);
        for (int i = 0; i < 10; i++) b[10 + i] = (byte)(sbyte)MathF.Round(qs[i] * 10f, MidpointRounding.AwayFromZero);
        b[20] = (byte)(save ? 1 : 0);
        return b;
    }
    public static byte[] SetEqGains(float[] gains) => Frame(SetEq, EqPayload(gains));

    public static byte[] TouchPayload(TouchMap m)
    {
        var d = TouchMap.Defaults();
        var o = new byte[10];
        for (int i = 0; i < 5; i++) { o[i] = (byte)(m.Left[i] < 0 ? d.Left[i] : m.Left[i]); o[5 + i] = (byte)(m.Right[i] < 0 ? d.Right[i] : m.Right[i]); }
        return o;
    }
    public static byte[] SetTouch(TouchMap m) => Frame(SetButtons, TouchPayload(m));

    public static Event? Decode(byte[] raw) => ParseFrame(raw) is { } f ? DecodePayload(f.Cmd, f.Payload) : null;

    static string Ver(byte[] p, int o) => $"{p[o]}.{p[o + 1]}.{p[o + 2]}";

    /// <summary>Protocol A sends both buds in one packet. One byte is the left bud only; two or more use the first two. Unlisted sides are not in use.</summary>
    static Event? DecodeBattery(byte[] p)
    {
        if (p.Length == 0) return null;
        var (l, cl) = BatteryText.Parse(p[0]);
        if (p.Length == 1) return new Event.Battery(l, null, cl, false, Snapshot: true);
        var (r, cr) = BatteryText.Parse(p[1]);
        return new Event.Battery(l, r, cl, cr, Snapshot: true);
    }

    static Event CaseOf(byte b) { var (l, c) = BatteryText.Parse(b); return new Event.CaseBattery(l, c); }
    public static Event DecodePayload(int cmd, byte[] p) => cmd switch
    {
        GetBattery or NotifyBattery => DecodeBattery(p) is { } b ? b : new Event.Raw(cmd, p),
        GetCaseBattery or NotifyCaseBattery => p.Length > 0 ? CaseOf(p[0]) : new Event.Raw(cmd, p),
        GetFwVersion => p.Length switch
        {
            3 => new Event.Firmware(Ver(p, 0), null),
            6 => new Event.Firmware(Ver(p, 0), Ver(p, 3)),
            _ => new Event.Raw(cmd, p)
        },
        GetGame or NotifyGame => p.Length > 0 ? new Event.Game(p[0] != 0) : new Event.Raw(cmd, p),
        GetButtons => DecodeTouch(p) is { } m ? new Event.Touch(m) : new Event.Raw(cmd, p),
        GetEq or NotifyEq => p.Length is >= 1 and <= 20 ? new Event.Eq(p.Take(Math.Min(10, p.Length)).Select(b => (sbyte)b / 10f).ToArray()) : new Event.Raw(cmd, p),
        GetCustomAncLevel => p.Length > 0 ? new Event.AncLevel(AncLevelFromRaw(p[0])) : new Event.Raw(cmd, p),
        GetAncMode or NotifyAncMode => p.Length > 0 ? new Event.Anc(p[0]) : new Event.Raw(cmd, p),
        NotifyAncOnOff => p.Length > 0 ? new Event.Anc(p[0] != 0 ? 1 : 0) : new Event.Raw(cmd, p),
        NotifyHt => p.Length > 0 ? new Event.Anc(p[0] != 0 ? 2 : 0) : new Event.Raw(cmd, p),
        GetBalance or NotifyBalance => p.Length > 0 ? new Event.Balance((sbyte)p[0]) : new Event.Raw(cmd, p),
        _ => (cmd >> 8) == 0x10 ? new Event.Ack(cmd, !(p.Length > 0 && p[0] == 2)) : new Event.Raw(cmd, p)
    };

    static TouchMap? DecodeTouch(byte[] p)
    {
        if (p.Length == 0 || p.Length % 2 == 1) return null;
        int half = p.Length / 2;
        int[] Side(int off) => Enumerable.Range(0, 5).Select(i => i < half ? (int)(sbyte)p[off + i] : -1).ToArray();
        return new TouchMap(Side(0), Side(half));
    }
}
