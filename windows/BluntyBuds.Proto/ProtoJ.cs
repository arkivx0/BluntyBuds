namespace BluntyBuds.Proto;

/// <summary>Protocol J. Request AA cmd len payload sum(all)%256; reply A5 cmd len payload chk.</summary>
public static class ProtoJ
{
    public const int BatteryLeft = 0x14, BatteryRight = 0x15, DeviceInfo = 0x17, ConnStatus = 0x18, GameSet = 0x12, GameGet = 0x19,
        ButtonSet = 0x38, ButtonGet = 0x39, EqSet = 0x41, EqGet = 0x42, Version = 0x66;
    public static readonly Guid Service = Guid.Parse("0000faa0-0000-1000-8000-00805f9b34fb");
    public static readonly Guid Write = Guid.Parse("0000faa1-0000-1000-8000-00805f9b34fb");
    public static readonly Guid Notify = Guid.Parse("0000faa2-0000-1000-8000-00805f9b34fb");

    // key -> (isLeft, slot)
    static readonly Dictionary<int, (bool Left, int Slot)> KeyToSlot = new()
    {
        [1] = (true, 0), [2] = (false, 0), [3] = (true, 1), [4] = (false, 1),
        [5] = (true, 3), [6] = (false, 3), [7] = (true, 2), [8] = (false, 2)
    };

    public static byte[] Frame(int cmd, byte[]? payload = null)
    {
        payload ??= Array.Empty<byte>();
        if (cmd is < 0 or > 0xFF || payload.Length > 255) throw new ArgumentOutOfRangeException(nameof(cmd));
        var o = new byte[payload.Length + 4];
        o[0] = 0xAA; o[1] = (byte)cmd; o[2] = (byte)payload.Length;
        payload.CopyTo(o, 3);
        int s = 0; for (int i = 0; i < o.Length - 1; i++) s += o[i];
        o[^1] = (byte)(s % 256);
        return o;
    }
    public static byte[] Get(int cmd) => Frame(cmd);
    public static byte[] SetGame(bool on) => Frame(GameSet, new[] { (byte)(on ? 1 : 0) });

    static void Le16(List<byte> o, int v) { o.Add((byte)v); o.Add((byte)(v >> 8)); }

    public static byte[] EqPayload(float[] gains)
    {
        if (gains.Length != 10) throw new ArgumentException("10 bands");
        var o = new List<byte>(80);
        for (int i = 0; i < 10; i++)
        {
            Le16(o, EqGrid.Freqs[i]);
            Le16(o, (int)MathF.Round(Math.Clamp(gains[i], EqGrid.MinDb, EqGrid.MaxDb) * 10f, MidpointRounding.AwayFromZero) + 120);
            Le16(o, (int)MathF.Round(EqGrid.Qs[i] * 10f, MidpointRounding.AwayFromZero));
            Le16(o, 1);
        }
        return o.ToArray();
    }
    public static byte[] SetEq(float[] gains) => Frame(EqSet, EqPayload(gains));

    public static byte[] TouchPayload(TouchMap m)
    {
        var o = new List<byte>();
        for (int key = 1; key <= 8; key++)
        {
            var (left, slot) = KeyToSlot[key];
            var a = TouchAction.FromA(left ? m.Left[slot] : m.Right[slot]);
            if (a is null) continue;
            o.Add((byte)key); o.Add((byte)a.J);
        }
        return o.ToArray();
    }
    public static byte[] SetTouch(TouchMap m) => Frame(ButtonSet, TouchPayload(m));

    public static List<(int Cmd, byte[] Payload, bool ChecksumOk)> SplitFrames(byte[] raw)
    {
        var res = new List<(int, byte[], bool)>();
        int i = 0;
        while (i + 3 < raw.Length)
        {
            if (raw[i] != 0xA5) { i++; continue; }
            int cmd = raw[i + 1], len = raw[i + 2], end = i + 3 + len;
            if (end >= raw.Length) break;
            int s = 0; for (int k = i; k < end; k++) s += raw[k];
            res.Add((cmd, raw[(i + 3)..end], s % 256 == raw[end]));
            i = end + 1;
        }
        return res;
    }

    public static List<Event> Decode(byte[] raw) => SplitFrames(raw).Select(f => DecodePayload(f.Cmd, f.Payload)).ToList();

    public static Event DecodePayload(int cmd, byte[] p)
    {
        switch (cmd)
        {
            case BatteryLeft when p.Length == 1:
                var (jl, jcl) = BatteryText.Parse(p[0]);
                return new Event.Battery(jl, null, ChargingLeft: jcl, ReportsRight: false);
            case BatteryRight when p.Length == 1:
                var (jr, jcr) = BatteryText.Parse(p[0]);
                return new Event.Battery(null, jr, ChargingRight: jcr, ReportsLeft: false);
            case BatteryLeft or BatteryRight: return new Event.Raw(cmd, p);
            case GameGet: return p.Length == 1 ? new Event.Game(p[0] == 1) : new Event.Raw(cmd, p);
            case GameSet: case ButtonSet: case EqSet: return new Event.Ack(cmd, p.Length == 1 && p[0] == 1);
            case ButtonGet:
            {
                var l = Enumerable.Repeat(-1, 5).ToArray(); var r = Enumerable.Repeat(-1, 5).ToArray();
                for (int k = 0; k + 1 < p.Length; k += 2)
                    if (KeyToSlot.TryGetValue(p[k], out var s) && TouchAction.FromJ(p[k + 1]) is { } a)
                        (s.Left ? l : r)[s.Slot] = a.A;
                return new Event.Touch(new TouchMap(l, r));
            }
            case EqGet:
            {
                int n = p.Length / 8;
                if (n == 0) return new Event.Raw(cmd, p);
                return new Event.Eq(Enumerable.Range(0, n).Select(i => ((p[i * 8 + 2] | (p[i * 8 + 3] << 8)) - 120) / 10f).ToArray());
            }
            case Version:
                return p.Length >= 5 ? new Event.Firmware(Ver(p[1], p[2]), Ver(p[3], p[4])) : new Event.Raw(cmd, p);
            default: return new Event.Raw(cmd, p);
        }
    }
    static string Ver(byte b1, byte b2) => $"{b1 >> 4}.{b1 & 0xF}.{b2}";
}
