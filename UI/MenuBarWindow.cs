using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Gravitone.Core;
using Gravitone.Interop;
using static Gravitone.Interop.NativeMethods;

namespace Gravitone.UI;

/// <summary>
/// The macOS-style menu bar of one monitor: a full-width strip along its top edge, registered as an
/// app bar so maximized windows start below it. Left: a Start logo and the name of the app in front.
/// Right: third-party tray icons (primary monitor only, as on the Windows 11 taskbar), Wi-Fi /
/// volume / battery (Quick Settings) and the date and time (notifications and calendar). Like the
/// dock, it is a transparent window holding the content over a separate acrylic window
/// (<see cref="MenuBarGlass"/>). What it shows comes from <see cref="MenuBars"/>.
/// </summary>
internal sealed class MenuBarWindow : Window
{
    const double BarHeight = 26; // DIP, like the menu bar of a Mac without a notch

    readonly MenuBars _owner;
    readonly string _device;
    readonly MenuBarGlass _glass = new();
    readonly Grid _root = new() { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) }; // alpha 1: clickable
    readonly Border _logo;
    readonly TextBlock _appName;
    readonly TrayIconsPanel? _tray;
    readonly Border _status;
    readonly TextBlock _wifi, _wifiBack, _volume, _battery;
    readonly Border _clock;
    readonly TextBlock _clockText;
    readonly List<TextBlock> _texts = [];
    readonly List<Shape> _shapes = [];

    AppBar? _appBar;
    IntPtr _hwnd;
    uint _taskbarCreatedMessage;
    uint _explorerPid;
    bool _fullscreen;
    bool _startOpenAtPress;
    bool _placePending;
    IntPtr _placedMonitor;

    public MenuBarWindow(MenuBars owner, string device, bool primary)
    {
        _owner = owner;
        _device = device;
        Title = "Gravitone Menu Bar";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0;
        Top = -100;
        Width = 100;
        Height = BarHeight;

        // The glass lives in its own window; keep it right below us in the z-order.
        _glass.Show();
        new WindowInteropHelper(this).Owner = _glass.Handle;

        _logo = Clickable(WindowsLogo(), 10);
        _logo.MouseLeftButtonDown += (_, _) => _startOpenAtPress = StartMenu.IsOpen(GetForegroundWindow());
        _logo.MouseLeftButtonUp += (_, _) =>
        {
            // Start may already have closed itself on the press; toggling then would reopen it.
            if (!_startOpenAtPress || StartMenu.IsOpen(GetForegroundWindow())) StartMenu.Toggle();
        };
        _logo.MouseRightButtonUp += (_, _) => StartMenu.ShowQuickLinkMenu();

        _appName = Text("", 13, FontWeights.Bold);
        _appName.Margin = new Thickness(6, 0, 0, 0);
        _appName.VerticalAlignment = VerticalAlignment.Center;

        // One tray service per session: it lives in the primary monitor's bar.
        if (primary) _tray = new TrayIconsPanel { Margin = new Thickness(0, 2, 4, 2) };

        _wifiBack = Glyph("");
        _wifiBack.Opacity = 0.3;
        _wifi = Glyph("");
        var wifiCell = new Grid { Margin = new Thickness(0, 0, 10, 0) };
        wifiCell.Children.Add(_wifiBack);
        wifiCell.Children.Add(_wifi);
        _volume = Glyph("");
        _volume.Margin = new Thickness(0, 0, 10, 0);
        _battery = Glyph("");
        _battery.FontSize = 16;
        var statusRow = new StackPanel { Orientation = Orientation.Horizontal };
        statusRow.Children.Add(wifiCell);
        statusRow.Children.Add(_volume);
        statusRow.Children.Add(_battery);
        _status = Clickable(statusRow, 8);
        _status.MouseLeftButtonUp += (_, _) => ShellFlyouts.QuickSettings();

        _clockText = Text("", 13, FontWeights.Normal);
        _clockText.VerticalAlignment = VerticalAlignment.Center;
        _clock = Clickable(_clockText, 8);
        _clock.MouseLeftButtonUp += (_, _) => ShellFlyouts.NotificationCenter();

        var left = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
        left.Children.Add(_logo);
        left.Children.Add(_appName);
        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 8, 0),
        };
        if (_tray is not null) right.Children.Add(_tray);
        right.Children.Add(_status);
        right.Children.Add(_clock);

        _root.Children.Add(left);
        _root.Children.Add(right);
        Content = _root;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        MakeToolWindow(_hwnd);
        HwndSource.FromHwnd(_hwnd)!.AddHook(WndProc);
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        _explorerPid = TaskbarState.ExplorerProcessId();
        _appBar = new AppBar(_hwnd);
        _appBar.Register();
        // Moved to a monitor with another scale, WPF resizes the window to its own idea of the size.
        DpiChanged += (_, _) => SchedulePlace();

        Place();
        ApplyTheme();
        ShowAppName(_owner.AppName);
        ShowClock(_owner.ClockText, _owner.ClockTip);
    }

    /// <summary>Gives the reserved strip and the tray back.</summary>
    protected override void OnClosed(EventArgs e)
    {
        _tray?.Dispose();
        _appBar?.Dispose();
        _appBar = null;
        _glass.Close();
        base.OnClosed(e);
    }

    public void ApplyTheme()
    {
        _glass.ApplyTheme();
        bool dark = Theme.IsDark;
        var text = new SolidColorBrush(dark ? Color.FromRgb(0xF5, 0xF5, 0xF7) : Color.FromRgb(0x1D, 0x1D, 0x1F));
        text.Freeze();
        foreach (var t in _texts) t.Foreground = text;
        foreach (var s in _shapes) s.Fill = text;
        if (_tray is not null) _tray.HoverBrush = HoverBrush;
    }

    /// <summary>After sleep or unlock DWM may show the glass as inactive (flat colour): make it active again.</summary>
    public void Reactivate() => _glass.Reactivate();

    static Brush HoverBrush => Theme.IsDark ? HoverDark : HoverLight;
    static readonly Brush HoverDark = Frozen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
    static readonly Brush HoverLight = Frozen(Color.FromArgb(0x1F, 0x00, 0x00, 0x00));

    static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    // ───────────────────────── Placement ─────────────────────────

    public void SchedulePlace(bool forget = false)
    {
        if (forget) _appBar?.ForgetPosition();
        if (_placePending) return;
        _placePending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _placePending = false;
            Place();
        });
    }

    void Place()
    {
        if (_hwnd == IntPtr.Zero || _appBar is null) return;
        var display = Displays.All().FirstOrDefault(d => string.Equals(d.DeviceName, _device, StringComparison.OrdinalIgnoreCase));
        if (display.Handle == IntPtr.Zero) return; // unplugged: MenuBars closes this bar

        if (_placedMonitor != IntPtr.Zero && _placedMonitor != display.Handle) _appBar.Reset();
        _placedMonitor = display.Handle;

        int height = (int)Math.Ceiling(BarHeight * display.Scale);
        var bar = _appBar.SetPosition(ABE_TOP, display.Bounds, height);
        var rect = new RECT(display.Bounds.Left, bar.Top, display.Bounds.Right, bar.Top + height);
        // Moving an app bar notifies the others, which move and notify back: only move when needed.
        if (!GetWindowRect(_hwnd, out var current) || !current.Equals(rect))
            SetWindowPos(_hwnd, HWND_TOPMOST, rect.Left, rect.Top, rect.Width, rect.Height, SWP_NOACTIVATE);
        _glass.SetBounds(rect);
        _root.Width = rect.Width / display.Scale;
        _root.Height = rect.Height / display.Scale;
        _tray?.SetHostRect(rect);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_MOUSEACTIVATE:
                // Clicks never take the focus from the app in front, as on the Mac.
                handled = true;
                return MA_NOACTIVATE;
            case WM_DISPLAYCHANGE:
                SchedulePlace(forget: true);
                return IntPtr.Zero;
            case WM_WINDOWPOSCHANGED:
                _appBar?.NotifyWindowPosChanged();
                return IntPtr.Zero;
        }
        if (msg == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            // The tray service broadcasts this too; only a new Explorer has forgotten our app bar.
            uint pid = TaskbarState.ExplorerProcessId();
            if (pid != 0 && pid != _explorerPid)
            {
                _explorerPid = pid;
                _appBar?.Reregister();
                SchedulePlace(forget: true);
            }
        }
        else if (_appBar is not null && msg == _appBar.CallbackMessage)
        {
            switch ((int)wParam)
            {
                case ABN_POSCHANGED:
                    SchedulePlace();
                    break;
                case ABN_FULLSCREENAPP:
                    OnFullscreenApp(lParam != IntPtr.Zero);
                    break;
            }
        }
        return IntPtr.Zero;
    }

    /// <summary>Step aside while a full-screen app (video, game, presentation) is in front on this monitor.</summary>
    void OnFullscreenApp(bool fullscreen)
    {
        if (fullscreen && !Displays.IsFullscreenAppOn(_hwnd)) return;
        if (_fullscreen == fullscreen) return;
        _fullscreen = fullscreen;
        if (fullscreen)
        {
            ShowWindow(_hwnd, SW_HIDE);
            _glass.SetVisible(false);
        }
        else
        {
            _glass.SetVisible(true);
            ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
    }

    // ───────────────────────── Content ─────────────────────────

    public void ShowAppName(string name) => _appName.Text = name;

    public void ShowClock(string text, string tip)
    {
        _clockText.Text = text;
        _clock.ToolTip = tip;
    }

    public void ShowStatus(SystemStatus s)
    {
        _wifiBack.Visibility = s.Network == NetworkKind.Wifi ? Visibility.Visible : Visibility.Collapsed;
        (_wifi.Text, string wifiTip) = s.Network switch
        {
            NetworkKind.Wifi => (s.WifiSignal switch
            {
                < 25 => "",
                < 50 => "",
                < 75 => "",
                _ => "",
            }, Loc.T("Wi-Fi: signal {0}%", s.WifiSignal)),
            NetworkKind.Ethernet => ("", Loc.T("Ethernet: connected")),
            _ => ("", Loc.T("No Internet connection")),
        };

        int volume = (int)Math.Round((s.Volume ?? 0) * 100);
        _volume.Text = s.Volume is null || s.Muted ? "" : volume switch
        {
            0 => "",
            < 34 => "",
            < 67 => "",
            _ => "",
        };
        string volumeTip = s.Volume is null ? Loc.T("No audio device")
            : s.Muted ? Loc.T("Volume: muted") : Loc.T("Volume: {0}%", volume);

        _battery.Visibility = s.HasBattery ? Visibility.Visible : Visibility.Collapsed;
        int level = (int)Math.Round((s.BatteryPercent ?? 100) / 10.0);
        _battery.Text = ((char)((s.Charging ? 0xEBAB : 0xEBA0) + level)).ToString();
        string batteryTip = s.BatteryPercent is int pct ? (s.Charging ? Loc.T("Battery: {0}% (charging)", pct) : Loc.T("Battery: {0}%", pct)) : Loc.T("Battery");

        var tips = new List<string> { wifiTip, volumeTip };
        if (s.HasBattery) tips.Add(batteryTip);
        _status.ToolTip = string.Join("\n", tips);
    }

    // ───────────────────────── Building blocks ─────────────────────────

    TextBlock Text(string text, double size, FontWeight weight)
    {
        var t = new TextBlock
        {
            Text = text,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = size,
            FontWeight = weight,
        };
        TextOptions.SetTextFormattingMode(t, TextFormattingMode.Display);
        _texts.Add(t);
        return t;
    }

    TextBlock Glyph(string glyph)
    {
        var t = Text(glyph, 14, FontWeights.Normal);
        t.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        t.VerticalAlignment = VerticalAlignment.Center;
        return t;
    }

    /// <summary>The four panes of the Windows logo, drawn in the text colour like the Apple menu.</summary>
    UIElement WindowsLogo()
    {
        const double pane = 5.5, gap = 1.2;
        var grid = new Grid { Width = pane * 2 + gap, Height = pane * 2 + gap, VerticalAlignment = VerticalAlignment.Center };
        for (int row = 0; row < 2; row++)
        {
            for (int col = 0; col < 2; col++)
            {
                var r = new Rectangle
                {
                    Width = pane,
                    Height = pane,
                    HorizontalAlignment = col == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                    VerticalAlignment = row == 0 ? VerticalAlignment.Top : VerticalAlignment.Bottom,
                };
                _shapes.Add(r);
                grid.Children.Add(r);
            }
        }
        return grid;
    }

    /// <summary>A menu-bar item: rounded highlight under the pointer, like the Mac's menu extras.</summary>
    Border Clickable(UIElement child, double padding)
    {
        var b = new Border
        {
            Child = child,
            Padding = new Thickness(padding, 0, padding, 0),
            Margin = new Thickness(0, 2, 0, 2),
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
        };
        ToolTipService.SetInitialShowDelay(b, 600);
        b.MouseEnter += (_, _) => b.Background = HoverBrush;
        b.MouseLeave += (_, _) => b.Background = Brushes.Transparent;
        return b;
    }
}

/// <summary>The acrylic strip under the menu bar's content.</summary>
internal sealed class MenuBarGlass : AcrylicWindow
{
    readonly Border _tint = new();
    bool _visible = true;

    public MenuBarGlass() : base(rounded: false)
    {
        Title = "Gravitone Menu Bar Glass";
        Content = _tint;
    }

    public override void ApplyTheme()
    {
        ApplyGlassTheme(DWMWA_COLOR_NONE);
        _tint.Background = new SolidColorBrush(Theme.IsDark
            ? Color.FromArgb(0x33, 0x10, 0x10, 0x10)
            : Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
    }

    public void SetVisible(bool visible)
    {
        if (Handle == IntPtr.Zero || visible == _visible) return;
        _visible = visible;
        ShowWindow(Handle, visible ? SW_SHOWNOACTIVATE : SW_HIDE);
    }
}
