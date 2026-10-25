namespace BluntyBuds.Proto;

/// <summary>What goes on the wire for one EQ change. Same as 1.4.4: a single set-EQ frame, no mode command before it.</summary>
public static class EqWire
{
    public static IReadOnlyList<byte[]> Frames(Protocol p, float[] gains) => p switch
    {
        Protocol.A => new[] { ProtoA.SetEqGains(gains) },
        Protocol.J => new[] { ProtoJ.SetEq(gains) },
        _ => Array.Empty<byte[]>()
    };
}
