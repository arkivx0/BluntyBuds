using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using Panel = System.Windows.Controls.Panel;
using TouchAction = BluntyBuds.Proto.TouchAction;
using System.Windows.Threading;
using BluntyBuds.Proto;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace BluntyBuds.Win;

/// <summary>
/// Home: one window, no scrolling. Top strip (device, connection dot, L / Case / R battery pills, Offline badge),
/// noise control, Tune (10-band EQ with Lock EQ) and Sound Profile chips, then icon buttons that open compact flyouts.
/// </summary>
public sealed class MainWindow : Window
{
    readonly BleClient _ble = new();
    readonly Tone _tone = new();
    readonly Presets _presets = new();
    readonly Settings _cfg = Settings.Load();
    readonly Tray _tray = new();
    readonly bool _startHidden, _demo;
    BluetoothLEAdvertisementWatcher? _watcherObj;
    /// <summary>Created on first use, so --demo never touches the Bluetooth stack.</summary>
    BluetoothLEAdvertisementWatcher _watcher
    {
        get
        {
            if (_watcherObj != null) return _watcherObj;
            var w = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
            w.Received += OnAdvert;
            w.Stopped += (_, _) => Ui(() => { if (!_ble.Protocol.HasValue && !_connecting) _status.Text = "Scan stopped."; });
            return _watcherObj = w;
        }
    }
    readonly Dictionary<ulong, (string Name, short Rssi, AdParser.VendorAd? Ad)> _seen = new();

