namespace BluntyBuds.Proto;

/// <summary>Vendor advertisement: manufacturer AD (0xFF) = classicMac[6] id[1] ("WR" batch[1] | "WOER").</summary>
public static class AdParser
{
    public sealed record VendorAd(string ClassicMac, string Mark);

    public static VendorAd? Parse(byte[]? rec)
    {
        if (rec is null) return null;
        int i = 0;
        while (i < rec.Length)
        {
            int len = rec[i];
            if (len == 0 || i + len >= rec.Length) break;
            if (rec[i + 1] == 0xFF && ParseManufacturer(rec[(i + 2)..(i + 1 + len)]) is { } ad) return ad;
            i += len + 1;
        }
        return null;
    }

    /// <summary>data = bytes after the 0xFF AD type (company-id bytes included).</summary>
    public static VendorAd? ParseManufacturer(byte[] d)
    {
        if (d.Length < 9) return null;
        var mac = string.Join(":", d.Take(6).Select(b => b.ToString("X2")));
        var id = d[6].ToString("X2");
        if (d[7] == 'W' && d[8] == 'R') return new VendorAd(mac, id + (d.Length >= 10 ? d[9].ToString("X2") : ""));
        if (d.Length >= 11 && d[7] == 'W' && d[8] == 'O' && d[9] == 'E' && d[10] == 'R') return new VendorAd(mac, id);
        return null;
    }

    public static readonly HashSet<string> A1Marks = new() { "61", "BE0D", "050E", "0B0E", "010E", "020E", "040E", "190E", "1B0E", "D90D" };
    static readonly HashSet<string> JHint = new() { "61", "BE0D", "050E", "0B0E", "72", "52", "55", "62", "66", "81", "88", "92" };
    public static Protocol ProtocolHint(string mark) => JHint.Contains(mark.ToUpperInvariant()) ? Protocol.J : Protocol.A;
}
