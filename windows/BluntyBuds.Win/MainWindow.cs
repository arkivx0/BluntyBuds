using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Documents;
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
/// Home: one window, no scrolling. Top strip (device, connection dot, L / Case / R battery pills, privacy badge),
/// noise control, a short Tune (10-band EQ with Lock EQ) and Sound Profiles, then L-R Balance and Low-Latency.
/// Gestures and Ping Buds stay as icon buttons that open compact flyouts.
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
            w.Stopped += (_, _) => Ui(() =>
            {
                if (_ble.Protocol.HasValue || _connecting) return;
                var any = _seen.Count > 0 || _paired.Any(p => IsKnownEarbud(p.Value.Name));
                SetStatus(any ? "Scan finished. Select a device and Connect." : NoBuds);
            });
            return _watcherObj = w;
        }
    }
    readonly Dictionary<ulong, (string Name, short Rssi, AdParser.VendorAd? Ad)> _seen = new();

    const string NoBuds = "No earbuds found. Put them in pairing mode, then Scan.";
    readonly TextBlock _status = new() { Text = NoBuds, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 36, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, ToolTip = NoBuds };
    readonly ComboBox _devices = new() { Width = 300, MaxDropDownHeight = 260 };
    readonly CheckBox _showAll = new() { Content = "All", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    readonly Grid _controls = new() { IsEnabled = false, Opacity = 0.45 };
    readonly TextBlock _deviceName = new() { Text = "Not connected", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    Button _nameBtn = null!;
    readonly Ellipse _dot = new() { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
    readonly Border _chipL = new(), _chipR = new();
    readonly Pill _pillL = new("L", false), _pillR = new("R", false), _pillCase = new("Case", true);
    readonly TextBlock _fw = new() { Text = "FW —", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    readonly CheckBox _game = new() { Content = "Low-Latency", VerticalAlignment = VerticalAlignment.Center };
    readonly Slider[] _eq = new Slider[10];
    readonly TextBlock[] _eqLbl = new TextBlock[10];
    readonly UniformGrid _eqGrid = new() { Columns = 10 };
    /// <summary>Sound Profiles dropdown: applies on selection (no Apply step), with Undo.</summary>
    readonly ComboBox _profileBox = new() { Height = 32, MaxDropDownHeight = 320, ToolTip = "Sound Profile: applied as soon as you pick it" };
    const string CustomItem = "Custom (unsaved)";
    bool _suppressProfile, _suppressTouch;
    readonly Button _lock = new() { Width = 32, Height = 32, Padding = new Thickness(0) };
    string? _selected;
    readonly Dictionary<(bool Left, Gesture G), ComboBox> _touch = new();
    readonly Slider _balance = new() { Minimum = -11, Maximum = 11, TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch };
    readonly TextBlock _balLbl = new() { Text = "0", Width = 28, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
    readonly Border _ancCard = new();
    readonly Dictionary<AncMode, RadioButton> _anc = new();
    readonly TextBlock _ancOther = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), FontSize = 12 };
    readonly DockPanel _ancLevelBox = new() { Margin = new Thickness(0, 8, 0, 0), Height = 32 };
    readonly Slider _ancLevel = new() { Minimum = ProtoA.AncLevelMin, Maximum = ProtoA.AncLevelMax, TickFrequency = 1, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock _ancLevelLbl = new() { Width = 72, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
    readonly Button _restoreEq;
    readonly Button _btnGestures, _btnPing;
    readonly TextBlock _lockHint = new() { Text = "Locked, tap to edit", FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0), Visibility = Visibility.Collapsed };
    Border _tuneCard = null!, _listenCard = null!;
    Grid _tuneSlot = null!;
    UIElement _balanceRow = null!, _lowLatRow = null!;
    readonly Path _lowLatIcon;
    readonly Border _snack = new() { Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 28), Padding = new Thickness(16, 4, 8, 4), CornerRadius = new CornerRadius(8), MinWidth = 280 };
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
    readonly Dictionary<ulong, (string Name, string Kind, bool Connected)> _paired = new();
    DeviceModel _model = DeviceModel.Unknown;
    ulong? _connectedAddr;
    string _connectedName = "";
    int? _ancMode;
    bool _connecting, _userDisconnected, _lowWarned, _quitting, _closeHintThisSession;
    TouchMap _map = TouchMap.Defaults();
    /// <summary>Gestures the connected earbuds reported; all four until they report.</summary>
    IReadOnlyList<Gesture> _gestures = Enum.GetValues<Gesture>();
    int? _bl, _br, _case;
    bool _cl, _cr, _caseCharging;
    int _batGen;
    bool _sawL, _sawR;
    bool _suppress, _suppressEq;

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
    const string IcBolt = "M13,2.5 L5,13.5 H11.5 L10.5,21.5 L19,10 H12.5 Z";
    const string IcPing = "M14.5,12 A2.5,2.5 0 1 1 9.5,12 A2.5,2.5 0 1 1 14.5,12 M16.8,9.2 A5.5,5.5 0 0 1 16.8,14.8 M7.2,9.2 A5.5,5.5 0 0 0 7.2,14.8 M20.2,7.3 A9.5,9.5 0 0 1 20.2,16.7 M3.8,7.3 A9.5,9.5 0 0 0 3.8,16.7";
    const string IcInfo = "M21,12 A9,9 0 1 1 3,12 A9,9 0 1 1 21,12 M12,11 V16.5 M12,7.6 V7.8";
    static Path IconPath(string data, Brush b) => new() { Data = Geometry.Parse(data), Stroke = b, StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    static Viewbox Ico(Path p, double size = 20) => new() { Width = size, Height = size, Child = new Canvas { Width = 24, Height = 24, Children = { p } } };

    /// <summary>Battery pill: number plus a bar. A missing earbud reads "In case"; with no link, "Not connected". Never shown as 0%.</summary>
    sealed class Pill : Border
    {
        readonly string _side;
        readonly TextBlock _t = new() { HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
        readonly Viewbox _bolt;
        readonly ColumnDefinition _a = new(), _b = new(); readonly Grid _bar = new() { Height = 4, Margin = new Thickness(0, 4, 0, 0) };
        public Pill(string side, bool small)
        {
            _side = side; _t.FontSize = small ? 12 : 14; if (!small) _t.FontWeight = FontWeights.SemiBold;
            CornerRadius = new CornerRadius(10); Padding = new Thickness(8, 4, 8, 6); BorderThickness = new Thickness(1);
            _bolt = Ico(IconPath(IcBolt, R("Accent")), 12);
            _bolt.Margin = new Thickness(4, 0, 0, 0); _bolt.Visibility = Visibility.Collapsed; _bolt.VerticalAlignment = VerticalAlignment.Center;
            _bar.ColumnDefinitions.Add(_a); _bar.ColumnDefinitions.Add(_b);
            var fill = new Border { Background = R("Accent"), CornerRadius = new CornerRadius(2) };
            var rest = new Border { Background = R("Line"), CornerRadius = new CornerRadius(2) };
            Grid.SetColumn(rest, 1); _bar.Children.Add(fill); _bar.Children.Add(rest);
            var label = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            label.Children.Add(_t); label.Children.Add(_bolt);
            Child = new StackPanel { Children = { label, _bar } };
            Set(null, false, false, false);
        }
        string Spoken => _side switch { "L" => "Left earbud", "R" => "Right earbud", "Case" => "Case", _ => _side };
        public void Set(int? pct, bool charging, bool missing, bool connected)
        {
            _bolt.Visibility = pct is not null && charging ? Visibility.Visible : Visibility.Collapsed;
            if (pct is int p)
            {
                _t.Text = $"{_side} {p}%"; _t.Foreground = R("Fg");
                _a.Width = new GridLength(Math.Max(p, 0.01), GridUnitType.Star); _b.Width = new GridLength(Math.Max(100 - p, 0.01), GridUnitType.Star);
                _bar.Visibility = Visibility.Visible; Background = R("Ctl"); BorderBrush = R("Ctl"); Opacity = 1;
                var spoken = $"{Spoken} {p} percent" + (charging ? ", charging" : "");
                ToolTip = spoken; AutomationName(this, spoken);
            }
            else if (missing)
            {
                _t.Text = "In case"; _t.Foreground = R("Dim");
                _bar.Visibility = Visibility.Hidden; Background = Brushes.Transparent; BorderBrush = R("Line"); Opacity = 0.55;
                var spoken = $"{Spoken}, in case";
                ToolTip = spoken; AutomationName(this, spoken);
            }
            else if (!connected && _side != "Case")
            {
                _t.Text = "Not connected"; _t.Foreground = R("Dim");
                _bar.Visibility = Visibility.Hidden; Background = Brushes.Transparent; BorderBrush = R("Line"); Opacity = 0.8;
                var spoken = $"{Spoken}, not connected";
                ToolTip = spoken; AutomationName(this, spoken);
            }
            else
            {
                _t.Text = $"{_side} —"; _t.Foreground = R("Dim");
                _bar.Visibility = Visibility.Hidden; Background = Brushes.Transparent; BorderBrush = R("Line"); Opacity = 0.8;
                var spoken = $"{Spoken}, battery unknown";
                ToolTip = spoken; AutomationName(this, spoken);
            }
        }
    }

    public MainWindow(bool startHidden = false, bool demo = false)
    {
        _startHidden = startHidden; _demo = demo;
        Title = "BluntyBuds";
        Width = 600; Height = 640; MinWidth = 560; MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        // implicit Window style doesn't apply to subclasses, so set the theme brushes directly
        try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/bluntybuds.ico")); } catch (Exception ex) { Program.Log(ex, "window icon"); }
        Background = R("Bg"); Foreground = R("Fg"); FontSize = 13;
        _restoreEq = B("My EQ", A(RestoreDeviceEq));
        _restoreEq.ToolTip = "Re-apply the Tune you last sent to this device";
        _lowLatIcon = IconPath(IcBolt, R("Fg"));
        _btnGestures = Tool(IconPath(IcGesture, R("Fg")), "Gestures", b => Flyout(b, "Gestures", GesturesPanel()));
        _btnPing = Tool(IconPath(IcPing, R("Fg")), "Ping Buds", b => Flyout(b, "Ping Buds", PingPanel()));
        DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock)).AddValueChanged(_status, (_, _) => _status.ToolTip = _status.Text);
        var host = new Grid();
        host.Children.Add(Build());
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

    void SetStatus(string s) { _status.Text = s; _status.ToolTip = s; }
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
        var nameBtn = new Button { Style = (Style)FindResource("FlatButton"), Content = _deviceName, HorizontalContentAlignment = HorizontalAlignment.Left, ToolTip = _deviceName.Text, Height = 40, HorizontalAlignment = HorizontalAlignment.Stretch };
        _nameBtn = nameBtn;
        AutomationName(nameBtn, "Earbuds and connection");
        nameBtn.Click += (_, _) => Flyout(nameBtn, "Earbuds", DevicePanel(), PlacementMode.Bottom);
        TextBlock BudText(string t) => new() { Text = t, FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _chipL.Child = BudText("L"); _chipR.Child = BudText("R");
        foreach (var c in new[] { _chipL, _chipR }) { c.Width = 40; c.Height = 40; c.CornerRadius = new CornerRadius(20); c.BorderThickness = new Thickness(1); c.Margin = new Thickness(4, 0, 0, 0); c.Visibility = Visibility.Collapsed; c.VerticalAlignment = VerticalAlignment.Center; }
        const string privacyTip = "No internet. Bluetooth only, no network, no analytics.";
        var privacy = new Button
        {
            Style = (Style)FindResource("FlatButton"), Width = 40, Height = 40, Margin = new Thickness(8, 0, 0, 0),
            ToolTip = privacyTip, Content = Ico(IconPath(IcShield, R("Accent")), 18)
        };
        AutomationName(privacy, privacyTip);
        privacy.Click += (_, _) => Flyout(privacy, "No internet", new TextBlock { Text = "Bluetooth only, no network, no analytics.", TextWrapping = TextWrapping.Wrap, Width = 260, FontSize = 13 });
        var about = new Button { Style = (Style)FindResource("FlatButton"), Content = Ico(IconPath(IcInfo, R("Dim"))), Width = 40, Height = 40, ToolTip = "About and Privacy" };
        AutomationName(about, "About and Privacy");
        about.Click += (_, _) => ShowAbout();
        var quit = new Button { Style = (Style)FindResource("FlatButton"), Content = new TextBlock { Text = "Quit", FontSize = 12, Foreground = R("Dim") }, Height = 40, ToolTip = "Quit BluntyBuds (closing the window only hides it to the tray)" };
        quit.Click += (_, _) => { _quitting = true; Close(); };
        var top = new DockPanel { MinHeight = 40 };
        foreach (var e in new UIElement[] { quit, about, privacy, _chipR, _chipL }) { DockPanel.SetDock(e, Dock.Right); top.Children.Add(e); }
        DockPanel.SetDock(_dot, Dock.Left); top.Children.Add(_dot);
        top.Children.Add(nameBtn);
        var pills = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        pills.ColumnDefinitions.Add(new ColumnDefinition());
        pills.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        pills.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 96 });
        pills.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        pills.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(_pillCase, 2); Grid.SetColumn(_pillR, 4);
        _pillCase.HorizontalAlignment = HorizontalAlignment.Center;
        pills.Children.Add(_pillL); pills.Children.Add(_pillCase); pills.Children.Add(_pillR);
        var strip = Card(new StackPanel { Children = { top, pills } });
        DockPanel.SetDock(strip, Dock.Top); root.Children.Add(strip);
        _status.Foreground = R("Dim"); _status.Margin = new Thickness(4, 0, 4, 8);
        DockPanel.SetDock(_status, Dock.Top); root.Children.Add(_status);

        // ---- bottom row: Gestures and Ping Buds. Balance and Low-Latency are on the main screen. ----
        var tools = new UniformGrid { Rows = 1, Columns = 2, Margin = new Thickness(0, 0, -8, 0) };
        foreach (var b in new[] { _btnGestures, _btnPing }) tools.Children.Add(b);
        _controls.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _controls.RowDefinitions.Add(new RowDefinition());
        _controls.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _controls.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(tools, 3); _controls.Children.Add(tools);
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
        _ancLevel.ValueChanged += (_, _) => { _ancLevelLbl.Text = $"Depth {(int)_ancLevel.Value}"; AutomationName(_ancLevel, $"Noise-cancelling depth {(int)_ancLevel.Value}"); if (!_suppress) { _levelDebounce.Stop(); _levelDebounce.Start(); } };
        AutomationName(_ancLevel, "Noise-cancelling depth 1");
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
            Typography.SetNumeralAlignment(_eqLbl[i], FontNumeralAlignment.Tabular);
            DockPanel.SetDock(_eqLbl[i], Dock.Top); col.Children.Add(_eqLbl[i]);
            var f = EqGrid.Freqs[i];
            var fl = Lbl(f >= 1000 ? $"{f / 1000.0:0.#}k" : $"{f}", 12); fl.HorizontalAlignment = HorizontalAlignment.Center; fl.Margin = new Thickness(0, 4, 0, 0);
            DockPanel.SetDock(fl, Dock.Bottom); col.Children.Add(fl);
            _eq[i] = new Slider { Orientation = Orientation.Vertical, Minimum = -12, Maximum = 12, TickFrequency = 0.5, IsSnapToTickEnabled = true, SmallChange = 0.5, LargeChange = 1, HorizontalAlignment = HorizontalAlignment.Center };
            _eq[i].ValueChanged += (_, _) => { EqLabel(k); if (!_suppressEq) OnUserEq(); };
            _eq[i].PreviewMouseLeftButtonDown += (_, e) => { if (_cfg.EqLocked) { e.Handled = true; ShowToast("EQ locked: tap the padlock to unlock"); } };
            _eq[i].PreviewKeyDown += (_, e) => { if (_cfg.EqLocked) { e.Handled = true; ShowToast("EQ locked: tap the padlock to unlock"); } };
            _eq[i].PreviewMouseWheel += (_, e) => { if (_cfg.EqLocked) e.Handled = true; };
            AutomationName(_eq[i], BandName(f, 0));
            col.Children.Add(_eq[i]);
            EqLabel(i);
            _eqGrid.Children.Add(col);
        }
        _lock.Style = (Style)FindResource("FlatButton");
        _lock.Click += (_, _) => { _cfg.EqLocked = !_cfg.EqLocked; if (!_demo) _cfg.Save(); RenderLock(); ShowToast(_cfg.EqLocked ? "EQ locked" : "EQ unlocked"); };
        var soundBtn = B("Sound", (_, _) => { }); soundBtn.Margin = new Thickness(0);
        soundBtn.Click += (_, _) => Flyout(soundBtn, "Sound", SoundPanel(), PlacementMode.Bottom, 394);
        var tuneHdr = new DockPanel { MinHeight = 32 };
        DockPanel.SetDock(soundBtn, Dock.Right); tuneHdr.Children.Add(soundBtn);
        tuneHdr.Children.Add(Row(new TextBlock { Text = "Tune", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) }, _lock, _lockHint));
        _profileBox.SelectionChanged += (_, _) => { if (!_suppressProfile && _profileBox.SelectedItem is string n && n != CustomItem) ApplyProfile(n); };
        var profMore = B("Edit", (_, _) => { }); profMore.Margin = new Thickness(8, 0, 0, 0); profMore.ToolTip = "Save, rename or delete Sound Profiles";
        profMore.Click += (_, _) => ProfileMenu(profMore, _selected);
        var profRow = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(profMore, Dock.Right); profRow.Children.Add(profMore); profRow.Children.Add(_profileBox);
        var tuneBody = new DockPanel();
        DockPanel.SetDock(tuneHdr, Dock.Top); tuneBody.Children.Add(tuneHdr);
        var bottom = new StackPanel { Children = { profRow } };
        DockPanel.SetDock(bottom, Dock.Bottom); tuneBody.Children.Add(bottom);
        _eqGrid.Margin = new Thickness(0, 4, 0, 0); _eqGrid.Height = 160; _eqGrid.MaxHeight = 160;
        // The Undo snackbar floats over the lower part of the sliders, never over the dropdown or the bottom row.
        tuneBody.Children.Add(new Grid { Children = { _eqGrid, _snack } });
        _tuneCard = Card(tuneBody); _tuneCard.VerticalAlignment = VerticalAlignment.Top;
        _tuneSlot = new Grid();
        _tuneSlot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _tuneSlot.Children.Add(_tuneCard);
        _tuneSlot.SizeChanged += (_, _) => FitEq();
        Grid.SetRow(_tuneSlot, 1); _controls.Children.Add(_tuneSlot);
        var centre = B("Centre", (_, _) => _balance.Value = 0); centre.Margin = new Thickness(8, 0, 0, 0);
        _balanceRow = new Grid { MinHeight = 32 };
        var balGrid = (Grid)_balanceRow;
        balGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        balGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        balGrid.ColumnDefinitions.Add(new ColumnDefinition());
        balGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        balGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        balGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        balGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var balL = Lbl("L", 13, dim: false); var balR = Lbl("R", 13, dim: false);
        Typography.SetNumeralAlignment(_balLbl, FontNumeralAlignment.Tabular);
        Grid.SetColumn(_balance, 2); Grid.SetColumn(balR, 4); Grid.SetColumn(_balLbl, 5); Grid.SetColumn(centre, 6);
        balGrid.Children.Add(balL); balGrid.Children.Add(_balance); balGrid.Children.Add(balR); balGrid.Children.Add(_balLbl); balGrid.Children.Add(centre);
        AutomationName(_balance, "Left-right balance 0");
        var lowBolt = Ico(_lowLatIcon, 16); lowBolt.Margin = new Thickness(0, 0, 8, 0); lowBolt.VerticalAlignment = VerticalAlignment.Center;
        _lowLatRow = new DockPanel { MinHeight = 32, Margin = new Thickness(0, 8, 0, 0), ToolTip = "Lower audio delay for games and video. May use a little more battery." };
        DockPanel.SetDock(lowBolt, Dock.Left); ((DockPanel)_lowLatRow).Children.Add(lowBolt); ((DockPanel)_lowLatRow).Children.Add(_game);
        var listen = new StackPanel(); listen.Children.Add(_balanceRow); listen.Children.Add(_lowLatRow);
        _listenCard = Card(listen);
        Grid.SetRow(_listenCard, 2); _controls.Children.Add(_listenCard);
        RefreshPresets(CanonicalPreset(_cfg.LastPreset));
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
        _balance.ValueChanged += (_, _) => { var n = (int)_balance.Value; _balLbl.Text = n.ToString("+0;-0;0", CultureInfo.InvariantCulture); AutomationName(_balance, $"Left-right balance {n.ToString("+0;-0;0", CultureInfo.InvariantCulture)}"); if (!_suppress) { _balDebounce.Stop(); _balDebounce.Start(); } };
        SizeChanged += (_, _) => FitEq();
        return root;
    }

    // ----- flyout panels (built once, re-hosted on open) -----
    StackPanel? _devicePanel, _gesturesPanel, _pingPanel, _soundPanel;
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
        touch.Children.Clear(); touch.RowDefinitions.Clear(); touch.ColumnDefinitions.Clear(); _touch.Clear();
        touch.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        touch.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        touch.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        touch.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        touch.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void Put(UIElement e, int r, int c) { Grid.SetRow(e, r); Grid.SetColumn(e, c); touch.Children.Add(e); }
        var lh = Lbl("Left", 12); lh.Margin = new Thickness(0, 0, 0, 4); Put(lh, 0, 1);
        var rh = Lbl("Right", 12); rh.Margin = new Thickness(0, 0, 0, 4); Put(rh, 0, 3);
        int row = 1;
        foreach (var g in _gestures)
        {
            touch.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
            var gl = Lbl(g.Label(), 13, dim: false); gl.Margin = new Thickness(0, 0, 12, 0); gl.VerticalAlignment = VerticalAlignment.Center;
            Put(gl, row, 0);
            foreach (var isLeft in new[] { true, false })
            {
                var cb = new ComboBox { ItemsSource = TouchAction.All, Margin = new Thickness(0, 2, 0, 2), Height = 32, MaxDropDownHeight = 320 };
                AutomationName(cb, $"{(isLeft ? "Left" : "Right")} {g.Label().ToLowerInvariant()}");
                cb.SelectionChanged += (_, _) => { if (!_suppressTouch && _ble.Protocol.HasValue) A(async () => { await SendTouch(); ShowToast("Gestures sent"); })(this, new RoutedEventArgs()); };
                _touch[(isLeft, g)] = cb; Put(cb, row, isLeft ? 1 : 3);
            }
            row++;
        }
        RenderTouch();
    }
    /// <summary>Gestures: one dropdown per gesture and side, only for gestures the earbuds report. Changes are sent right away.</summary>
    UIElement GesturesPanel() => _gesturesPanel ??= new StackPanel
    {
        Children = { _touchGrid, new Border { Height = 4 }, Row(B("Defaults", A(async () => { _map = TouchMap.Defaults(); RenderTouch(); if (_ble.Protocol.HasValue) { await SendTouch(); ShowToast("Gestures sent"); } }))) }
    };
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
                        WrapRow(B("Import Correction", (_, _) => ImportAutoEq()), B("Save as new…", (_, _) => SavePreset()), B("Import profiles", (_, _) => ImportPresets()), B("Export profiles", (_, _) => ExportPresets()), _restoreEq),
            Lbl("Import Correction loads an AutoEQ ParametricEQ.txt or GraphicEQ.txt and fits it to the 10 bands, offline.", 12)
        }
    };

    void SetConnectedUi(bool on) { _controls.IsEnabled = on; _controls.Opacity = on ? 1 : 0.45; if (_gesturesPanel != null) _gesturesPanel.IsEnabled = on; _touchGrid.IsEnabled = on; _balance.IsEnabled = on; _game.IsEnabled = on; }

    // ----- strip / Solo Mode -----
    void RenderConnection(bool on)
    {
        _dot.Fill = on ? R("Accent") : R("Dim");
        _dot.ToolTip = on ? "Connected" : "Not connected";
        _deviceName.Text = on ? (_connectedName != "" ? _connectedName : "Earbuds") : (_cfg.LastName is { } n && !_demo ? $"{n} · click to connect" : "Click to connect");
        var extra = !on || _model.IsUnknown || _model.Tested ? "" : " · untested model";
        _deviceName.ToolTip = _deviceName.Text + extra;
        if (_nameBtn != null) _nameBtn.ToolTip = _deviceName.ToolTip;
        if (!on) { _case = null; }
        ShowBattery();
    }
    void ShowBattery()
    {
        bool on = _ble.Protocol.HasValue || _demo;
        var solo = on ? BatteryText.Solo(_bl, _br) : null;
        _pillL.Set(_bl, _cl, solo == "R", on); _pillR.Set(_br, _cr, solo == "L", on);
        _pillCase.Set(_case, _caseCharging, false, on); _pillCase.Visibility = _case is null ? Visibility.Hidden : Visibility.Visible;
        void Chip(Border c, string side, bool present)
        {
            c.Visibility = on && (_bl is not null || _br is not null) ? Visibility.Visible : Visibility.Collapsed;
            c.Background = present ? R("Accent") : Brushes.Transparent; c.BorderBrush = present ? R("Accent") : R("Line");
            if (c.Child is TextBlock t) t.Foreground = present ? R("AccentFg") : R("Dim");
            var spoken = $"{side} earbud " + (present ? "in use" : "not in use");
            c.ToolTip = spoken; AutomationName(c, spoken);
        }
        Chip(_chipL, "Left", _bl is not null); Chip(_chipR, "Right", _br is not null);
        UpdateTray();
    }
    void RenderLowLat(bool on) => _lowLatIcon.Stroke = on ? R("Accent") : R("Fg");
    void RenderLock()
    {
        bool l = _cfg.EqLocked;
        _lock.Content = Ico(IconPath(l ? IcLock : IcUnlock, l ? R("Accent") : R("Dim")));
        _lock.ToolTip = l ? "Locked, tap to edit" : "Lock EQ";
        AutomationName(_lock, l ? "EQ locked, tap to edit" : "Lock EQ");
        _lockHint.Visibility = l ? Visibility.Visible : Visibility.Collapsed;
        _lockHint.Foreground = R("Accent");
        foreach (var s in _eq) s.Opacity = l ? 0.4 : 1;
        foreach (var t in _eqLbl) { t.Opacity = 1; t.Foreground = R("Fg"); }
    }
    /// <summary>The EQ is the piece that shrinks on a short screen, and it never grows past 160.</summary>
    void FitEq()
    {
        if (_tuneSlot.ActualHeight <= 0) return;
        double eqH = _eqGrid.ActualHeight, cardH = _tuneCard.ActualHeight;
        if (eqH < 1 || cardH < 1) return;
        double chrome = cardH - eqH;
        if (chrome < 24) return;
        double room = _tuneSlot.ActualHeight - chrome - 4;
        double h = room < 48 ? Math.Max(0, room) : Math.Min(160, room);
        if (Math.Abs(_eqGrid.Height - h) > 0.5) _eqGrid.Height = h;
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

    void UpdateTray() => _tray.Update(string.IsNullOrEmpty(_connectedName) ? "Earbuds" : _connectedName, _bl, _br, _ble.Protocol.HasValue, _ancMode, Models.For(_model), _game.IsChecked == true, _cl, _cr, _case, _caseCharging);

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
        if (_cfg.EqLocked) { ShowToast("EQ locked: tap the padlock to unlock"); return; }
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
            if (_cfg.EqLocked) { ShowToast("EQ locked: tap the padlock to unlock"); return; }
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
    // Built-in corrections use generic names. The connected earbuds' curve is "Tuned preset"; the rest are numbered and never named after a model.
    // A model name can cover several hardware revisions: only a confident match is called "this model" and auto-selected.
    (AutoEq.Profile? P, bool Confident) MyAutoEqMatch => AutoEq.Suggest(_model);
    AutoEq.Profile? MyAutoEq => MyAutoEqMatch.P;
    Dictionary<string, float[]> PresetMap()
    {
        var d = new Dictionary<string, float[]>();
        var mine = MyAutoEq;
        if (mine is not null) d[AutoEq.SuggestLabel(mine, MyAutoEqMatch.Confident)] = mine.Gains;
        foreach (var kv in _presets.All()) d[kv.Key] = kv.Value;
        int n = 2;
        foreach (var p in AutoEq.Profiles) if (!ReferenceEquals(p, mine)) d[AutoEq.ExtraPresetLabel(n++)] = p.Gains;
        return d;
    }
    bool IsFixed(string n) => _presets.IsBuiltIn(n) || n.StartsWith("AutoEQ") || n.StartsWith("Tuned preset");
    /// <summary>Maps a saved name onto the current generic label. Older "AutoEQ · {model}" names are not shown.</summary>
    string? CanonicalPreset(string? saved)
    {
        if (string.IsNullOrEmpty(saved)) return null;
        if (PresetMap().ContainsKey(saved)) return saved;
        if (_model.IsUnknown || !saved.StartsWith("AutoEQ · ", StringComparison.Ordinal)) return null;
        var rest = saved["AutoEQ · ".Length..];
        int cut = rest.IndexOf(" (", StringComparison.Ordinal);
        var legacy = (cut >= 0 ? rest[..cut] : rest).Trim();
        if (legacy.Length == 0) return null;
        var (mine, sure) = MyAutoEqMatch;
        if (mine is not null && mine.Model.Equals(legacy, StringComparison.OrdinalIgnoreCase)) return AutoEq.SuggestLabel(mine, sure);
        int n = 2;
        foreach (var p in AutoEq.Profiles)
        {
            if (ReferenceEquals(p, mine)) continue;
            var label = AutoEq.ExtraPresetLabel(n++);
            if (p.Model.Equals(legacy, StringComparison.OrdinalIgnoreCase)) return label;
        }
        return null;
    }
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
        SetStatus(n == 0 ? NoBuds : $"{n} paired earbud(s). Take the earbuds out of the case, select one, and Connect (Scan if it doesn't connect).");
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
        if (_deviceName.Text.Length > 0)
        {
            _deviceName.ToolTip = _deviceName.Text + (UntestedTag == "" ? "" : " · untested model");
            if (_nameBtn != null) _nameBtn.ToolTip = _deviceName.ToolTip;
        }
        if (UntestedTag != "" && !_status.Text.EndsWith(UntestedTag)) _status.Text += UntestedTag;
        // Hide what the device does not support.
        bool a = _ble.Protocol == Protocol.A || _demo;
        _ancLevelBox.Visibility = f.AncLevel && a ? Visibility.Visible : Visibility.Collapsed;
        _anc[AncMode.Custom].Visibility = _ancLevelBox.Visibility;
        _restoreEq.IsEnabled = DevKey is { } k && _cfg.EqByDevice.ContainsKey(k);
        _ancCard.Visibility = f.Anc && a ? Visibility.Visible : Visibility.Collapsed;
        bool showBal = f.Balance && a, showLat = f.Game;
        if (_balanceRow != null) _balanceRow.Visibility = showBal ? Visibility.Visible : Visibility.Collapsed;
        if (_lowLatRow != null) _lowLatRow.Visibility = showLat ? Visibility.Visible : Visibility.Collapsed;
        if (_listenCard != null) _listenCard.Visibility = showBal || showLat ? Visibility.Visible : Visibility.Collapsed;
        _btnGestures.Visibility = f.Touch ? Visibility.Visible : Visibility.Collapsed;
        // Same as Android: the last-used profile wins; otherwise a confident match. Names stay generic.
        var (ae, sure) = MyAutoEqMatch;
        var last = CanonicalPreset(_cfg.LastPreset);
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
        var item = new DevItem(0x001122334455, null, Protocol.A, Models.Detect("NC9"), "Earbuds (demo) · 00:11:22:33:44:55");
        _devItems.Add(item); _devices.SelectedItem = item;
        _model = item.Model; _connectedName = "Earbuds (demo)";
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
        _dot.Fill = R("Accent"); _deviceName.Text = _connectedName; _deviceName.ToolTip = _connectedName; _nameBtn.ToolTip = _connectedName;
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
            foreach (var c in ProtoJ.StatusQueries)
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
    static string DbWords(double db)
    {
        if (Math.Abs(db) < 0.05) return "0 dB";
        var abs = Math.Abs(db).ToString("0.#", CultureInfo.InvariantCulture);
        return db < 0 ? $"minus {abs} dB" : $"plus {abs} dB";
    }
    static string BandName(int freq, double db) => $"{freq} Hz, {DbWords(db)}";
    void EqLabel(int i)
    {
        var v = _eq[i].Value;
        int shown = (int)Math.Round(v, MidpointRounding.AwayFromZero);
        _eqLbl[i].Text = shown.ToString("+0;-0;0", CultureInfo.InvariantCulture);
        _eqLbl[i].ToolTip = v.ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture) + " dB";
        AutomationName(_eq[i], BandName(EqGrid.Freqs[i], v));
    }
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
            case Protocol p: foreach (var f in EqWire.Frames(p, g)) await _ble.SendAsync(f); break;
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
        _suppressTouch = true;
        try
        {
            foreach (var ((left, g), cb) in _touch)
                cb.SelectedItem = TouchAction.FromA((left ? _map.Left : _map.Right)[(int)g]) ?? TouchAction.None;
        }
        finally { _suppressTouch = false; }
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
        _suppressProfile = true;
        try
        {
            var items = PresetMap().Keys.ToList();
            if (_selected is null) items.Insert(0, CustomItem);
            _profileBox.ItemsSource = items;
            _profileBox.SelectedItem = _selected ?? CustomItem;
        }
        finally { _suppressProfile = false; }
    }
    /// <summary>Tap a chip: applied to the earbuds immediately, with a 3-second Undo.</summary>
    async void ApplyProfile(string n)
    {
        try
        {
            if (_cfg.EqLocked) { ShowToast("EQ locked: tap the padlock to unlock"); return; }
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
    /// <summary>Edit next to the dropdown: save the current Tune, rename or delete your own profiles.</summary>
    void ProfileMenu(FrameworkElement anchor, string? n)
    {
        var sp = new StackPanel();
        if (n is null || IsFixed(n))
        {
            sp.Children.Add(Lbl(n is null ? "Current Tune is not saved" : "Built-in profile", 12));
            sp.Children.Add(new Border { Height = 8 });
            sp.Children.Add(B("Save current Tune as new…", (_, _) => { CloseFlyout(); SavePreset(); }));
        }
        else
        {
            sp.Children.Add(WrapRow(
                B("Save current Tune here", (_, _) => { CloseFlyout(); _presets.Save(n, EqUi()); RefreshPresets(n); ShowToast($"Saved “{n}”"); }),
                B("Rename…", (_, _) => { CloseFlyout(); if (AskName("Rename Sound Profile", n) is { } nn) { var g = _presets.All()[n]; _presets.Save(nn, g); _presets.Delete(n); if (_cfg.LastPreset == n) { _cfg.LastPreset = nn; _cfg.Save(); } RefreshPresets(_selected == n ? nn : _selected); } }),
                B("Delete", (_, _) => { CloseFlyout(); _presets.Delete(n); RefreshPresets(_selected == n ? null : _selected); }),
                B("Save as new…", (_, _) => { CloseFlyout(); SavePreset(); })));
        }
        Flyout(anchor, n ?? "Sound Profiles", sp);
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
                var sup = t.Map.Supported();
                if (!sup.SequenceEqual(_gestures)) { _gestures = sup; BuildTouchGrid(); } else RenderTouch();
                break;
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