    readonly TextBlock _status = new() { Text = "Idle", TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
    readonly ComboBox _devices = new() { Width = 300, MaxDropDownHeight = 260 };
    readonly CheckBox _showAll = new() { Content = "All", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    readonly Grid _controls = new() { IsEnabled = false, Opacity = 0.45 };
    readonly TextBlock _deviceName = new() { Text = "Not connected", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    readonly Ellipse _dot = new() { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    readonly Border _chipL = new(), _chipR = new();
    readonly TextBlock _solo = new() { Text = "Solo", FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0), Visibility = Visibility.Collapsed };
    readonly Pill _pillL = new("L", false), _pillR = new("R", false), _pillCase = new("Case", true);
    readonly TextBlock _fw = new() { Text = "FW —", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    readonly CheckBox _game = new() { Content = "Low-Latency", VerticalAlignment = VerticalAlignment.Center };
    readonly Slider[] _eq = new Slider[10];
    readonly TextBlock[] _eqLbl = new TextBlock[10];
    readonly UniformGrid _eqGrid = new() { Columns = 10 };
    readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal };
    readonly Button _lock = new() { Width = 32, Height = 32, Padding = new Thickness(0) };
    string? _selected;
    readonly Dictionary<(bool Left, Gesture G), ComboBox> _touch = new();
    readonly Slider _balance = new() { Minimum = -11, Maximum = 11, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 200, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _balLbl = new() { Text = "0", Width = 28, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
    readonly Border _ancCard = new();
    readonly Dictionary<AncMode, RadioButton> _anc = new();
    readonly TextBlock _ancOther = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), FontSize = 12 };
    readonly DockPanel _ancLevelBox = new() { Margin = new Thickness(0, 8, 0, 0), Height = 32 };
    readonly Slider _ancLevel = new() { Minimum = ProtoA.AncLevelMin, Maximum = ProtoA.AncLevelMax, TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _ancLevelLbl = new() { Width = 72, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    readonly Button _restoreEq;
    readonly Button _btnGestures, _btnBalance, _btnLowLat, _btnPing;
    readonly Path _lowLatIcon;
    readonly Border _snack = new() { Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 80), Padding = new Thickness(16, 4, 8, 4), CornerRadius = new CornerRadius(8), MinWidth = 280 };
    readonly TextBlock _snackText = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    readonly Button _snackUndo = new() { Content = "Undo", Margin = new Thickness(16, 0, 0, 0) };
    readonly DispatcherTimer _snackTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    Action? _undo;
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
    readonly DispatcherTimer _eqDebounce = new() { Interval = TimeSpan.FromMilliseconds(350) };
    readonly DispatcherTimer _balDebounce = new() { Interval = TimeSpan.FromMilliseconds(350) };
    readonly DispatcherTimer _hold = new() { Interval = TimeSpan.FromMilliseconds(600) };
    readonly Dictionary<ulong, (string Name, string Kind, bool Connected)> _paired = new();
    DeviceModel _model = DeviceModel.Unknown;
    ulong? _connectedAddr;
    string _connectedName = "";
    int? _ancMode;
    bool _connecting, _userDisconnected, _lowWarned, _quitting, _closeHintThisSession;
    TouchMap _map = TouchMap.Defaults();
    int? _bl, _br, _case;
    bool _cl, _cr, _caseCharging;
    int _batGen;
    bool _sawL, _sawR;
    bool _suppress, _suppressEq;
    string? _holdName; bool _holdFired;

    // global hotkey Ctrl+Alt+N = cycle noise mode
    const int HotkeyId = 0x7A01, MOD_ALT = 1, MOD_CONTROL = 2, MOD_NOREPEAT = 0x4000, VK_N = 0x4E, WM_HOTKEY = 0x0312;
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr h, int id, int mods, int vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);

    static Brush R(string k) => (Brush)Application.Current.FindResource(k);

    // 24x24 line icons (no icon font or image files needed)
    const string IcShield = "M12,3 L20,6 L20,12 C20,17 16,20 12,21.5 C8,20 4,17 4,12 L4,6 Z M8.5,12 L11,14.5 L15.5,9.5";
    const string IcLock = "M5,11 H19 V21 H5 Z M8,11 V7.5 C8,2.5 16,2.5 16,7.5 V11";
    const string IcUnlock = "M5,11 H19 V21 H5 Z M8,11 V7.5 C8,2.5 16,2.5 16,7.5";
    const string IcGesture = "M9,13 V5 C9,3 12,3 12,5 V11 L17,12 C19,12.5 19.5,14 19,16 L18,21 H10 L6,15 C5.5,13.5 7.5,12.5 9,14 M5,6 H7 M14,6 H16";
    const string IcBalance = "M4,9 H20 M16,5 L20,9 L16,13 M20,15 H4 M8,11 L4,15 L8,19";
    const string IcBolt = "M13,2.5 L5,13.5 H11.5 L10.5,21.5 L19,10 H12.5 Z";
    const string IcPing = "M14.5,12 A2.5,2.5 0 1 1 9.5,12 A2.5,2.5 0 1 1 14.5,12 M16.8,9.2 A5.5,5.5 0 0 1 16.8,14.8 M7.2,9.2 A5.5,5.5 0 0 0 7.2,14.8 M20.2,7.3 A9.5,9.5 0 0 1 20.2,16.7 M3.8,7.3 A9.5,9.5 0 0 0 3.8,16.7";
    const string IcInfo = "M21,12 A9,9 0 1 1 3,12 A9,9 0 1 1 21,12 M12,11 V16.5 M12,7.6 V7.8";
    static Path IconPath(string data, Brush b) => new() { Data = Geometry.Parse(data), Stroke = b, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    static Viewbox Ico(Path p, double size = 20) => new() { Width = size, Height = size, Child = new Canvas { Width = 24, Height = 24, Children = { p } } };

    /// <summary>Battery pill: number plus a bar. A missing earbud is faded to an outline, never shown as 0%.</summary>
    sealed class Pill : Border
    {
        readonly string _side; readonly TextBlock _t = new() { HorizontalAlignment = HorizontalAlignment.Center };
        readonly ColumnDefinition _a = new(), _b = new(); readonly Grid _bar = new() { Height = 4, Margin = new Thickness(0, 4, 0, 0) };
        public Pill(string side, bool small)
        {
            _side = side; _t.FontSize = small ? 12 : 14; if (!small) _t.FontWeight = FontWeights.SemiBold;
            CornerRadius = new CornerRadius(10); Padding = new Thickness(8, 4, 8, 6); BorderThickness = new Thickness(1);
            _bar.ColumnDefinitions.Add(_a); _bar.ColumnDefinitions.Add(_b);
            var fill = new Border { Background = R("Accent"), CornerRadius = new CornerRadius(2) };
            var rest = new Border { Background = R("Line"), CornerRadius = new CornerRadius(2) };
            Grid.SetColumn(rest, 1); _bar.Children.Add(fill); _bar.Children.Add(rest);
            Child = new StackPanel { Children = { _t, _bar } };
            Set(null, false, false);
        }
        public void Set(int? pct, bool charging, bool missing)
        {
            if (pct is int p)
            {
                _t.Text = $"{_side} {p}%" + (charging ? " ⚡" : ""); _t.Foreground = R("Fg");
                _a.Width = new GridLength(Math.Max(p, 0.01), GridUnitType.Star); _b.Width = new GridLength(Math.Max(100 - p, 0.01), GridUnitType.Star);
                _bar.Visibility = Visibility.Visible; Background = R("Ctl"); BorderBrush = R("Ctl"); Opacity = 1;
                ToolTip = $"{_side}: {p}%" + (charging ? ", charging" : "");
            }
            else
            {
                _t.Text = missing ? _side : $"{_side} —"; _t.Foreground = R("Dim");
                _bar.Visibility = Visibility.Hidden; Background = Brushes.Transparent; BorderBrush = R("Line"); Opacity = missing ? 0.55 : 0.8;
                ToolTip = missing ? $"{_side} earbud not in use" : $"{_side}: unknown";
            }
        }
    }

    public MainWindow(bool startHidden = false, bool demo = false)
    {
        _startHidden = startHidden; _demo = demo;
        Title = "BluntyBuds";
        Width = 600; Height = 680; MinWidth = 560; MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        // implicit Window style doesn't apply to subclasses, so set the theme brushes directly
        try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/bluntybuds.ico")); } catch (Exception ex) { Program.Log(ex, "window icon"); }
        Background = R("Bg"); Foreground = R("Fg"); FontSize = 13;
        _restoreEq = B("My EQ", A(RestoreDeviceEq));
        _restoreEq.ToolTip = "Re-apply the Tune you last sent to this device";
        _lowLatIcon = IconPath(IcBolt, R("Fg"));
        _btnGestures = Tool(IconPath(IcGesture, R("Fg")), "Gestures", b => Flyout(b, "Gestures", GesturesPanel()));
        _btnBalance = Tool(IconPath(IcBalance, R("Fg")), "L-R Balance", b => Flyout(b, "L-R Balance", BalancePanel()));
        _btnLowLat = Tool(_lowLatIcon, "Low-Latency", b => Flyout(b, "Low-Latency", LowLatPanel()));
        _btnPing = Tool(IconPath(IcPing, R("Fg")), "Ping Buds", b => Flyout(b, "Ping Buds", PingPanel()));
        var host = new Grid();
        host.Children.Add(Build());
        host.Children.Add(_snack);
        Content = host;
        _ble.Status += s => Ui(() => _status.Text = s + UntestedTag);
        _ble.EventReceived += e => Ui(() => { try { OnEvent(e); } catch (Exception ex) { Program.Log(ex, "event"); } });
        _ble.Disconnected += () => Ui(OnLinkLost);
        _retry.Tick += (_, _) => A(RetryTick)(this, new RoutedEventArgs());
        _renderTimer.Tick += (_, _) => { if (_renderCoalesce.ShouldFlush(DateTime.UtcNow)) Render(); };
        _renderTimer.Start();
        _devices.ItemsSource = _devItems; _devices.DisplayMemberPath = nameof(DevItem.Label);
        _levelDebounce.Tick += (_, _) => A(async () => { _levelDebounce.Stop(); await SendAncLevel(); })(this, new RoutedEventArgs());
        _eqDebounce.Tick += (_, _) => A(async () => { _eqDebounce.Stop(); await SendEq(); })(this, new RoutedEventArgs());
        _balDebounce.Tick += (_, _) => A(async () => { _balDebounce.Stop(); await SendBalance(); })(this, new RoutedEventArgs());
        _snackTimer.Tick += (_, _) => HideSnack();
        _hold.Tick += (_, _) => { _hold.Stop(); if (_holdName is { } n && _chips.Children.OfType<Button>().FirstOrDefault(c => (string?)c.Tag == n) is { } c) { _holdFired = true; ProfileMenu(c, n); } };

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
            RenderConnection(false);
            if (_demo) { RunDemo(); return; }
            if (_startHidden) Hide();
            await ListPaired();
            await AutoConnect();
        });
        Closed += (_, _) =>
        {
            try { UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyId); } catch { }
            _retry.Stop();
            try { _watcherObj?.Stop(); } catch { }
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
    static Button B(string t, RoutedEventHandler h, bool accent = false)
    {
        var b = new Button { Content = t, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
        if (accent) b.Style = (Style)Application.Current.FindResource("AccentButton");
        b.Click += h; return b;
    }
    static StackPanel Row(params UIElement[] e) { var p = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var x in e) p.Children.Add(x); return p; }
    static WrapPanel WrapRow(params UIElement[] e) { var p = new WrapPanel(); foreach (var x in e) { if (x is FrameworkElement f) f.Margin = new Thickness(0, 0, 8, 8); p.Children.Add(x); } return p; }
    static TextBlock Lbl(string t, double size = 12, bool dim = true) => new()
    {
        Text = t, FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
        Foreground = dim ? R("Dim") : R("Fg")
    };
    static Border Card(UIElement child, double bottom = 8) => new() { Background = R("Card"), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, bottom), Child = child };

    Button Tool(Path icon, string label, Action<Button> open)
    {
        var b = new Button { Style = (Style)FindResource("ToolButton"), ToolTip = label, Margin = new Thickness(0, 0, 8, 0) };
        b.Content = new StackPanel { Children = { Ico(icon), new TextBlock { Text = label, FontSize = 12, Foreground = R("Dim"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) } } };
        AutomationName(b, label);
        b.Click += (_, _) => { try { open(b); } catch (Exception ex) { Program.Log(ex, "flyout"); } };
        return b;
    }
    static void AutomationName(DependencyObject d, string n) => System.Windows.Automation.AutomationProperties.SetName(d, n);

    Popup? _openFlyout;
    /// <summary>Compact flyout anchored to a button; closes when you click elsewhere.</summary>
    void Flyout(FrameworkElement anchor, string title, UIElement body, PlacementMode place = PlacementMode.Top, double alignRightWidth = 0)
    {
        if (_openFlyout != null) _openFlyout.IsOpen = false;
        if (body is FrameworkElement fe && fe.Parent is Panel pp) pp.Children.Remove(body);
        if (body is FrameworkElement fe2 && fe2.Parent is Border pb) pb.Child = null;
        var panel = new StackPanel { Children = { new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) }, body } };
        var p = new Popup
        {
            PlacementTarget = anchor, Placement = place, StaysOpen = false, HorizontalOffset = alignRightWidth > 0 ? anchor.ActualWidth - alignRightWidth : 0, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade,
            Child = new Border { Background = R("Card"), BorderBrush = R("Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Margin = new Thickness(0, 4, 0, 4), Child = panel, MaxWidth = 540 }
        };
        p.Closed += (_, _) => { panel.Children.Remove(body); if (_openFlyout == p) _openFlyout = null; };
        _openFlyout = p; p.IsOpen = true;
    }

    UIElement Build()
    {
        var root = new DockPanel { Margin = new Thickness(16, 8, 16, 8), LastChildFill = true };

        // ---- top strip ----
        var nameBtn = new Button { Style = (Style)FindResource("FlatButton"), Content = _deviceName, HorizontalContentAlignment = HorizontalAlignment.Left, ToolTip = "Earbuds and connection", Height = 32 };
        AutomationName(nameBtn, "Earbuds and connection");
        nameBtn.Click += (_, _) => Flyout(nameBtn, "Earbuds", DevicePanel(), PlacementMode.Bottom);
        TextBlock BudText(string t) => new() { Text = t, FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _chipL.Child = BudText("L"); _chipR.Child = BudText("R");
        foreach (var c in new[] { _chipL, _chipR }) { c.Width = 24; c.Height = 24; c.CornerRadius = new CornerRadius(12); c.BorderThickness = new Thickness(1); c.Margin = new Thickness(4, 0, 0, 0); c.Visibility = Visibility.Collapsed; }
        _solo.Foreground = R("Accent");
        var offline = new Border
        {
            BorderBrush = R("AccentDim"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(8, 0, 4, 0), Height = 24,
            ToolTip = "Offline: BluntyBuds never uses the internet",
            Child = Row(Ico(IconPath(IcShield, R("Accent")), 14), new TextBlock { Text = "Offline", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = R("Accent"), Margin = new Thickness(4, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center })
        };
        offline.MouseLeftButtonUp += (_, _) => ShowAbout();
        var about = new Button { Style = (Style)FindResource("FlatButton"), Content = Ico(IconPath(IcInfo, R("Dim"))), Width = 32, Height = 32, ToolTip = "About and Privacy" };
        AutomationName(about, "About and Privacy");
        about.Click += (_, _) => ShowAbout();
        var quit = new Button { Style = (Style)FindResource("FlatButton"), Content = new TextBlock { Text = "Quit", FontSize = 12, Foreground = R("Dim") }, Height = 32, ToolTip = "Quit BluntyBuds (closing the window only hides it to the tray)" };
        quit.Click += (_, _) => { _quitting = true; Close(); };
        var top = new DockPanel { Height = 32 };
        foreach (var e in new UIElement[] { quit, about, offline, _chipR, _chipL, _solo }) { DockPanel.SetDock(e, Dock.Right); top.Children.Add(e); }
        DockPanel.SetDock(_dot, Dock.Left); top.Children.Add(_dot);
        top.Children.Add(nameBtn);
        var pills = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        pills.ColumnDefinitions.Add(new ColumnDefinition());
        pills.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        pills.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        pills.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        pills.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(_pillCase, 2); Grid.SetColumn(_pillR, 4);
        pills.Children.Add(_pillL); pills.Children.Add(_pillCase); pills.Children.Add(_pillR);
        var strip = Card(new StackPanel { Children = { top, pills } });
        DockPanel.SetDock(strip, Dock.Top); root.Children.Add(strip);
        _status.Foreground = R("Dim"); _status.Margin = new Thickness(4, 0, 4, 8);
        DockPanel.SetDock(_status, Dock.Top); root.Children.Add(_status);

        // ---- bottom row: icon buttons ----
        var tools = new UniformGrid { Rows = 1, Margin = new Thickness(0, 0, -8, 0) };
        foreach (var b in new[] { _btnGestures, _btnBalance, _btnLowLat, _btnPing }) tools.Children.Add(b);
        _controls.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _controls.RowDefinitions.Add(new RowDefinition());
        _controls.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(tools, 2); _controls.Children.Add(tools);
        root.Children.Add(_controls);

        // ---- noise: segmented control, Depth only where supported ----
        _game.Style = (Style)FindResource("Switch");
        _game.Checked += A(() => { RenderLowLat(true); return _suppress ? Task.CompletedTask : SendGame(true); });
        _game.Unchecked += A(() => { RenderLowLat(false); return _suppress ? Task.CompletedTask : SendGame(false); });
        var seg = new Border { Background = R("Ctl"), CornerRadius = new CornerRadius(8), Padding = new Thickness(2) };
        var segRow = new UniformGrid { Rows = 1 };
        foreach (var (m, t) in new[] { (AncMode.Anc, "Quiet"), (AncMode.Transparency, "Aware"), (AncMode.Off, "Off"), (AncMode.Custom, "Custom") })
        {
            var rb = new RadioButton { Content = t, GroupName = "anc", Style = (Style)FindResource("Segment"), Height = 28 };
            var mm = m;
            rb.Checked += A(() => _suppress ? Task.CompletedTask : SendAnc(mm));
            _anc[m] = rb; segRow.Children.Add(rb);
        }
        seg.Child = segRow;
        _ancLevel.ValueChanged += (_, _) => { _ancLevelLbl.Text = $"Depth {(int)_ancLevel.Value}"; if (!_suppress) { _levelDebounce.Stop(); _levelDebounce.Start(); } };
        _ancLevelLbl.Text = $"Depth {(int)_ancLevel.Value}";
        DockPanel.SetDock(_ancLevelLbl, Dock.Left); _ancLevelBox.Children.Add(_ancLevelLbl); _ancLevelBox.Children.Add(_ancLevel);
        _ancLevelBox.ToolTip = "Noise-cancelling depth, 1–10";
        var ancBody = new StackPanel { Children = { seg, _ancLevelBox, _ancOther } };
        _ancCard.Background = R("Card"); _ancCard.CornerRadius = new CornerRadius(12); _ancCard.Padding = new Thickness(12, 8, 12, 8); _ancCard.Margin = new Thickness(0, 0, 0, 8); _ancCard.Child = ancBody;
        _controls.Children.Add(_ancCard);

        // ---- Tune: EQ + Lock EQ + Sound Profile chips ----
        for (int i = 0; i < 10; i++)
        {
            int k = i;
            var col = new DockPanel { Margin = new Thickness(2, 0, 2, 0) };
            _eqLbl[i] = new TextBlock { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(_eqLbl[i], Dock.Top); col.Children.Add(_eqLbl[i]);
            var f = EqGrid.Freqs[i];
            var fl = Lbl(f >= 1000 ? $"{f / 1000.0:0.#}k" : $"{f}", 12); fl.HorizontalAlignment = HorizontalAlignment.Center; fl.Margin = new Thickness(0, 4, 0, 0);
            DockPanel.SetDock(fl, Dock.Bottom); col.Children.Add(fl);
            _eq[i] = new Slider { Orientation = Orientation.Vertical, Minimum = -12, Maximum = 12, TickFrequency = 0.5, IsSnapToTickEnabled = true, SmallChange = 0.5, LargeChange = 1, HorizontalAlignment = HorizontalAlignment.Center };
            _eq[i].ValueChanged += (_, _) => { EqLabel(k); if (!_suppressEq) OnUserEq(); };
            _eq[i].PreviewMouseLeftButtonDown += (_, e) => { if (_cfg.EqLocked) { e.Handled = true; ShowToast("EQ locked"); } };
            _eq[i].PreviewKeyDown += (_, e) => { if (_cfg.EqLocked) { e.Handled = true; ShowToast("EQ locked"); } };
            _eq[i].PreviewMouseWheel += (_, e) => { if (_cfg.EqLocked) e.Handled = true; };
            AutomationName(_eq[i], $"{fl.Text} Hz");
            col.Children.Add(_eq[i]);
            EqLabel(i);
            _eqGrid.Children.Add(col);
        }
        _lock.Style = (Style)FindResource("FlatButton");
        _lock.Click += (_, _) => { _cfg.EqLocked = !_cfg.EqLocked; if (!_demo) _cfg.Save(); RenderLock(); ShowToast(_cfg.EqLocked ? "EQ locked" : "EQ unlocked"); };
        var soundBtn = B("Sound", (_, _) => { }); soundBtn.Margin = new Thickness(0);
        soundBtn.Click += (_, _) => Flyout(soundBtn, "Sound", SoundPanel(), PlacementMode.Bottom, 394);
        var tuneHdr = new DockPanel { Height = 32 };
        DockPanel.SetDock(soundBtn, Dock.Right); tuneHdr.Children.Add(soundBtn);
        tuneHdr.Children.Add(Row(new TextBlock { Text = "Tune", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) }, _lock));
        var chipScroll = new ScrollViewer { Content = _chips, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Height = 32 };
        chipScroll.PreviewMouseWheel += (_, e) => { chipScroll.ScrollToHorizontalOffset(chipScroll.HorizontalOffset - e.Delta / 2.0); e.Handled = true; };
        var tuneBody = new DockPanel();
        DockPanel.SetDock(tuneHdr, Dock.Top); tuneBody.Children.Add(tuneHdr);
        var profLbl = Lbl("Sound Profiles · tap to apply · hold or right-click to save or rename", 12); profLbl.Margin = new Thickness(0, 4, 0, 4);
        var bottom = new StackPanel { Children = { profLbl, chipScroll } };
        DockPanel.SetDock(bottom, Dock.Bottom); tuneBody.Children.Add(bottom);
        _eqGrid.Margin = new Thickness(0, 4, 0, 0);
        tuneBody.Children.Add(_eqGrid);
        var tuneCard = Card(tuneBody);
        Grid.SetRow(tuneCard, 1); _controls.Children.Add(tuneCard);
        RefreshPresets(_cfg.LastPreset);
        RenderLock();

        // ---- snackbar ----
        _snack.Background = R("Ctl"); _snack.BorderBrush = R("Line"); _snack.BorderThickness = new Thickness(1);
        _snackUndo.Style = (Style)FindResource("FlatButton"); _snackUndo.Foreground = R("Accent"); _snackUndo.FontWeight = FontWeights.SemiBold; _snackUndo.Height = 32;
        _snackUndo.Click += (_, _) => { var u = _undo; HideSnack(); u?.Invoke(); };
        var sd = new DockPanel(); DockPanel.SetDock(_snackUndo, Dock.Right); sd.Children.Add(_snackUndo); sd.Children.Add(_snackText);
        _snack.Child = sd;

        // app options live in the Earbuds flyout
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
        _showAll.Checked += (_, _) => Render(); _showAll.Unchecked += (_, _) => Render();

        BuildTouchGrid();
        _balance.ValueChanged += (_, _) => { _balLbl.Text = $"{(int)_balance.Value:+0;-0;0}"; if (!_suppress) { _balDebounce.Stop(); _balDebounce.Start(); } };
        return root;
    }

    // ----- flyout panels (built once, re-hosted on open) -----
    StackPanel? _devicePanel, _gesturesPanel, _balancePanel, _lowLatPanel, _pingPanel, _soundPanel;
    readonly Grid _touchGrid = new();
    UIElement DevicePanel() => _devicePanel ??= new StackPanel
    {
        Width = 500,
        Children =
        {
            Row(_devices, new Border { Width = 8 }, B("Connect", A(() => ConnectSelected()), accent: true)),
            WrapRow(B("Disconnect", (_, _) => UserDisconnect()), B("Scan", (_, _) => StartScan()), B("Refresh paired", A(() => ListPaired())), _showAll),
            _fw,
            new Border { Height = 8 },
            WrapRow(_autoRe, _toTray, _closeTray, _startWin),
            Lbl("Ctrl+Alt+N cycles noise mode: Off → Quiet → Aware.", 12)
        }
    };
    void BuildTouchGrid()
    {
        var touch = _touchGrid;
        touch.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        touch.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        touch.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        touch.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        touch.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void Put(UIElement e, int r, int c) { Grid.SetRow(e, r); Grid.SetColumn(e, c); touch.Children.Add(e); }
        var lh = Lbl("Left", 12); lh.Margin = new Thickness(0, 0, 0, 4); Put(lh, 0, 1);
        var rh = Lbl("Right", 12); rh.Margin = new Thickness(0, 0, 0, 4); Put(rh, 0, 3);
        int row = 1;
        foreach (var g in Enum.GetValues<Gesture>())
        {
            touch.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
            var gl = Lbl(g + (g == Gesture.Long ? " press" : " tap"), 13, dim: false); gl.Margin = new Thickness(0, 0, 12, 0);
            Put(gl, row, 0);
            foreach (var isLeft in new[] { true, false })
            {
                var cb = new ComboBox { ItemsSource = TouchAction.All, Margin = new Thickness(0, 2, 0, 2), Height = 32 };
                _touch[(isLeft, g)] = cb; Put(cb, row, isLeft ? 1 : 3);
            }
            row++;
        }
        RenderTouch();
    }
    UIElement GesturesPanel() => _gesturesPanel ??= new StackPanel
    {
        Children = { _touchGrid, Row(B("Apply", A(async () => { await SendTouch(); ShowToast("Gestures sent"); }), accent: true), B("Defaults", (_, _) => { _map = TouchMap.Defaults(); RenderTouch(); })) }
    };
    UIElement BalancePanel() => _balancePanel ??= new StackPanel
    {
        Children = { Row(Lbl("L", 13), new Border { Width = 8 }, _balance, new Border { Width = 8 }, Lbl("R", 13), _balLbl), new Border { Height = 8 }, Row(B("Centre", (_, _) => _balance.Value = 0)) }
    };
    UIElement LowLatPanel() => _lowLatPanel ??= new StackPanel { Width = 280, Children = { _game, new Border { Height = 8 }, Lbl("Lower audio delay for games and video. May use a little more battery.", 12) } };
    UIElement PingPanel() => _pingPanel ??= new StackPanel
    {
        Width = 320,
        Children = { Lbl("Plays a loud chirp. Take the earbuds out of your ears first.", 12), new Border { Height = 8 },
            Row(B("Left", (_, _) => _tone.Play(true, false)), B("Right", (_, _) => _tone.Play(false, true)), B("Both", (_, _) => _tone.Play(true, true)), B("Stop", (_, _) => _tone.Stop())) }
    };
    UIElement SoundPanel() => _soundPanel ??= new StackPanel
    {
        Width = 360,
        Children =
        {
            Lbl("Tap a Sound Profile chip to apply it. Hold or right-click a chip to save or rename.", 12), new Border { Height = 8 },
            WrapRow(B("Import Correction", (_, _) => ImportAutoEq()), B("Save as new…", (_, _) => SavePreset()), B("Import profiles", (_, _) => ImportPresets()), B("Export profiles", (_, _) => ExportPresets()), _restoreEq),
            Lbl("Import Correction loads an AutoEQ ParametricEQ.txt or GraphicEQ.txt and fits it to the 10 bands, offline.", 12)
        }
    };

    void SetConnectedUi(bool on) { _controls.IsEnabled = on; _controls.Opacity = on ? 1 : 0.45; foreach (var p in new UIElement?[] { _gesturesPanel, _balancePanel, _lowLatPanel }) if (p != null) p.IsEnabled = on; _touchGrid.IsEnabled = on; _balance.IsEnabled = on; _game.IsEnabled = on; }

    // ----- strip / Solo Mode -----
    void RenderConnection(bool on)
    {
        _dot.Fill = on ? R("Accent") : R("Dim");
        _dot.ToolTip = on ? "Connected" : "Not connected";
        _deviceName.Text = on ? (_connectedName != "" ? _connectedName : _model.IsUnknown ? "Earbuds" : _model.ToString()) : (_cfg.LastName is { } n && !_demo ? $"{n} · click to connect" : "Click to connect");
        if (!on) { _case = null; }
        ShowBattery();
    }
    void ShowBattery()
    {
        bool on = _ble.Protocol.HasValue || _demo;
        var solo = on ? BatteryText.Solo(_bl, _br) : null;
        _pillL.Set(_bl, _cl, solo == "R"); _pillR.Set(_br, _cr, solo == "L");
        _pillCase.Set(_case, _caseCharging, false); _pillCase.Visibility = _case is null ? Visibility.Hidden : Visibility.Visible;
        void Chip(Border c, bool present)
        {
            c.Visibility = on && (_bl is not null || _br is not null) ? Visibility.Visible : Visibility.Collapsed;
            c.Background = present ? R("Accent") : Brushes.Transparent; c.BorderBrush = present ? R("Accent") : R("Line");
            if (c.Child is TextBlock t) t.Foreground = present ? R("AccentFg") : R("Dim");
            c.ToolTip = present ? "In use" : "Not in use";
        }
        Chip(_chipL, _bl is not null); Chip(_chipR, _br is not null);
        _solo.Visibility = solo is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateTray();
    }
    void RenderLowLat(bool on) => _lowLatIcon.Stroke = on ? R("Accent") : R("Fg");
    void RenderLock()
    {
        bool l = _cfg.EqLocked;
        _lock.Content = Ico(IconPath(l ? IcLock : IcUnlock, l ? R("Accent") : R("Dim")));
        _lock.ToolTip = l ? "EQ locked · click to unlock" : "Lock EQ";
        AutomationName(_lock, l ? "EQ locked" : "Lock EQ");
        _eqGrid.Opacity = l ? 0.4 : 1;
    }
    void ShowToast(string msg) => ShowSnack(msg, null);
    void ShowSnack(string msg, Action? undo)
    {
        _snackText.Text = msg; _undo = undo; _snackUndo.Visibility = undo is null ? Visibility.Collapsed : Visibility.Visible;
        _snack.Visibility = Visibility.Visible; _snackTimer.Stop(); _snackTimer.Interval = TimeSpan.FromSeconds(undo is null ? 1.5 : 3); _snackTimer.Start();
    }
    void HideSnack() { _snackTimer.Stop(); _snack.Visibility = Visibility.Collapsed; _undo = null; }

    void SetStartup(bool on)
    {
        try { Settings.StartWithWindows = on; _status.Text = on ? "BluntyBuds will start (in the tray) when you sign in." : "Start with Windows turned off."; }
        catch (Exception ex) { _status.Text = "Couldn't change startup setting: " + ex.Message; }
    }

    void UserDisconnect()
    {
        _userDisconnected = true; _retry.Stop();
        _ble.Close(); SetConnectedUi(false); _connectedAddr = null; _status.Text = "Disconnected"; RenderConnection(false);
    }

    void OnLinkLost()
    {
        SetConnectedUi(false); _connectedAddr = null; RenderConnection(false);
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
            _lowWarned = false; _bl = _br = _case = null; _cl = _cr = _caseCharging = false; _ancMode = null;
            ApplyModel();
            SetConnectedUi(true);
            RenderConnection(true);
            // Battery (and case) are read right away on connect, before anything else, and the strip refreshes as they arrive.
            await RefreshAll();
            ShowBattery();
        }
        finally { _connecting = false; }
    }

    async Task CycleAnc()
    {
        if (!_ble.Protocol.HasValue || !Models.For(_model).Anc) { _tray.Toast("BluntyBuds", "Connected earbuds have no noise control — hotkey ignored."); return; }
        var next = _ancMode switch { 0 => AncMode.Anc, 1 => AncMode.Transparency, _ => AncMode.Off };
        await SendAnc(next);
        _tray.Toast("Noise mode", next switch { AncMode.Anc => "Quiet", AncMode.Transparency => "Aware", _ => "Off" });
    }

    void UpdateTray() => _tray.Update(_model.IsUnknown ? "Earbuds" : _model.ToString(), _bl, _br, _ble.Protocol.HasValue, _ancMode, Models.For(_model), _game.IsChecked == true, _cl, _cr, _case, _caseCharging);

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
        _ancOther.Visibility = _ancOther.Text == "" ? Visibility.Collapsed : Visibility.Visible;
        _suppress = false;
        UpdateTray();
    }

    // ----- per-device EQ + import/export -----
    string? DevKey => _connectedAddr?.ToString("X12");
    void RememberDeviceEq(float[] g) { if (DevKey is { } k && !_demo) { _cfg.EqByDevice[k] = g; _cfg.Save(); _restoreEq.IsEnabled = true; } }
    async Task RestoreDeviceEq()
    {
        if (_cfg.EqLocked) { ShowToast("EQ locked"); return; }
        if (DevKey is not { } k || !_cfg.EqByDevice.TryGetValue(k, out var g) || g.Length != 10) { _status.Text = "No saved Tune for this device yet (it is saved each time one is sent)."; return; }
        SetEq(g); await SendEq(); _status.Text = "Re-applied your saved Tune for this device.";
    }
    void ImportPresets()
    {
        try
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "BluntyBuds Sound Profiles (*.json)|*.json|All files|*.*" };
            if (dlg.ShowDialog(this) != true) return;
            int n = _presets.Import(File.ReadAllText(dlg.FileName));
            RefreshPresets(_selected); _status.Text = $"Imported {n} Sound Profile(s).";
        }
        catch (Exception ex) { _status.Text = "Import failed: " + ex.Message; }
    }
    async void ImportAutoEq()
    {
        try
        {
            if (_cfg.EqLocked) { ShowToast("EQ locked"); return; }
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "AutoEQ ParametricEQ / GraphicEQ (*.txt)|*.txt|All files|*.*" };
            if (dlg.ShowDialog(this) != true) return;
            var prev = EqUi(); var prevSel = _selected;
            SetEq(AutoEq.FitText(File.ReadAllText(dlg.FileName))); _selected = null; RenderChips();
            if (_ble.Protocol.HasValue)
            {
                await SendEq();
                ShowSnack("Correction applied", () => { SetEq(prev); _selected = prevSel; RenderChips(); _ = SendEq(); });
            }
            else _status.Text = "Correction fitted to Tune. Connect to send it, or Save as new… to keep it.";
        }
        catch (Exception ex) { _status.Text = "Import Correction failed: " + ex.Message; }
    }
    void ShowAbout()
    {
        var v = typeof(MainWindow).Assembly.GetName().Version;
        var perms = new[] { "Bluetooth: find and connect to your earbuds", "Notifications: tray tooltips and low-battery toasts (local only)" };
        var sp = new StackPanel { Margin = new Thickness(16), Width = 440 };
        sp.Children.Add(new TextBlock { Text = $"BluntyBuds {v?.ToString(3)}", FontSize = 16, FontWeight = FontWeights.SemiBold });
        sp.Children.Add(Lbl("Unofficial companion", 13));
        sp.Children.Add(new TextBlock { Text = "No internet. No account. No tracking.", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = R("Accent"), Margin = new Thickness(0, 12, 0, 12) });
        sp.Children.Add(new TextBlock { Text = "Permissions granted", FontSize = 13, FontWeight = FontWeights.SemiBold });
        foreach (var p in perms) sp.Children.Add(Lbl("• " + p, 13, dim: false));
        sp.Children.Add(Lbl("Settings and Sound Profiles stay on this PC. MIT License.", 12));
        ((FrameworkElement)sp.Children[^1]).Margin = new Thickness(0, 12, 0, 12);
        sp.Children.Add(Lbl(AutoEq.Attribution + "\n\nAutoEq is MIT licensed: permission is granted, free of charge, to use, copy, modify and distribute it, provided the copyright and permission notice are included. It is provided \"as is\", without warranty of any kind.", 12));
        var ok = B("OK", (_, _) => { }, accent: true); ok.HorizontalAlignment = HorizontalAlignment.Right; ok.Margin = new Thickness(0, 12, 0, 0); ok.IsDefault = true; ok.IsCancel = true;
        sp.Children.Add(ok);
        var w = new Window { Title = "About and Privacy", SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, Owner = IsVisible ? this : null, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = R("Bg"), Foreground = R("Fg"), Content = sp };
        ok.Click += (_, _) => w.Close();
        w.SourceInitialized += (_, _) => Theme.DarkTitleBar(w);
        w.ShowDialog();
    }
    // AutoEQ profiles are listed with the Sound Profiles; the connected model's own profile comes first.
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
            var dlg = new Microsoft.Win32.SaveFileDialog { Filter = "BluntyBuds Sound Profiles (*.json)|*.json", FileName = "bluntybuds-profiles.json" };
            if (dlg.ShowDialog(this) != true) return;
            File.WriteAllText(dlg.FileName, _presets.Export());
            _status.Text = $"Exported {_presets.UserPresets().Count} Sound Profile(s).";
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
        try { _watcherObj?.Stop(); } catch { }
        _retry.Stop();
        if (d.Address is { } a)
        {
            var name = d.Label.Split("  [")[0];
            await ConnectTo(a, d.Hint, d.Model, name);
        }
        else if (await _ble.ConnectByIdAsync(d.Id!, d.Hint)) { _model = d.Model; ApplyModel(); SetConnectedUi(true); RenderConnection(true); await RefreshAll(); }
    }

    string UntestedTag => _ble.Protocol.HasValue && !_model.IsUnknown && !_model.Tested ? " · untested model" : "";

    void ApplyModel()
    {
        var f = Models.For(_model);
        _deviceName.ToolTip = (_model.IsUnknown ? "Model not recognised, showing generic features" : _model.Tested ? _model.ToString() : $"{_model}: untested model, features come from the built-in model table")
            + $" · protocol {_ble.Protocol}";
        if (UntestedTag != "" && !_status.Text.EndsWith(UntestedTag)) _status.Text += UntestedTag;
        // Hide what the device does not support.
        bool a = _ble.Protocol == Protocol.A || _demo;
        _ancLevelBox.Visibility = f.AncLevel && a ? Visibility.Visible : Visibility.Collapsed;
        _anc[AncMode.Custom].Visibility = _ancLevelBox.Visibility;
        _restoreEq.IsEnabled = DevKey is { } k && _cfg.EqByDevice.ContainsKey(k);
        _ancCard.Visibility = f.Anc && a ? Visibility.Visible : Visibility.Collapsed;
        _btnBalance.Visibility = f.Balance && a ? Visibility.Visible : Visibility.Collapsed;
        _btnLowLat.Visibility = f.Game ? Visibility.Visible : Visibility.Collapsed;
        _btnGestures.Visibility = f.Touch ? Visibility.Visible : Visibility.Collapsed;
        // Same as Android: the last-used profile wins; otherwise a confident AutoEQ match.
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
        var item = new DevItem(0x001122334455, null, Protocol.A, Models.Detect("NC9"), "NC9 (demo) · 00:11:22:33:44:55");
        _devItems.Add(item); _devices.SelectedItem = item;
        _model = item.Model; _connectedName = "NC9 (demo)";
        ApplyModel(); SetConnectedUi(true);
        _cfg.EqLocked = false; RenderLock();
        var (ae, sure) = MyAutoEqMatch;
        if (ae is not null)
        {
            RefreshPresets(AutoEq.SuggestLabel(ae, sure));
            SetEq(ae.Gains);
        }
        foreach (var e in new Event[] { new Event.Battery(82, null, Snapshot: true), new Event.CaseBattery(64), new Event.Firmware("1.0.0", "1.0.0"), new Event.Anc((int)AncMode.Anc), new Event.AncLevel(6),
                     new Event.Game(false), new Event.Balance(0), new Event.Touch(TouchMap.Defaults()) })
            OnEvent(e);
        _dot.Fill = R("Accent"); _deviceName.Text = _connectedName;
        ShowBattery();
        _status.Text = "Demo mode: made-up battery, Bluetooth not used";
    }

    async Task RefreshAll()
    {
        if (_ble.Protocol == Protocol.A)
            foreach (var c in ProtoA.StatusQueries(Models.For(_model).Anc, Models.For(_model).AncLevel))
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
    void SetEq(float[] g) { _suppressEq = true; try { for (int i = 0; i < 10; i++) _eq[i].Value = Math.Clamp(g[i], -12, 12); } finally { _suppressEq = false; } }
    void EqLabel(int i) { _eqLbl[i].Text = $"{_eq[i].Value:+0.#;-0.#;0}"; }
    /// <summary>No Apply step: a slider change is sent shortly after you stop moving it.</summary>
    void OnUserEq()
    {
        if (!_ble.Protocol.HasValue) return;
        _eqDebounce.Stop(); _eqDebounce.Start();
        if (_selected is { } s && !(PresetMap().TryGetValue(s, out var g) && g.SequenceEqual(EqUi()))) { _selected = null; RenderChips(); }
    }
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

    // ----- Sound Profiles (chips) -----
    void RefreshPresets(string? select = null)
    {
        var names = PresetMap().Keys.ToList();
        _selected = select is not null && names.Contains(select) ? select : null;
        RenderChips();
    }
    void RenderChips()
    {
        _chips.Children.Clear();
        foreach (var n in PresetMap().Keys)
        {
            var c = new Button { Content = n, Tag = n, Style = (Style)FindResource(n == _selected ? "ChipOn" : "Chip") };
            var nn = n;
            c.Click += (_, _) => { if (_holdFired) { _holdFired = false; return; } ApplyProfile(nn); };
            c.PreviewMouseLeftButtonDown += (_, _) => { _holdName = nn; _holdFired = false; _hold.Stop(); _hold.Start(); };
            c.PreviewMouseLeftButtonUp += (_, _) => _hold.Stop();
            c.MouseLeave += (_, _) => _hold.Stop();
            c.MouseRightButtonUp += (_, e) => { e.Handled = true; ProfileMenu(c, nn); };
            _chips.Children.Add(c);
        }
        var add = new Button { Content = "+ Save", Style = (Style)FindResource("Chip"), Foreground = R("Accent"), ToolTip = "Save the current Tune as a Sound Profile" };
        add.Click += (_, _) => SavePreset();
        _chips.Children.Add(add);
    }
    /// <summary>Tap a chip: applied to the earbuds immediately, with a 3-second Undo.</summary>
    async void ApplyProfile(string n)
    {
        try
        {
            if (_cfg.EqLocked) { ShowToast("EQ locked"); return; }
            if (!PresetMap().TryGetValue(n, out var g)) return;
            var prev = EqUi(); var prevSel = _selected;
            SetEq(g); _selected = n; RenderChips();
            if (!_demo) { _cfg.LastPreset = n; _cfg.Save(); }
            if (_ble.Protocol.HasValue) await SendEq();
            ShowSnack($"“{n}” applied", () =>
            {
                SetEq(prev); _selected = prevSel; RenderChips();
                if (!_demo && prevSel is not null) { _cfg.LastPreset = prevSel; _cfg.Save(); }
                if (_ble.Protocol.HasValue) _ = SendEq();
            });
        }
        catch (Exception ex) { Program.Log(ex, "apply profile"); _status.Text = "Couldn't apply: " + ex.Message; }
    }
    /// <summary>Hold or right-click a chip: save the current Tune into it, rename or delete your own profiles.</summary>
    void ProfileMenu(FrameworkElement anchor, string n)
    {
        var sp = new StackPanel();
        if (IsFixed(n))
        {
            sp.Children.Add(Lbl("Built-in profile", 12));
            sp.Children.Add(new Border { Height = 8 });
            sp.Children.Add(B("Save current Tune as new…", (_, _) => { CloseFlyout(); SavePreset(); }));
        }
        else
        {
            sp.Children.Add(WrapRow(
                B("Save current Tune here", (_, _) => { CloseFlyout(); _presets.Save(n, EqUi()); RefreshPresets(n); ShowToast($"Saved “{n}”"); }),
                B("Rename…", (_, _) => { CloseFlyout(); if (AskName("Rename Sound Profile", n) is { } nn) { var g = _presets.All()[n]; _presets.Save(nn, g); _presets.Delete(n); if (_cfg.LastPreset == n) { _cfg.LastPreset = nn; _cfg.Save(); } RefreshPresets(_selected == n ? nn : _selected); } }),
                B("Delete", (_, _) => { CloseFlyout(); _presets.Delete(n); RefreshPresets(_selected == n ? null : _selected); })));
        }
        Flyout(anchor, n, sp);
    }
    void CloseFlyout() { if (_openFlyout != null) _openFlyout.IsOpen = false; }
    string? AskName(string title, string initial)
    {
        var tb = new TextBox { Width = 240, Text = initial, Height = 32, VerticalContentAlignment = VerticalAlignment.Center };
        var ok = new Button { Content = "Save", IsDefault = true, Margin = new Thickness(8, 0, 0, 0), Height = 32, Style = (Style)FindResource("AccentButton") };
        var dlg = new Window { Title = title, SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, Owner = this, Background = R("Bg"), Foreground = R("Fg"),
            Content = new Border { Padding = new Thickness(16), Child = Row(tb, ok) }, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        dlg.SourceInitialized += (_, _) => Theme.DarkTitleBar(dlg);
        ok.Click += (_, _) => dlg.DialogResult = true;
        dlg.Loaded += (_, _) => { tb.Focus(); tb.SelectAll(); };
        if (dlg.ShowDialog() != true) return null;
        var n = tb.Text.Trim();
        if (n.Length == 0 || IsFixed(n) || (n != initial && PresetMap().ContainsKey(n))) { _status.Text = "Pick a new profile name."; return null; }
        return n == initial ? null : n;
    }
    void SavePreset()
    {
        if (AskName("Save Sound Profile", "") is not { } n) return;
        _presets.Save(n, EqUi()); if (!_demo) { _cfg.LastPreset = n; _cfg.Save(); } RefreshPresets(n);
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
                // Low battery: only the earbud(s) in use; a missing bud is never reported as low or disconnected.
                var low = BatteryText.Lowest(_bl, _br);
                if (low is int p && p <= _cfg.LowBatteryPercent && !_lowWarned && !_demo) { _lowWarned = true; _tray.Toast("BluntyBuds · low battery", BatteryText.Format(_bl, _br, _cl, _cr), warn: true); }
                else if (low is null || low > _cfg.LowBatteryPercent + 5) _lowWarned = false;
                break;
            case Event.CaseBattery cb: _case = cb.Level; _caseCharging = cb.Charging; ShowBattery(); break;
            case Event.Firmware f: _fw.Text = $"FW {f.Left}" + (f.Right is null || f.Right == f.Left ? "" : $" / {f.Right}"); _fw.Foreground = R("Dim"); break;
            case Event.Game g: _suppress = true; _game.IsChecked = g.On; _suppress = false; RenderLowLat(g.On); UpdateTray(); break;
            case Event.Touch t:
                for (int i = 0; i < 5; i++) { if (t.Map.Left[i] >= 0) _map.Left[i] = t.Map.Left[i]; if (t.Map.Right[i] >= 0) _map.Right[i] = t.Map.Right[i]; }
                RenderTouch(); break;
            case Event.Eq q:
                if (q.Gains.Length == 10)
                {
                    SetEq(q.Gains);
                    if (!(_selected is { } s && PresetMap().TryGetValue(s, out var sg) && sg.SequenceEqual(EqUi())))
                    { _selected = PresetMap().FirstOrDefault(kv => kv.Value.SequenceEqual(EqUi())).Key; RenderChips(); }
                }
                break;
            case Event.Anc an: _ancMode = an.Mode; ShowAnc(); break;
            case Event.AncLevel lv: _suppress = true; _ancLevel.Value = lv.Level; _suppress = false; break;
            case Event.Balance bal: _suppress = true; _balance.Value = Math.Clamp(bal.Value, -11, 11); _suppress = false; break;
            case Event.Ack a: if (!a.Ok) _status.Text = $"Earbuds rejected the change (0x{a.Cmd:X4})"; break;
        }
    }
}
