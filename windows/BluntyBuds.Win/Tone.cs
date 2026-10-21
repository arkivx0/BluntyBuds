using System.Media;
namespace BluntyBuds.Win;

/// <summary>"Find earbuds": loud chirp to the default audio output (the earbuds, if they're the active device).</summary>
public sealed class Tone
{
    SoundPlayer? _p;
    MemoryStream? _ms;
    public void Play(bool left, bool right)
    {
        Stop();
        const int rate = 44100, frames = rate;
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        int data = frames * 4;
        bw.Write("RIFF"u8.ToArray()); bw.Write(36 + data); bw.Write("WAVEfmt "u8.ToArray()); bw.Write(16); bw.Write((short)1); bw.Write((short)2);
        bw.Write(rate); bw.Write(rate * 4); bw.Write((short)4); bw.Write((short)16); bw.Write("data"u8.ToArray()); bw.Write(data);
        for (int i = 0; i < frames; i++)
        {
            double t = (double)i / rate, ph = t % 0.25;
            bool beep = ph < 0.12 && t < 0.75;
            double f = 2600 + 1200 * (ph / 0.12);
            short s = beep ? (short)(Math.Sin(2 * Math.PI * f * t) * 30000) : (short)0;
            bw.Write(left ? s : (short)0); bw.Write(right ? s : (short)0);
        }
        bw.Flush(); ms.Position = 0;
        _ms = ms; _p = new SoundPlayer(ms); _p.PlayLooping();
    }
    public void Stop() { _p?.Stop(); _p?.Dispose(); _p = null; _ms?.Dispose(); _ms = null; }
}
