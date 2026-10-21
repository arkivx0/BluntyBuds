using System.Text.Json;
namespace BluntyBuds.Win;

/// <summary>EQ presets stored in %APPDATA%\BluntyBuds\presets.json only.</summary>
public sealed class Presets
{
    static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BluntyBuds");
    static readonly string FilePath = Path.Combine(Dir, "presets.json");
    public static readonly Dictionary<string, float[]> BuiltIn = new()
    {
        ["Flat"] = new float[10],
        ["Bass boost"] = new float[] { 6, 5, 4, 2, 0, 0, 0, 0, 0, 0 },
        ["Treble boost"] = new float[] { 0, 0, 0, 0, 0, 1, 2, 4, 5, 6 },
        ["Vocal"] = new float[] { -2, -1, 0, 1, 3, 3, 2, 1, 0, -1 },
        ["Rock"] = new float[] { 4, 3, 1, 0, -1, 0, 1, 3, 4, 4 },
        ["Classical"] = new float[] { 3, 2, 1, 0, 0, 0, 1, 2, 3, 3 },
        ["Podcast"] = new float[] { -4, -3, -1, 1, 3, 4, 3, 1, -1, -3 },
    };
    Dictionary<string, float[]> _user = new();
    public Presets() { try { if (File.Exists(FilePath)) _user = JsonSerializer.Deserialize<Dictionary<string, float[]>>(File.ReadAllText(FilePath)) ?? new(); } catch { } }
    public Dictionary<string, float[]> All() { var d = new Dictionary<string, float[]>(BuiltIn); foreach (var kv in _user) if (kv.Value.Length == 10) d[kv.Key] = kv.Value; return d; }
    public bool IsBuiltIn(string n) => BuiltIn.ContainsKey(n);
    public void Save(string n, float[] g) { _user[n] = g; Persist(); }
    public void Delete(string n) { _user.Remove(n); Persist(); }
    public Dictionary<string, float[]> UserPresets() => new(_user);
    /// <summary>Import presets from a JSON object {"name": [10 gains]}. Returns the number imported.</summary>
    public int Import(string json)
    {
        var d = JsonSerializer.Deserialize<Dictionary<string, float[]>>(json) ?? throw new InvalidDataException("Not a preset file");
        int n = 0;
        foreach (var (k, v) in d)
        {
            if (string.IsNullOrWhiteSpace(k) || v is null || v.Length != 10 || IsBuiltIn(k)) continue;
            _user[k.Trim()] = v.Select(x => Math.Clamp(x, -12f, 12f)).ToArray(); n++;
        }
        Persist(); return n;
    }
    public string Export() => JsonSerializer.Serialize(_user, new JsonSerializerOptions { WriteIndented = true });
    void Persist() { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(_user)); }
}
