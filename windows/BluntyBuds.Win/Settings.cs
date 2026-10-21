using System.Text.Json;
namespace BluntyBuds.Win;

/// <summary>Local settings in %APPDATA%\BluntyBuds\settings.json. Nothing leaves the PC.</summary>
public sealed class Settings
{
    public ulong? LastAddress { get; set; }
    public string? LastName { get; set; }
    public bool AutoReconnect { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool CloseHintShown { get; set; }
    public string? LastPreset { get; set; }
    public int LowBatteryPercent { get; set; } = 20;
    public Dictionary<string, float[]> EqByDevice { get; set; } = new();

    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BluntyBuds", "settings.json");
    public static Settings Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception ex) { Program.Log(ex, "settings load"); }
        return new();
    }
    public void Save()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { Program.Log(ex, "settings save"); }
    }

    // ----- Start with Windows (HKCU Run, off unless the user turns it on) -----
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    /// <summary>If autostart is on, rewrite the Run entry with the current exe path (handles moved/renamed folders).</summary>
    public static void RefreshStartupPath()
    {
        try { if (StartWithWindows) StartWithWindows = true; } catch (Exception ex) { Program.Log(ex, "startup refresh"); }
    }
    public static bool StartWithWindows
    {
        get { try { using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue("BluntyBuds") != null; } catch { return false; } }
        set
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) k.SetValue("BluntyBuds", $"\"{Environment.ProcessPath}\" --tray"); else k.DeleteValue("BluntyBuds", false);
        }
    }
}
