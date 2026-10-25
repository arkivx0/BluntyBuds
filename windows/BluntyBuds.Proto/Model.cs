namespace BluntyBuds.Proto;

public enum Protocol { A, J }

/// <summary>Touch actions: A = protocol-A byte, J = protocol-J value .</summary>
public sealed record TouchAction(string Name, int A, int J, string Label)
{
    public static readonly TouchAction None = new("NONE", 0, 0, "None");
    public static readonly TouchAction PlayPause = new("PLAY_PAUSE", 1, 8, "Play / Pause");
    public static readonly TouchAction Next = new("NEXT", 2, 6, "Next track");
    public static readonly TouchAction Previous = new("PREVIOUS", 3, 7, "Previous track");
    public static readonly TouchAction Assistant = new("ASSISTANT", 4, 160, "Voice assistant");
    public static readonly TouchAction VolumeUp = new("VOLUME_UP", 5, 1, "Volume +");
    public static readonly TouchAction VolumeDown = new("VOLUME_DOWN", 6, 2, "Volume -");
    public static readonly TouchAction GameMode = new("GAME_MODE", 7, 100, "Toggle Low-Latency");
    public static readonly TouchAction AncSwitch = new("ANC_SWITCH", 8, 3, "Switch noise mode");
    public static readonly TouchAction AncOff = new("ANC_OFF", 10, 10, "Noise off");
    public static readonly IReadOnlyList<TouchAction> All = new[] { None, PlayPause, Next, Previous, Assistant, VolumeUp, VolumeDown, GameMode, AncSwitch, AncOff };
    public static TouchAction? FromA(int b) => All.FirstOrDefault(a => a.A == b);
    public static TouchAction? FromJ(int v) => All.FirstOrDefault(a => a.J == v);
    public override string ToString() => Label;
}

public enum Gesture { Single = 0, Double = 1, Triple = 2, Long = 3 }
public static class GestureText
{
    public static string Label(this Gesture g) => g switch { Gesture.Single => "Single tap", Gesture.Double => "Double tap", Gesture.Triple => "Triple tap", _ => "Long press" };
}

/// <summary>Per-side map of protocol-A action bytes (5 slots; slot 4 unknown, preserved). -1 = not reported.</summary>
public sealed class TouchMap
{
    public int[] Left { get; }
    public int[] Right { get; }
    public TouchMap(int[] left, int[] right)
    {
        if (left.Length != 5 || right.Length != 5) throw new ArgumentException("5 slots per side");
        Left = left; Right = right;
    }
    public static TouchMap Defaults() => new(new[] { 1, 3, 4, 6, 0 }, new[] { 1, 2, 4, 5, 0 });
    /// <summary>Gestures to show: those the earbuds reported (slot value >= 0). Before any report, all four.</summary>
    public IReadOnlyList<Gesture> Supported()
    {
        var all = Enum.GetValues<Gesture>();
        var got = all.Where(g => Left[(int)g] >= 0 || Right[(int)g] >= 0).ToArray();
        return got.Length > 0 ? got : all;
    }
    public TouchMap Copy() => new((int[])Left.Clone(), (int[])Right.Clone());
    public override bool Equals(object? o) => o is TouchMap m && Left.SequenceEqual(m.Left) && Right.SequenceEqual(m.Right);
    public override int GetHashCode() => HashCode.Combine(Left.Sum(), Right.Sum());
}

public static class EqGrid
{
    public static readonly int[] Freqs = { 20, 50, 100, 200, 400, 800, 1600, 3200, 6400, 12800 };
    public static readonly float[] Qs = { 0.5f, 0.6f, 0.7f, 0.8f, 1.0f, 1.2f, 1.4f, 1.6f, 1.8f, 2.0f };
    public const float MinDb = -12f, MaxDb = 12f;
}

public abstract record Event
{
    /// <summary>
    /// One battery report. Snapshot replaces both sides (a missing side is not in use).
    /// A partial report only changes the sides it names, so a left-only protocol-J packet does not wipe the right.
    /// </summary>
    public sealed record Battery(int? Left, int? Right, bool ChargingLeft = false, bool ChargingRight = false, bool Snapshot = false, bool ReportsLeft = true, bool ReportsRight = true) : Event;
    /// <summary>Charging-case battery. Level null = not reported.</summary>
    public sealed record CaseBattery(int? Level, bool Charging = false) : Event;
    public sealed record Firmware(string Left, string? Right) : Event;
    public sealed record Game(bool On) : Event;
    public sealed record Touch(TouchMap Map) : Event;
    public sealed record Eq(float[] Gains) : Event;
    public sealed record Balance(int Value) : Event;
    public sealed record Ack(int Cmd, bool Ok) : Event;
    public sealed record Anc(int Mode) : Event;
    public sealed record AncLevel(int Level) : Event;
    public sealed record Raw(int Cmd, byte[] Payload) : Event;
}

/// <summary>Battery percent text for the window, the tray and alerts. A side that was not reported is omitted.</summary>
public static class BatteryText
{
    /// <summary>0–100 is a percent. The high bit plus a percent of 0–100 means charging. 0xFF and other out-of-range values mean that earbud is absent.</summary>
    public static (int? Percent, bool Charging) Parse(int raw)
    {
        int b = raw & 0xFF;
        if (b == 0xFF) return (null, false);
        if (b > 100 && (b & 0x80) != 0)
        {
            int p = b & 0x7F;
            if (p <= 100) return (p, true);
        }
        if (b > 100) return (null, false);
        return (b, false);
    }

