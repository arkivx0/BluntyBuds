using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using BluntyBuds.Proto;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace BluntyBuds.Win;

public sealed class MainWindow : Window
{
    readonly BleClient _ble = new();
    readonly Tone _tone = new();
    readonly Presets _presets = new();
    readonly Settings _cfg = Settings.Load();
    readonly Tray _tray = new();
    readonly bool _startHidden, _demo;
    readonly BluetoothLEAdvertisementWatcher _watcher = new() { ScanningMode = BluetoothLEScanningMode.Active };
    readonly Dictionary<ulong, (string Name, short Rssi, AdParser.VendorAd? Ad)> _seen = new();

    readonly TextBlock _status = new() { Text = "Idle", TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    readonly ComboBox _devices = new() { Width = 330, MaxDropDownHeight = 260 };
    readonly CheckBox _showAll = new() { Content = "All devices", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
    readonly Grid _controls = new() { IsEnabled = false, Opacity = 0.45 };
    readonly TextBlock _battery = new() { Text = "L —   R —", FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _fw = new() { Text = "FW —", VerticalAlignment = VerticalAlignment.Center };
    readonly CheckBox _game = new() { Content = "Game mode", VerticalAlignment = VerticalAlignment.Center };
    readonly Slider[] _eq = new Slider[10];
    readonly TextBlock[] _eqLbl = new TextBlock[10];
    readonly ComboBox _presetBox = new() { Width = 150 };
    readonly Dictionary<(bool Left, Gesture G), ComboBox> _touch = new();
    readonly Slider _balance = new() { Minimum = -11, Maximum = 11, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 140, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _balLbl = new() { Text = "0", Width = 26, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
    readonly StackPanel _balBox = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
    readonly StackPanel _ancBox = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly Dictionary<AncMode, RadioButton> _anc = new();
    readonly TextBlock _ancOther = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    readonly StackPanel _ancLevelBox = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
    readonly Slider _ancLevel = new() { Minimum = ProtoA.AncLevelMin, Maximum = ProtoA.AncLevelMax, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 130, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _ancLevelLbl = new() { Width = 54, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _modelLbl = new() { FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    readonly Button _restoreEq;
    readonly CheckBox _autoRe = new() { Content = "Auto-reconnect", VerticalAlignment = VerticalAlignment.Center };
    readonly CheckBox _startWin = new() { Content = "Start with Windows", VerticalAlignment = VerticalAlignment.Center };
    readonly CheckBox _toTray = new() { Content = "Minimize to tray", VerticalAlignment = VerticalAlignment.Center };
    readonly CheckBox _closeTray = new() { Content = "Close to tray", VerticalAlignment = VerticalAlignment.Center };
    readonly DispatcherTimer _retry = new() { Interval = TimeSpan.FromSeconds(15) };
    readonly Backoff _backoff = new(15, 30, 60);
    readonly Generation _scanGen = new();
    readonly Coalescer _renderCoalesce = new(TimeSpan.FromMilliseconds(500));
    readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly System.Collections.ObjectModel.ObservableCollection<DevItem> _devItems = new();
    readonly DispatcherTimer _levelDebounce = new() { Interval = TimeSpan.FromMilliseconds(350) };
    readonly Dictionary<ulong, (string Name, string Kind, bool Connected)> _paired = new();
    DeviceModel _model = DeviceModel.Unknown;
    ulong? _connectedAddr;
    string _connectedName = "";
    int? _ancMode;
    bool _connecting, _userDisconnected, _lowWarned, _quitting, _closeHintThisSession;
    TouchMap _map = TouchMap.Defaults();
    int? _bl, _br;
    bool _cl, _cr;
    int _batGen;
    bool _sawL, _sawR;
    bool _suppress;

    // global hotkey Ctrl+Alt+N = cycle noise mode
    const int HotkeyId = 0x7A01, MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000, VK_N = 0x4E, WM_HOTKEY = 0x0312;
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, int mods, int vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);

    public MainWindow(bool startHidden = false, bool demo = false)
    {
        _startHidden = startHidden; _demo = demo;
        Title = "BluntyBuds — unofficial offline earbud controller";
        Width = 920; Height = 640; MinWidth = 820; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        // implicit Window style doesn't apply to subclasses, so set the theme brushes directly
        try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/bluntybuds.ico")); } catch (Exception ex) { Program.Log(ex, "window icon"); }
        Background = (Brush)FindResource("Bg"); Foreground = (Brush)FindResource("Fg");
        _restoreEq = B("My EQ", A(RestoreDeviceEq), small: true);
        _restoreEq.ToolTip = "Re-apply the EQ you last applied to this device";
        Content = new ScrollViewer { Content = Build(), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _watcher.Received += OnAdvert;
        _watcher.Stopped += (_, _) => Ui(() => { if (!_ble.Protocol.HasValue && !_connecting) _status.Text = "Scan stopped."; });
        _ble.Status += s => Ui(() => _status.Text = s + UntestedTag);
        _ble.EventReceived += e => Ui(() => { try { OnEvent(e); } catch (Exception ex) { Program.Log(ex, "event"); } });
        _ble.Disconnected += () => Ui(OnLinkLost);
        _retry.Tick += (_, _) => A(RetryTick)(this, new RoutedEventArgs());
        _renderTimer.Tick += (_, _) => { if (_renderCoalesce.ShouldFlush(DateTime.UtcNow)) Render(); };
        _renderTimer.Start();
        _devices.ItemsSource = _devItems; _devices.DisplayMemberPath = nameof(DevItem.Label);
        _levelDebounce.Tick += (_, _) => A(async () => { _levelDebounce.Stop(); await SendAncLevel(); })(this, new RoutedEventArgs());

        _tray.OpenRequested += () => Ui(ShowFromTray);
        _tray.QuitRequested += () => Ui(() => { _quitting = true; Close(); });
        Application.Current.SessionEnding += (_, _) => _quitting = true; // never block sign-out/shutdown
        _tray.AncRequested += m => Ui(() => A(() => SendAnc(m))(this, new RoutedEventArgs()));
        _tray.GameToggleRequested += () => Ui(() => { if (_controls.IsEnabled) _game.IsChecked = !(_game.IsChecked == true); });

        SourceInitialized += (_, _) =>
        {
            Theme.DarkTitleBar(this);
            try
            {
                var h = new WindowInteropHelper(this).Handle;
                HwndSource.FromHwnd(h)?.AddHook(WndProc);
                if (!RegisterHotKey(h, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_N))
                    _status.Text = "Note: Ctrl+Alt+N is in use by another app; the noise-mode hotkey is unavailable.";
            }
            catch (Exception ex) { Program.Log(ex, "hotkey"); }
        };
        StateChanged += (_, _) => { if (WindowState == WindowState.Minimized && _cfg.MinimizeToTray) Hide(); };
        Loaded += A(async () =>
        {
            if (_demo) { RunDemo(); return; }
            if (_startHidden) Hide();
            await ListPaired();
            await AutoConnect();
        });
        Closed += (_, _) =>
        {
            try { UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyId); } catch { }
            _retry.Stop();
            try { _watcher.Stop(); } catch { }
            _tone.Stop(); _ble.Dispose(); _tray.Dispose();
        };
    }

    IntPtr WndProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == WM_HOTKEY && w.ToInt32() == HotkeyId) { handled = true; A(CycleAnc)(this, new RoutedEventArgs()); }
        return IntPtr.Zero;
    }

    void ShowFromTray() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); }

    void Ui(Action a) => Dispatcher.BeginInvoke(a);
    RoutedEventHandler A(Func<Task> f) => async (_, _) =>
    {
        try { await f(); }
        catch (Exception ex) { Program.Log(ex, "UI action"); _status.Text = $"Error: {ex.Message} (0x{ex.HResult:X8})"; }
    };
    static Button B(string t, RoutedEventHandler h, bool small = false, bool accent = false)
    {
        var b = new Button { Content = t };
        if (accent) b.Style = (Style)Application.Current.FindResource("AccentButton");
        else if (small) b.Style = (Style)Application.Current.FindResource("SmallButton");
        b.Click += h; return b;
    }
    static WrapPanel WrapRow(params UIElement[] e) { var p = new WrapPanel(); foreach (var x in e) { if (x is FrameworkElement f && f is Button) f.Margin = new Thickness(0, 0, 5, 4); p.Children.Add(x); } return p; }
    static StackPanel Row(params UIElement[] e) { var p = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var x in e) p.Children.Add(x); return p; }
    static TextBlock Lbl(string t, double size = 12, bool dim = true) => new()
    {
        Text = t, FontSize = size, VerticalAlignment = VerticalAlignment.Center,
        Foreground = dim ? (Brush)Application.Current.FindResource("Dim") : (Brush)Application.Current.FindResource("Fg")
    };
    static Border Card(UIElement child, string? title = null, UIElement? titleExtra = null)
    {
        var sp = new DockPanel { LastChildFill = true };
        if (title != null)
        {
            var hdr = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var t = new TextBlock { Text = title.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = (Brush)Application.Current.FindResource("Dim"), VerticalAlignment = VerticalAlignment.Center };
            hdr.Children.Add(t);
            if (titleExtra != null) { DockPanel.SetDock(titleExtra, Dock.Right); titleExtra.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Right); hdr.Children.Add(titleExtra); }
            DockPanel.SetDock(hdr, Dock.Top); sp.Children.Add(hdr);
        }
        sp.Children.Add(child);
        return new Border { Background = (Brush)Application.Current.FindResource("Card"), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(0, 0, 0, 10), Child = sp };
    }
    static Border Sep() => new() { Width = 1, Background = (Brush)Application.Current.FindResource("Line"), Margin = new Thickness(14, 2, 14, 2) };

    UIElement Build()
    {
        var root = new DockPanel { Margin = new Thickness(14, 12, 14, 8) };

        // ---- header: device dropdown + actions + status ----
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var title = new TextBlock { Text = "BluntyBuds", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("Accent"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        _devices.ToolTip = "Paired and nearby earbuds";
        _showAll.Checked += (_, _) => Render(); _showAll.Unchecked += (_, _) => Render();
        var actions = Row(_devices, new Border { Width = 6 },
            B("Connect", A(() => ConnectSelected()), accent: true),
            B("Disconnect", (_, _) => UserDisconnect()),
            B("Scan", (_, _) => StartScan()),
            B("⟳", A(() => ListPaired()), small: true), _showAll);
        var quit = B("Quit", (_, _) => { _quitting = true; Close(); });
        quit.ToolTip = "Quit BluntyBuds (closing the window only hides it to the tray)";
        quit.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(quit, Dock.Right); header.Children.Add(quit);
        var about = B("About", (_, _) => ShowAbout()); about.Margin = new Thickness(6, 0, 0, 0);
        DockPanel.SetDock(about, Dock.Right); header.Children.Add(about);
        foreach (var c in actions.Children.OfType<Button>())
            c.ToolTip = (c.Content as string) switch { "Scan" => "Scan 12 s for advertising earbuds", "⟳" => "Refresh paired devices", _ => null };
        header.Children.Add(Row(title, actions));
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var statusRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        _status.Foreground = (Brush)FindResource("Dim"); _status.FontSize = 12;
        statusRow.Children.Add(_status);
        DockPanel.SetDock(statusRow, Dock.Top); root.Children.Add(statusRow);

        // ---- footer: app options ----
        _autoRe.IsChecked = _cfg.AutoReconnect;
        _autoRe.Checked += (_, _) => { _cfg.AutoReconnect = true; _cfg.Save(); };
        _autoRe.Unchecked += (_, _) => { _cfg.AutoReconnect = false; _cfg.Save(); _retry.Stop(); };
        _toTray.IsChecked = _cfg.MinimizeToTray;
        _toTray.Checked += (_, _) => { _cfg.MinimizeToTray = true; _cfg.Save(); };
        _toTray.Unchecked += (_, _) => { _cfg.MinimizeToTray = false; _cfg.Save(); };
        _closeTray.IsChecked = _cfg.CloseToTray;
        _closeTray.Checked += (_, _) => { _cfg.CloseToTray = true; _cfg.Save(); };
        _closeTray.Unchecked += (_, _) => { _cfg.CloseToTray = false; _cfg.Save(); };
        Settings.RefreshStartupPath();
        _startWin.IsChecked = Settings.StartWithWindows;
        _startWin.Checked += (_, _) => SetStartup(true); _startWin.Unchecked += (_, _) => SetStartup(false);
        var footer = Row(_autoRe, new Border { Width = 16 }, _toTray, new Border { Width = 16 }, _closeTray, new Border { Width = 16 }, _startWin, new Border { Width = 16 },
            Lbl("Unofficial offline earbud controller · Ctrl+Alt+N cycles noise mode · no internet, account or analytics", 11));
        footer.Margin = new Thickness(2, 2, 0, 0);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);

        root.Children.Add(_controls);
        _controls.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _controls.RowDefinitions.Add(new RowDefinition());
        _controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.12, GridUnitType.Star) });
        _controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        _controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // ---- strip: model · battery · fw | noise | game ----
        _game.Style = (Style)FindResource("Switch");
        _game.Checked += A(() => _suppress ? Task.CompletedTask : SendGame(true));
        _game.Unchecked += A(() => _suppress ? Task.CompletedTask : SendGame(false));
        var seg = new Border { Background = (Brush)FindResource("Ctl"), CornerRadius = new CornerRadius(7), Padding = new Thickness(1) };
        var segRow = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (m, t) in new[] { (AncMode.Off, "Off"), (AncMode.Anc, "Noise cancelling"), (AncMode.Transparency, "Ambient"), (AncMode.Custom, "Custom") })
        {
            var rb = new RadioButton { Content = t, GroupName = "anc", Style = (Style)FindResource("Segment") };
            var mm = m;
            rb.Checked += A(() => _suppress ? Task.CompletedTask : SendAnc(mm));
            _anc[m] = rb; segRow.Children.Add(rb);
        }
        seg.Child = segRow;
        _ancLevel.ValueChanged += (_, _) => { _ancLevelLbl.Text = $"Depth {(int)_ancLevel.Value}"; if (!_suppress) { _levelDebounce.Stop(); _levelDebounce.Start(); } };
        _ancLevelLbl.Text = $"Depth {(int)_ancLevel.Value}";
        _ancLevelBox.Children.Add(_ancLevelLbl); _ancLevelBox.Children.Add(_ancLevel);
        _ancLevelBox.ToolTip = "Noise-cancelling depth, 1–10";
        _ancBox.Children.Add(Lbl("NOISE", 11)); _ancBox.Children.Add(new Border { Width = 8 });
        _ancBox.Children.Add(seg); _ancBox.Children.Add(_ancLevelBox); _ancBox.Children.Add(_ancOther);
        _ancBox.Children.Add(Sep());
        var strip = new DockPanel();
        var refresh = B("⟳", A(() => RefreshAll()), small: true); refresh.ToolTip = "Refresh status from the earbuds";
        var left = Row(_modelLbl, Sep(), _battery, new Border { Width = 12 }, _fw, new Border { Width = 6 }, refresh, Sep(), _ancBox, _game);
        strip.Children.Add(left);
        var stripCard = Card(strip);
        Grid.SetColumnSpan(stripCard, 3);
        _controls.Children.Add(stripCard);

        // ---- EQ card (left) ----
        var eqGrid = new UniformGrid { Columns = 10, Height = 210 };
        for (int i = 0; i < 10; i++)
        {
            int k = i;
            var col = new DockPanel { Margin = new Thickness(2, 0, 2, 0) };
            _eqLbl[i] = new TextBlock { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(_eqLbl[i], Dock.Top); col.Children.Add(_eqLbl[i]);
            var f = EqGrid.Freqs[i];
            var fl = Lbl(f >= 1000 ? $"{f / 1000.0:0.#}k" : $"{f}", 11); fl.HorizontalAlignment = HorizontalAlignment.Center; fl.Margin = new Thickness(0, 4, 0, 0);
            DockPanel.SetDock(fl, Dock.Bottom); col.Children.Add(fl);
            _eq[i] = new Slider { Orientation = Orientation.Vertical, Minimum = -12, Maximum = 12, TickFrequency = 0.5, IsSnapToTickEnabled = true, SmallChange = 0.5, LargeChange = 1, HorizontalAlignment = HorizontalAlignment.Center };
            _eq[i].ValueChanged += (_, _) => EqLabel(k);
            col.Children.Add(_eq[i]);
            EqLabel(i);
            eqGrid.Children.Add(col);
        }
        _presetBox.Width = 310; // fits "AutoEQ · <model> (may differ by hardware revision)"; the tool row wraps on narrow windows
        var eqTools = WrapRow(_presetBox, new Border { Width = 6 }, B("Load", (_, _) => LoadPreset(), small: true), B("Save…", (_, _) => SavePreset(), small: true),
            B("Delete", (_, _) => DeletePreset(), small: true), B("Flat", (_, _) => SetEq(new float[10]), small: true),
            B("Import", (_, _) => ImportPresets(), small: true), B("Export", (_, _) => ExportPresets(), small: true), _restoreEq,
            Tip(B("AutoEQ file…", (_, _) => ImportAutoEq(), small: true), "Import an AutoEQ ParametricEQ.txt or GraphicEQ.txt and fit it to the 10 bands"));
        eqTools.Margin = new Thickness(0, 0, 0, 8);
        var eqBody = new DockPanel();
        DockPanel.SetDock(eqTools, Dock.Top); eqBody.Children.Add(eqTools);
        var apply = B("Apply EQ", A(() => SendEq()), accent: true); apply.HorizontalAlignment = HorizontalAlignment.Right; apply.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(apply, Dock.Bottom); eqBody.Children.Add(apply);
        eqBody.Children.Add(eqGrid);
        var eqCard = Card(eqBody, "Equalizer · ±12 dB");
        Grid.SetRow(eqCard, 1); _controls.Children.Add(eqCard);
        RefreshPresets(_cfg.LastPreset);

        // ---- right column: touch, balance, find ----
        var touch = new Grid();
        touch.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        touch.ColumnDefinitions.Add(new ColumnDefinition());
        touch.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        touch.ColumnDefinitions.Add(new ColumnDefinition());
        touch.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void Put(UIElement e, int r, int c) { Grid.SetRow(e, r); Grid.SetColumn(e, c); touch.Children.Add(e); }
        var lh = Lbl("Left", 11); lh.Margin = new Thickness(0, 0, 0, 3); Put(lh, 0, 1);
        var rh = Lbl("Right", 11); rh.Margin = new Thickness(0, 0, 0, 3); Put(rh, 0, 3);
        int row = 1;
        foreach (var g in Enum.GetValues<Gesture>())
        {
            touch.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var gl = Lbl(g + (g == Gesture.Long ? " press" : " tap"), 12, dim: false); gl.Margin = new Thickness(0, 0, 10, 0);
            Put(gl, row, 0);
            foreach (var isLeft in new[] { true, false })
            {
                var cb = new ComboBox { ItemsSource = TouchAction.All, Margin = new Thickness(0, 2, 0, 2), FontSize = 12 };
                _touch[(isLeft, g)] = cb; Put(cb, row, isLeft ? 1 : 3);
            }
            row++;
        }
        RenderTouch();
        var touchBody = new StackPanel();
        touchBody.Children.Add(touch);

        _balance.ValueChanged += (_, _) => _balLbl.Text = $"{(int)_balance.Value:+0;-0;0}";
        _balBox.Children.Add(Lbl("Balance  L", 12)); _balBox.Children.Add(new Border { Width = 6 });
        _balBox.Children.Add(_balance); _balBox.Children.Add(Lbl(" R", 12)); _balBox.Children.Add(_balLbl);
        _balBox.Children.Add(new Border { Width = 8 });
        _balBox.Children.Add(B("Apply", A(() => SendBalance()), small: true)); _balBox.Children.Add(B("Centre", (_, _) => _balance.Value = 0, small: true));
        touchBody.Children.Add(_balBox);

        var touchCard = Card(touchBody, "Touch controls", Row(B("Apply", A(() => SendTouch()), small: true), B("Defaults", (_, _) => { _map = TouchMap.Defaults(); RenderTouch(); }, small: true)));
        var find = Row(Lbl("Play a loud chirp (take them out of your ears):", 11), new Border { Width = 6 },
            B("L", (_, _) => _tone.Play(true, false), small: true), B("R", (_, _) => _tone.Play(false, true), small: true),
            B("Both", (_, _) => _tone.Play(true, true), small: true), B("Stop", (_, _) => _tone.Stop(), small: true));
        var findCard = Card(find, "Find earbuds");
        var rightCol = new StackPanel();
        rightCol.Children.Add(touchCard); rightCol.Children.Add(findCard);
        Grid.SetRow(rightCol, 1); Grid.SetColumn(rightCol, 2);
        _controls.Children.Add(rightCol);
        return root;
    }

    void SetConnectedUi(bool on) { _controls.IsEnabled = on; _controls.Opacity = on ? 1 : 0.45; }

    void SetStartup(bool on)
    {
        try { Settings.StartWithWindows = on; _status.Text = on ? "BluntyBuds will start (in the tray) when you sign in." : "Start with Windows turned off."; }
        catch (Exception ex) { _status.Text = "Couldn't change startup setting: " + ex.Message; }
    }

    void UserDisconnect()
    {
        _userDisconnected = true; _retry.Stop();
        _ble.Close(); SetConnectedUi(false); _connectedAddr = null; _status.Text = "Disconnected"; UpdateTray();
    }

    void OnLinkLost()
    {
        SetConnectedUi(false); _connectedAddr = null; UpdateTray();
        if (_cfg.AutoReconnect && !_userDisconnected && _cfg.LastAddress != null)
        {
            _backoff.Reset();
            _status.Text = "Earbuds disconnected. Will reconnect automatically when they're back (retrying after 15 s, 30 s, then every 60 s).";
            ScheduleRetry();
        }
        else _status.Text = "Disconnected";
    }

    void ScheduleRetry()
    {
        _retry.Stop();
        _retry.Interval = _backoff.Next();
        _retry.Start();
    }

    async Task RetryTick()
    {
        _retry.Stop(); // one attempt at a time; rescheduled below only after this attempt has finished
        if (_ble.Protocol.HasValue || !_cfg.AutoReconnect || _userDisconnected || _cfg.LastAddress is not { } a) return;
        if (_connecting) { ScheduleRetry(); return; }
        await ConnectTo(a, null, Models.Detect(_cfg.LastName), _cfg.LastName ?? "", quiet: true);
        if (_ble.Protocol.HasValue) _backoff.Reset();
        else if (_cfg.AutoReconnect && !_userDisconnected) { ScheduleRetry(); _status.Text += $" · next try in {_retry.Interval.TotalSeconds:0} s"; }
    }

    async Task AutoConnect()
    {
        if (!_cfg.AutoReconnect || _cfg.LastAddress is not { } a) return;
        _status.Text = $"Reconnecting to {_cfg.LastName ?? a.ToString("X12")}…";
        await ConnectTo(a, null, Models.Detect(_cfg.LastName), _cfg.LastName ?? "", quiet: true);
        if (!_ble.Protocol.HasValue)
        {
            _backoff.Reset();
            _status.Text = $"{_cfg.LastName ?? "Last device"} isn't reachable yet. Will keep trying (15 s, 30 s, then every 60 s; take the earbuds out of the case).";
            ScheduleRetry();
        }
    }

    async Task ConnectTo(ulong addr, Protocol? hint, DeviceModel model, string name, bool quiet = false)
    {
        if (_connecting) return;
        _connecting = true;
        try
        {
            SetConnectedUi(false);
            var attempt = _ble.ConnectAsync(addr, hint);
            var done = await Task.WhenAny(attempt, Task.Delay(_backoff.ConnectTimeout));
            if (done != attempt)
            {
                _status.Text = $"Connect timed out after {_backoff.ConnectTimeout.TotalSeconds:0} s.";
                _ble.Close();
                _ = attempt.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default); // observe late result/exception
                return;
            }
            if (!await attempt) return;
            _backoff.Reset();
            _userDisconnected = false;
            _model = model; _connectedAddr = addr; _connectedName = name;
            _cfg.LastAddress = addr; if (!string.IsNullOrEmpty(name)) _cfg.LastName = name; _cfg.Save();
            _lowWarned = false; _bl = _br = null; _cl = _cr = false; _ancMode = null;
            ApplyModel();
            SetConnectedUi(true);
            await RefreshAll();
            UpdateTray();
        }
        finally { _connecting = false; }
    }

    async Task CycleAnc()
    {
        if (!_ble.Protocol.HasValue || !Models.For(_model).Anc) { _tray.Toast("BluntyBuds", "Connected earbuds have no noise control — hotkey ignored."); return; }
        var next = _ancMode switch { 0 => AncMode.Anc, 1 => AncMode.Transparency, _ => AncMode.Off };
        await SendAnc(next);
        _tray.Toast("Noise mode", next switch { AncMode.Anc => "Noise cancelling", AncMode.Transparency => "Ambient", _ => "Off" });
    }

    void UpdateTray() => _tray.Update(_model.IsUnknown ? "Earbuds" : _model.ToString(), _bl, _br, _ble.Protocol.HasValue, _ancMode, Models.For(_model), _game.IsChecked == true, _cl, _cr);
    void ShowBattery()
    {
        _battery.Text = BatteryText.Format(_bl, _br, _cl, _cr);
        UpdateTray();
    }

    async Task SendAncLevel()
    {
        if (_ble.Protocol != Protocol.A || !Models.For(_model).AncLevel) return;
        if (_ancMode != (int)AncMode.Custom && !await _ble.SendAsync(ProtoA.SetAnc(AncMode.Custom))) return;
        await _ble.SendAsync(ProtoA.SetAncLevel((int)_ancLevel.Value));
        _ancMode = (int)AncMode.Custom; ShowAnc();
    }

    void ShowAnc()
    {
        _suppress = true;
        foreach (var (m, rb) in _anc) rb.IsChecked = _ancMode == (int)m;
        _ancOther.Text = _ancMode is { } x && !_anc.ContainsKey((AncMode)x) || (_ancMode == (int)AncMode.Custom && !Models.For(_model).AncLevel) ? $"({(AncMode)_ancMode!})" : "";
        _suppress = false;
        UpdateTray();
    }

    // ----- per-device EQ + import/export -----
    string? DevKey => _connectedAddr?.ToString("X12");
    void RememberDeviceEq(float[] g) { if (DevKey is { } k) { _cfg.EqByDevice[k] = g; _cfg.Save(); _restoreEq.IsEnabled = true; } }
    async Task RestoreDeviceEq()
    {
        if (DevKey is not { } k || !_cfg.EqByDevice.TryGetValue(k, out var g) || g.Length != 10) { _status.Text = "No saved EQ for this device yet (it's saved each time you Apply)."; return; }
        SetEq(g); await SendEq(); _status.Text = "Re-applied your saved EQ for this device.";
    }
    void ImportPresets()
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "BluntyBuds presets (*.json)|*.json|All files|*.*" };
            if (dlg.ShowDialog(this) != true) return;
            int n = _presets.Import(File.ReadAllText(dlg.FileName));
            RefreshPresets(); _status.Text = $"Imported {n} preset(s).";
        }
        catch (Exception ex) { _status.Text = "Import failed: " + ex.Message; }
    }
    static Button Tip(Button b, string t) { b.ToolTip = t; return b; }
    async void ImportAutoEq()
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "AutoEQ ParametricEQ / GraphicEQ (*.txt)|*.txt|All files|*.*" };
            if (dlg.ShowDialog(this) != true) return;
            SetEq(AutoEq.FitText(File.ReadAllText(dlg.FileName)));
            if (_ble.Protocol.HasValue) { await SendEq(); _status.Text = $"Applied {Path.GetFileName(dlg.FileName)}."; }
            else _status.Text = $"Fitted {Path.GetFileName(dlg.FileName)} to the sliders. Connect, then click Apply EQ, or Save… as a preset.";
        }
        catch (Exception ex) { _status.Text = "AutoEQ import failed: " + ex.Message; }
    }
    void ShowAbout()
    {
        var v = typeof(MainWindow).Assembly.GetName().Version;
        MessageBox.Show(this, $"BluntyBuds {v?.ToString(3)}\nUnofficial offline earbud controller. No internet access, no account, no analytics, no auto-update. MIT License.\n\n"
            + AutoEq.Attribution + "\n\nAutoEq is MIT licensed: permission is granted, free of charge, to use, copy, modify and distribute it, provided the copyright and permission notice are included. It is provided \"as is\", without warranty of any kind."
            + "\n\nThe AutoEQ profile-fitting idea was inspired by the tozo-eq project (github.com/vvr3ddy/tozo-eq).", "About BluntyBuds");
    }
    // AutoEQ profiles are listed with the presets; the connected model's own profile comes first.
    static string AutoEqName(string m) => "AutoEQ · " + m;
    // A model name can cover several hardware revisions: only a confident match is called "this model" and auto-selected.
    (AutoEq.Profile? P, bool Confident) MyAutoEqMatch => AutoEq.Suggest(_model);
    AutoEq.Profile? MyAutoEq => MyAutoEqMatch.P;
    Dictionary<string, float[]> PresetMap()
    {
        var d = new Dictionary<string, float[]>();
        var mine = MyAutoEq;
        if (mine is not null) d[AutoEq.SuggestLabel(mine, MyAutoEqMatch.Confident)] = mine.Gains;
        foreach (var kv in _presets.All()) d[kv.Key] = kv.Value;
        foreach (var p in AutoEq.Profiles) if (!ReferenceEquals(p, mine)) d[AutoEqName(p.Model)] = p.Gains;
        return d;
    }
    bool IsFixed(string n) => _presets.IsBuiltIn(n) || n.StartsWith("AutoEQ");
    void ExportPresets()
    {
        try
        {
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "BluntyBuds presets (*.json)|*.json", FileName = "bluntybuds-presets.json" };
            if (dlg.ShowDialog(this) != true) return;
            File.WriteAllText(dlg.FileName, _presets.Export());
            _status.Text = $"Exported {_presets.UserPresets().Count} custom preset(s) to {dlg.FileName}.";
        }
        catch (Exception ex) { _status.Text = "Export failed: " + ex.Message; }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // X / Alt+F4 hide to tray when "Close to tray" is on; tray "Quit" (or the setting off) really exits.
        if (!_quitting && !_demo && _cfg.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            // Once per session (not once ever): a reminder each time the app is started, without a toast on every X.
            if (!_closeHintThisSession)
            {
                _closeHintThisSession = true;
                _tray.Toast("BluntyBuds", "BluntyBuds is still running in the tray. Use Quit (in the window or the tray menu) to exit.");
            }
            return;
        }
        base.OnClosing(e);
    }

    // ----- scanning -----
    void StartScan()
    {
        _seen.Clear(); Render();
        try { _watcher.Start(); } catch (Exception ex) { _status.Text = "Scan failed: " + ex.Message; return; }
        _status.Text = "Scanning 12 s… (take the earbuds out of the case; disconnect the phone app)";
        var gen = _scanGen.Begin(); // a newer scan invalidates this timer, so it can't stop the new scan early
        _ = Task.Delay(12000).ContinueWith(_ => Ui(() => { if (_scanGen.IsCurrent(gen)) try { _watcher.Stop(); } catch { } }));
    }

    void OnAdvert(BluetoothLEAdvertisementWatcher w, BluetoothLEAdvertisementReceivedEventArgs a)
    {
        AdParser.VendorAd? ad = null;
        foreach (var sec in a.Advertisement.DataSections)
        {
            if (sec.DataType != 0xFF) continue;
            var b = new byte[sec.Data.Length];
            DataReader.FromBuffer(sec.Data).ReadBytes(b);
            ad = AdParser.ParseManufacturer(b);
            if (ad != null) break;
        }
        var name = a.Advertisement.LocalName;
        Ui(() =>
        {
            var prev = _seen.TryGetValue(a.BluetoothAddress, out var p) ? p : default;
            _seen[a.BluetoothAddress] = (string.IsNullOrEmpty(name) ? prev.Name ?? "" : name, a.RawSignalStrengthInDBm, ad ?? prev.Ad);
            _renderCoalesce.Mark(); // list refreshes at most ~2x/s
        });
    }

    sealed record DevItem(ulong? Address, string? Id, Protocol? Hint, DeviceModel Model, string Label) { public override string ToString() => Label; }

    static bool IsKnownEarbud(string n) => n.Contains("TOZO", StringComparison.OrdinalIgnoreCase);

    void Render()
    {
        var sel = (_devices.SelectedItem as DevItem)?.Address;
        var items = new List<DevItem>();
        var covered = new HashSet<ulong>();
        foreach (var (addr, v) in _seen)
        {
            if (v.Ad is null && !IsKnownEarbud(v.Name) && _showAll.IsChecked != true) continue;
            var model = Models.Detect(v.Name, v.Ad?.Mark);
            ulong classic = 0;
            if (v.Ad != null) ulong.TryParse(v.Ad.ClassicMac.Replace(":", ""), System.Globalization.NumberStyles.HexNumber, null, out classic);
            string pairedName = "";
            foreach (var k in new[] { addr, classic }) if (_paired.TryGetValue(k, out var pp)) { covered.Add(k); pairedName = pp.Name; }
            if (model.IsUnknown && pairedName != "") model = Models.Detect(pairedName);
            var name = v.Name != "" ? v.Name : pairedName != "" ? pairedName : "(no name)";
            var extra = v.Ad is null ? "" : $"  · mark {v.Ad.Mark} · hint {AdParser.ProtocolHint(v.Ad.Mark)}";
            items.Add(new DevItem(addr, null, v.Ad is null ? null : AdParser.ProtocolHint(v.Ad.Mark), model,
                $"{name}  [{addr:X12}]  {(model.IsUnknown ? "" : model + " · ")}advertising {v.Rssi} dBm{extra}"));
        }
        foreach (var (addr, p) in _paired)
        {
            if (covered.Contains(addr) || (!IsKnownEarbud(p.Name) && _showAll.IsChecked != true)) continue;
            var model = Models.Detect(p.Name);
            items.Add(new DevItem(addr, null, null, model,
                $"{p.Name}  [{addr:X12}]  {(model.IsUnknown ? "" : model + " · ")}paired ({p.Kind}){(p.Connected ? " · connected" : "")}"));
        }
        // update in place: keep existing rows (and the selection / open dropdown) where nothing changed
        if (_devices.IsDropDownOpen && items.Count == _devItems.Count && items.Select(i => i.Address).SequenceEqual(_devItems.Select(i => i.Address)))
        {
            for (int i = 0; i < items.Count; i++) if (items[i] != _devItems[i] && !ReferenceEquals(_devices.SelectedItem, _devItems[i])) _devItems[i] = items[i];
            return;
        }
        for (int i = _devItems.Count - 1; i >= 0; i--) if (!items.Any(x => x.Address == _devItems[i].Address)) _devItems.RemoveAt(i);
        for (int i = 0; i < items.Count; i++)
        {
            int j = -1; for (int k = 0; k < _devItems.Count; k++) if (_devItems[k].Address == items[i].Address) { j = k; break; }
            if (j < 0) _devItems.Insert(Math.Min(i, _devItems.Count), items[i]);
            else { if (j != i) _devItems.Move(j, i); if (_devItems[i] != items[i]) _devItems[i] = items[i]; }
        }
        var want = _devItems.FirstOrDefault(i => i.Address == sel) ?? _devItems.FirstOrDefault(i => i.Address == _cfg.LastAddress) ?? _devItems.FirstOrDefault();
        if (!ReferenceEquals(_devices.SelectedItem, want)) _devices.SelectedItem = want;
    }

    async Task ListPaired()
    {
        _paired.Clear();
        foreach (var (sel, kind) in new[] { (BluetoothDevice.GetDeviceSelectorFromPairingState(true), "classic"), (BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), "LE") })
        {
            try
            {
                foreach (var d in await DeviceInformation.FindAllAsync(sel))
                {
                    try
                    {
                        ulong addr; bool conn; string name;
                        if (kind == "classic") { using var bd = await BluetoothDevice.FromIdAsync(d.Id); if (bd is null) continue; addr = bd.BluetoothAddress; conn = bd.ConnectionStatus == BluetoothConnectionStatus.Connected; name = bd.Name; }
                        else { using var ld = await BluetoothLEDevice.FromIdAsync(d.Id); if (ld is null) continue; addr = ld.BluetoothAddress; conn = ld.ConnectionStatus == BluetoothConnectionStatus.Connected; name = ld.Name; }
                        if (string.IsNullOrEmpty(name)) name = d.Name;
                        if (_paired.TryGetValue(addr, out var ex)) _paired[addr] = (ex.Name, ex.Kind + "+" + kind, ex.Connected || conn);
                        else _paired[addr] = (name, kind, conn);
                    }
                    catch (Exception ex) { Program.Log(ex, "paired " + d.Name); }
                }
            }
            catch (Exception ex) { _status.Text = "Listing paired devices failed: " + ex.Message; }
        }
        Render();
        int n = _paired.Count(p => IsKnownEarbud(p.Value.Name));
        _status.Text = $"{n} paired earbud(s). Take the earbuds out of the case, select one, and Connect (Scan if it doesn't connect).";
    }

    async Task ConnectSelected()
    {
        if (_devices.SelectedItem is not DevItem d) { _status.Text = "Select a device first."; return; }
        try { _watcher.Stop(); } catch { }
        _retry.Stop();
        if (d.Address is { } a)
        {
            var name = d.Label.Split("  [")[0];
            await ConnectTo(a, d.Hint, d.Model, name);
        }
        else if (await _ble.ConnectByIdAsync(d.Id!, d.Hint)) { _model = d.Model; ApplyModel(); SetConnectedUi(true); await RefreshAll(); }
    }

    string UntestedTag => _ble.Protocol.HasValue && !_model.IsUnknown && !_model.Tested ? " · untested model" : "";

    void ApplyModel()
    {
        var f = Models.For(_model);
        _modelLbl.Text = _model.IsUnknown ? "Earbuds (generic)" : _model.Tested ? _model.ToString() : $"{_model} (untested)";
        _modelLbl.ToolTip = $"Protocol {_ble.Protocol}" + (_model.Mark is { } mk ? $" · id {mk}" : "") + (_model.IsUnknown ? " · model not recognised, showing generic features"
            : _model.Tested ? "" : " · untested model: features come from the built-in model table and have not been verified on hardware");
        if (UntestedTag != "" && !_status.Text.EndsWith(UntestedTag)) _status.Text += UntestedTag;
        _ancLevelBox.Visibility = f.AncLevel && _ble.Protocol == Protocol.A ? Visibility.Visible : Visibility.Collapsed;
        _anc[AncMode.Custom].Visibility = _ancLevelBox.Visibility;
        _restoreEq.IsEnabled = DevKey is { } k && _cfg.EqByDevice.ContainsKey(k);
        _ancBox.Visibility = f.Anc && _ble.Protocol == Protocol.A ? Visibility.Visible : Visibility.Collapsed;
        _balBox.Visibility = f.Balance && _ble.Protocol == Protocol.A ? Visibility.Visible : Visibility.Collapsed;
        _game.Visibility = f.Game ? Visibility.Visible : Visibility.Collapsed;
        // Same as Android: the last-used preset wins; otherwise a confident AutoEQ match; otherwise the first entry.
        var names = PresetMap().Keys;
        var last = _cfg.LastPreset is { } lp && names.Contains(lp) ? lp : null;
        var (ae, sure) = MyAutoEqMatch;
        RefreshPresets(last ?? (ae is not null && sure ? AutoEq.SuggestLabel(ae, true) : null));
    }

    async Task SendAnc(AncMode m)
    {
        if (_ble.Protocol != Protocol.A || !Models.For(_model).Anc) return;
        if (m == AncMode.Custom && !Models.For(_model).AncLevel) return;
        await _ble.SendAsync(ProtoA.SetAnc(m));
        if (m == AncMode.Custom) await _ble.SendAsync(ProtoA.SetAncLevel((int)_ancLevel.Value));
        _ancMode = (int)m; ShowAnc();
    }

    // ----- commands -----
    /// <summary>--demo: fills the UI with made-up data for screenshots. No Bluetooth, no settings written.</summary>
    void RunDemo()
    {
        _retry.Stop(); _renderTimer.Stop();
        _ble.EnterDemo();
        _devItems.Clear();
        var item = new DevItem(0x001122334455, null, Protocol.A, Models.Detect("TOZO NC9"), "TOZO NC9 · 00:11:22:33:44:55 · connected (demo)");
        _devItems.Add(item); _devices.SelectedItem = item;
        _model = item.Model;
        ApplyModel(); SetConnectedUi(true);
        foreach (var e in new Event[] { new Event.Battery(85, null, ChargingLeft: true, Snapshot: true), new Event.Firmware("1.0.0", "1.0.0"), new Event.Anc((int)AncMode.Anc),
                     new Event.Game(false), new Event.Eq(new float[] { 3, 2, 1, 0, 0, 0, 1, 2, 2, 1 }), new Event.Balance(0), new Event.Touch(TouchMap.Defaults()) })
            OnEvent(e);
        _status.Text = "Demo mode: made-up data, Bluetooth not used";
    }

    async Task RefreshAll()
    {
        if (_ble.Protocol == Protocol.A)
            foreach (var c in new[] { ProtoA.GetBattery, ProtoA.GetFwVersion, ProtoA.GetGame, ProtoA.GetButtons, ProtoA.GetEq, ProtoA.GetBalance }
                     .Concat(Models.For(_model).Anc ? new[] { ProtoA.GetAncMode } : Array.Empty<int>())
                     .Concat(Models.For(_model).AncLevel ? new[] { ProtoA.GetCustomAncLevel } : Array.Empty<int>()))
                await _ble.SendAsync(ProtoA.Get(c));
        else if (_ble.Protocol == Protocol.J)
        {
            int gen = ++_batGen; _sawL = _sawR = false;
            foreach (var c in new[] { ProtoJ.BatteryLeft, ProtoJ.BatteryRight, ProtoJ.Version, ProtoJ.GameGet, ProtoJ.ButtonGet, ProtoJ.EqGet })
                await _ble.SendAsync(ProtoJ.Get(c));
            await Task.Delay(1500);
            if (gen != _batGen) return;
            if (!_sawL) { _bl = null; _cl = false; }
            if (!_sawR) { _br = null; _cr = false; }
            ShowBattery();
        }
    }
    Task SendGame(bool on) => _ble.Protocol switch
    {
        Protocol.A => _ble.SendAsync(ProtoA.SetGameMode(on)),
        Protocol.J => _ble.SendAsync(ProtoJ.SetGame(on)),
        _ => Task.CompletedTask
    };
    float[] EqUi() => _eq.Select(s => (float)s.Value).ToArray();
    void SetEq(float[] g) { for (int i = 0; i < 10; i++) _eq[i].Value = Math.Clamp(g[i], -12, 12); }
    void EqLabel(int i) { var f = EqGrid.Freqs[i]; _eqLbl[i].Text = $"{_eq[i].Value:+0.#;-0.#;0}"; }
    async Task SendEq()
    {
        var g = EqUi();
        switch (_ble.Protocol)
        {
            case Protocol.A: await _ble.SendAsync(ProtoA.SetEqGains(g)); break;
            case Protocol.J: await _ble.SendAsync(ProtoJ.SetEq(g)); break;
            default: return;
        }
        RememberDeviceEq(g);
    }
    TouchMap TouchUi()
    {
        var m = _map.Copy();
        foreach (var ((left, g), cb) in _touch)
            if (cb.SelectedItem is TouchAction a) (left ? m.Left : m.Right)[(int)g] = a.A;
        return m;
    }
    void RenderTouch()
    {
        foreach (var ((left, g), cb) in _touch)
            cb.SelectedItem = TouchAction.FromA((left ? _map.Left : _map.Right)[(int)g]) ?? TouchAction.None;
    }
    Task SendTouch()
    {
        _map = TouchUi();
        return _ble.Protocol switch
        {
            Protocol.A => _ble.SendAsync(ProtoA.SetTouch(_map)),
            Protocol.J => _ble.SendAsync(ProtoJ.SetTouch(_map)),
            _ => Task.CompletedTask
        };
    }
    Task SendBalance() => _ble.Protocol == Protocol.A ? _ble.SendAsync(ProtoA.SetBalanceValue((int)_balance.Value)) : Task.CompletedTask;

    // ----- presets -----
    void RefreshPresets(string? select = null)
    {
        var names = PresetMap().Keys.ToList();
        _presetBox.ItemsSource = names;
        _presetBox.SelectedItem = select is not null && names.Contains(select) ? select : names.FirstOrDefault();
    }
    async void LoadPreset()
    {
        if (_presetBox.SelectedItem is string n && PresetMap().TryGetValue(n, out var g))
        {
            SetEq(g);
            if (!_demo) { _cfg.LastPreset = n; _cfg.Save(); }
            if (_ble.Protocol.HasValue) { await SendEq(); _status.Text = $"Applied \"{n}\"."; }
            else _status.Text = $"Loaded \"{n}\" onto the sliders. Connect, then click Load again to send it.";
        }
    }
    void SavePreset()
    {
        var tb = new TextBox { Width = 220 };
        var ok = new Button { Content = "Save", IsDefault = true, Margin = new Thickness(6, 0, 0, 0) };
        var dlg = new Window { Title = "Preset name", SizeToContent = SizeToContent.WidthAndHeight, Owner = this, Content = Row(tb, ok), WindowStartupLocation = WindowStartupLocation.CenterOwner };
        ok.Click += (_, _) => dlg.DialogResult = true;
        if (dlg.ShowDialog() != true) return;
        var n = tb.Text.Trim();
        if (n.Length == 0 || IsFixed(n)) { _status.Text = "Pick a new preset name."; return; }
        _presets.Save(n, EqUi()); RefreshPresets(n);
    }
    void DeletePreset()
    {
        if (_presetBox.SelectedItem is not string n) return;
        if (IsFixed(n)) { _status.Text = "Built-in presets can't be deleted."; return; }
        _presets.Delete(n); RefreshPresets();
    }

    // ----- events -----
    void OnEvent(Event e)
    {
        switch (e)
        {
            case Event.Battery b:
                if (b.Snapshot)
                {
                    _bl = b.Left; _br = b.Right; _cl = b.ChargingLeft; _cr = b.ChargingRight;
                }
                else
                {
                    if (b.ReportsLeft) { _bl = b.Left; _cl = b.ChargingLeft; _sawL = true; }
                    if (b.ReportsRight) { _br = b.Right; _cr = b.ChargingRight; _sawR = true; }
                }
                ShowBattery();
                var low = BatteryText.Lowest(_bl, _br);
                if (low is int p && p <= _cfg.LowBatteryPercent && !_lowWarned) { _lowWarned = true; _tray.Toast("BluntyBuds · low battery", $"{_connectedName} {_battery.Text}", warn: true); }
                else if (low is null || low > _cfg.LowBatteryPercent + 5) _lowWarned = false;
                break;
            case Event.Firmware f: _fw.Text = $"FW {f.Left}" + (f.Right is null || f.Right == f.Left ? "" : $" / {f.Right}"); _fw.Foreground = (Brush)FindResource("Dim"); break;
            case Event.Game g: _suppress = true; _game.IsChecked = g.On; _suppress = false; UpdateTray(); break;
            case Event.Touch t:
                for (int i = 0; i < 5; i++) { if (t.Map.Left[i] >= 0) _map.Left[i] = t.Map.Left[i]; if (t.Map.Right[i] >= 0) _map.Right[i] = t.Map.Right[i]; }
                RenderTouch(); break;
            case Event.Eq q: if (q.Gains.Length == 10) SetEq(q.Gains); break;
            case Event.Anc an: _ancMode = an.Mode; ShowAnc(); break;
            case Event.AncLevel lv: _suppress = true; _ancLevel.Value = lv.Level; _suppress = false; break;
            case Event.Balance bal: _balance.Value = Math.Clamp(bal.Value, -11, 11); break;
            case Event.Ack a: _status.Text = a.Ok ? "Saved ✓" : $"Earbuds rejected the change (0x{a.Cmd:X4})"; break;
        }
    }
}
