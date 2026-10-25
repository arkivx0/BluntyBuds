using System.Runtime.InteropServices;
using BluntyBuds.Proto;
using D = System.Drawing;
using F = System.Windows.Forms;

namespace BluntyBuds.Win;

/// <summary>System-tray icon: battery tooltip + mini battery glyph, menu for noise/game/open/quit, balloon toasts.</summary>
public sealed class Tray : IDisposable
{
    readonly F.NotifyIcon _icon = new() { Text = "BluntyBuds" };
    bool _disposed;
    readonly F.ContextMenuStrip _menu = new();
    readonly Dictionary<AncMode, F.ToolStripMenuItem> _anc = new();
    readonly F.ToolStripMenuItem _game = new("Game mode") { CheckOnClick = false };
    readonly F.ToolStripSeparator _sep1 = new();

    public event Action? OpenRequested, QuitRequested, GameToggleRequested;
    public event Action<AncMode>? AncRequested;


    public Tray()
    {
        _menu.Items.Add("Open BluntyBuds", null, (_, _) => Safe(() => OpenRequested?.Invoke()));
        _menu.Items.Add(_sep1);
        foreach (var (m, t) in new[] { (AncMode.Anc, "Quiet"), (AncMode.Transparency, "Aware"), (AncMode.Off, "Off"), (AncMode.Custom, "Custom depth") })
        {
            var mm = m; var it = new F.ToolStripMenuItem(t); it.Click += (_, _) => Safe(() => AncRequested?.Invoke(mm));
            _anc[m] = it; _menu.Items.Add(it);
        }
        _game.Click += (_, _) => Safe(() => GameToggleRequested?.Invoke());
        _menu.Items.Add(_game);
        _menu.Items.Add(new F.ToolStripSeparator());
        _menu.Items.Add("Quit", null, (_, _) => Safe(() => QuitRequested?.Invoke()));
        _icon.ContextMenuStrip = _menu;
        try
        {
            var res = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/bluntybuds.ico"));
            _icon.Icon = res != null ? new D.Icon(res.Stream, F.SystemInformation.SmallIconSize) : D.SystemIcons.Application;
        }
        catch (Exception ex) { Program.Log(ex, "tray icon"); _icon.Icon = D.SystemIcons.Application; }
        _icon.Visible = true;
        _icon.MouseClick += (_, e) => Safe(() => { if (e.Button == F.MouseButtons.Left) OpenRequested?.Invoke(); });
        Update(null, null, null, false, null, Models.For(DeviceModel.Unknown), false);
    }

    /// <summary>Safe to call from any thread; marshalled to the UI thread. Never throws.</summary>
    public void Update(string? model, int? l, int? r, bool connected, int? ancMode, Models.Features f, bool game, bool chargingLeft = false, bool chargingRight = false, int? caseLevel = null, bool caseCharging = false)
    {
        var d = System.Windows.Application.Current?.Dispatcher;
        if (d != null && !d.CheckAccess()) { d.BeginInvoke(() => Update(model, l, r, connected, ancMode, f, game, chargingLeft, chargingRight, caseLevel, caseCharging)); return; }
        if (_disposed) return;
        try
        {
            // Only the earbud(s) in use, e.g. "L 82%": a missing bud is left out, never shown as 0% or disconnected.
            string bat = BatteryText.Format(l, r, chargingLeft, chargingRight);
            string? cs = BatteryText.Case(caseLevel, caseCharging);
            string? noise = f.Anc && ancMode is { } m ? m switch { 1 => "Quiet", 2 => "Aware", 0 => "Off", 5 => "Custom", _ => ((AncMode)m).ToString() } : null;
            var tip = connected ? bat + (cs is null ? "" : $" · {cs}") + $"\n{model}" + (noise is null ? "" : $" · {noise}") : "BluntyBuds · not connected";
            _icon.Text = Clamp(tip);
            foreach (var (m2, it) in _anc) { it.Visible = connected && f.Anc && (m2 != AncMode.Custom || f.AncLevel); it.Checked = ancMode == (int)m2; }
            _sep1.Visible = connected && (f.Anc || f.Game);
            _game.Visible = connected && f.Game; _game.Checked = game;
        }
        catch (Exception ex) { Program.Log(ex, "tray update"); }
    }

    /// <summary>NotifyIcon.Text throws above 127 chars (63 on older frameworks); stay well under.</summary>
    public static string Clamp(string s) => s.Length <= 63 ? s : s[..62] + "…";
    static void Safe(Action a) { try { a(); } catch (Exception ex) { Program.Log(ex, "tray handler"); } }

    public void Toast(string title, string text, bool warn = false)
    {
        var d = System.Windows.Application.Current?.Dispatcher;
        if (d != null && !d.CheckAccess()) { d.BeginInvoke(() => Toast(title, text, warn)); return; }
        if (_disposed) return;
        try { _icon.ShowBalloonTip(5000, Clamp(title), text.Length > 250 ? text[..250] : text, warn ? F.ToolTipIcon.Warning : F.ToolTipIcon.Info); }
        catch (Exception ex) { Program.Log(ex, "tray toast"); }
    }

    public void Dispose()
    {
        if (_disposed) return; _disposed = true;
        try { _icon.Visible = false; _icon.Dispose(); _menu.Dispose(); } catch { }
    }
}
