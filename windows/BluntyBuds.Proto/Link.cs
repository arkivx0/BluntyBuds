namespace BluntyBuds.Proto;

/// <summary>ATT / GATT error code names (Bluetooth Core spec Vol 3 Part F 3.4.1.1, plus common Android stack codes).</summary>
public static class GattErrors
{
    public static string Name(int code) => code switch
    {
        0x00 => "success",
        0x01 => "invalid handle",
        0x02 => "read not permitted",
        0x03 => "write not permitted",
        0x05 => "insufficient authentication",
        0x06 => "request not supported",
        0x07 => "invalid offset",
        0x08 => "insufficient authorization",
        0x0D => "invalid attribute value length",
        0x0E => "unlikely error",
        0x0F => "insufficient encryption",
        0x13 => "remote user terminated connection",
        0x16 => "connection terminated by local host",
        0x22 => "LMP response timeout",
        0x3E => "connection failed to be established",
        0x93 => "connection timeout",
        0x80 => "no resources",
        0x81 => "internal error",
        0x85 => "generic GATT error (often: out of range or link dropped)",
        _ => $"code {code}"
    };
}

/// <summary>Reconnect backoff: 15 s, 30 s, 60 s, 60 s ... Reset after a successful connect.</summary>
public sealed class Backoff
{
    readonly int[] _steps;
    int _i;
    public Backoff(params int[] stepsSeconds) => _steps = stepsSeconds.Length > 0 ? stepsSeconds : new[] { 15, 30, 60 };
    public TimeSpan Next() => TimeSpan.FromSeconds(_steps[Math.Min(_i++, _steps.Length - 1)]);
    public TimeSpan Max => TimeSpan.FromSeconds(_steps.Max());
    public void Reset() => _i = 0;
    /// <summary>Connect attempts must outlive the longest retry gap so a slow connect isn't abandoned and re-started.</summary>
    public TimeSpan ConnectTimeout => Max + TimeSpan.FromSeconds(15);
}

/// <summary>Coalesces bursts (e.g. advertisement floods) into at most one flush per interval.</summary>
public sealed class Coalescer
{
    readonly TimeSpan _min; DateTime _last = DateTime.MinValue; bool _dirty;
    public Coalescer(TimeSpan minInterval) => _min = minInterval;
    public void Mark() => _dirty = true;
    /// <summary>True if a flush should happen now (dirty and the interval has passed).</summary>
    public bool ShouldFlush(DateTime now)
    {
        if (!_dirty || now - _last < _min) return false;
        _dirty = false; _last = now; return true;
    }
}

/// <summary>Generation token: a delayed action only runs if no newer generation started meanwhile.</summary>
public sealed class Generation
{
    int _g;
    public int Begin() => Interlocked.Increment(ref _g);
    public bool IsCurrent(int g) => Volatile.Read(ref _g) == g;
}