    public static string Format(int? left, int? right, bool chargingLeft = false, bool chargingRight = false)
    {
        string? One(string name, int? p, bool c) => p is int v ? $"{name} {v}%" + (c ? " charging" : "") : null;
        var parts = new[] { One("L", left, chargingLeft), One("R", right, chargingRight) }.Where(s => s is not null).ToArray();
        return parts.Length == 0 ? "—" : string.Join("  ", parts);
    }

    /// <summary>Lowest present earbud. Null when neither side is in use, so a missing bud cannot trip a low-battery alert.</summary>
    public static int? Lowest(int? left, int? right)
    {
        int? m = left;
        if (right is int r) m = m is int x ? Math.Min(x, r) : r;
        return m;
    }

    /// <summary>Solo Mode: "L" or "R" when exactly one earbud is in use, null when both or neither are.</summary>
    public static string? Solo(int? left, int? right) => (left, right) switch
    {
        (not null, null) => "L",
        (null, not null) => "R",
        _ => null
    };

    /// <summary>Small case figure, or null when the case level is unknown.</summary>
    public static string? Case(int? level, bool charging = false) => level is int v ? $"Case {v}%" + (charging ? " charging" : "") : null;
}

/// <summary>ANC modes as used by protocol A (GET 0x0030 payload[0]).</summary>
public enum AncMode { Off = 0, Anc = 1, Transparency = 2, Wind = 3, Leisure = 4, Custom = 5, Adaptive = 6 }

/// <summary>A detected model: display name, optional ad mark, protocol hint and the feature set to show.</summary>
public sealed record DeviceModel(string Name, string? Mark, string Chip, Protocol? Hint, Models.Features F, bool Tested)
{
    public static readonly DeviceModel Unknown = new("Earbuds", null, "?", null, Models.Generic, false);
    /// <summary>Hand-verified on real hardware (name-only detection).</summary>
    public static readonly DeviceModel A1 = new("A1", null, "?", null, new(false, true, true, true, true), true);
    public static readonly DeviceModel NC9 = new("NC9", null, "?", null, new(true, true, true, true, true, AncLevel: true), true);
    public bool IsUnknown => ReferenceEquals(this, Unknown) || Name == Unknown.Name && Mark is null;
    public override string ToString() => Name;
}

/// <summary>Model detection + per-model feature set (see ModelTable.g.cs).</summary>
public static partial class Models
{
    public sealed record Features(bool Anc, bool Game, bool Eq, bool Touch, bool Balance, bool AncLevel = false);
    public static readonly Features Generic = new(false, true, true, true, true);
    public static readonly HashSet<string> TestedNames = new(StringComparer.OrdinalIgnoreCase) { "A1", "NC9" };

    static DeviceModel FromRow((string Mark, string Name, string Chip, Protocol Proto, bool Anc, bool AncLevel, bool Game, bool Eq, bool Touch, bool Balance) r)
        => new(r.Name, r.Mark, r.Chip, r.Proto, new(r.Anc, r.Game, r.Eq, r.Touch, r.Balance, r.AncLevel), TestedNames.Contains(r.Name));

    /// <summary>Exact model by advertisement mark (most precise).</summary>
    public static DeviceModel? ByMark(string? mark)
    {
        if (string.IsNullOrEmpty(mark)) return null;
        foreach (var r in Table) if (string.Equals(r.Mark, mark, StringComparison.OrdinalIgnoreCase)) return FromRow(r);
        return null;
    }

    static string[]? _names;
    static string[] NamesLongestFirst => _names ??= Table.Select(r => r.Name).Distinct().OrderByDescending(n => n.Length).ToArray();

    /// <summary>
    /// Model family from the Bluetooth name (e.g. "TOZO NC9", "TOZO-T6S"). Features are the intersection of every
    /// supported variant with that name (conservative); A1 and NC9 use their hardware-verified sets.
    /// </summary>
    public static DeviceModel? ByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = name.Replace(" ", "").Replace("-", "").Replace("_", "");
        foreach (var fam in NamesLongestFirst)
        {
            var f = fam.Replace(" ", "");
            int i = n.IndexOf(f, StringComparison.OrdinalIgnoreCase);
            while (i >= 0)
            {
                bool leftOk = i == 0 || !char.IsLetterOrDigit(n[i - 1]) || IsBrandPrefix(n[..i]);
                bool rightOk = i + f.Length == n.Length || !char.IsLetterOrDigit(n[i + f.Length]);
                if (leftOk && rightOk) return Family(fam);
                i = n.IndexOf(f, i + 1, StringComparison.OrdinalIgnoreCase);
            }
        }
        return null;
    }
    static bool IsBrandPrefix(string s) => s.EndsWith("TOZO", StringComparison.OrdinalIgnoreCase);

    public static DeviceModel Family(string fam)
    {
        if (fam.Equals("A1", StringComparison.OrdinalIgnoreCase)) return DeviceModel.A1;
        if (fam.Equals("NC9", StringComparison.OrdinalIgnoreCase)) return DeviceModel.NC9;
        var v = Table.Where(r => r.Name.Equals(fam, StringComparison.OrdinalIgnoreCase)).ToArray();
        var protos = v.Select(r => r.Proto).Distinct().ToArray();
        var f = new Features(v.All(r => r.Anc), v.All(r => r.Game), v.All(r => r.Eq), v.All(r => r.Touch), v.All(r => r.Balance), v.All(r => r.AncLevel));
        return new DeviceModel(v[0].Name, null, string.Join("/", v.Select(r => r.Chip).Distinct()), protos.Length == 1 ? protos[0] : null, f, TestedNames.Contains(fam));
    }

    public static DeviceModel Detect(string? name, string? mark = null) => ByMark(mark) ?? ByName(name) ?? DeviceModel.Unknown;
    public static Features For(DeviceModel m) => m.F;
    public static IEnumerable<string> SupportedNames => NamesLongestFirst.OrderBy(n => n);
}
