using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Gravitone.Core;
using Gravitone.Interop;
using static Gravitone.Interop.NativeMethods;

namespace Gravitone.UI;

/// <summary>
/// Transparent, click-through strip along the screen edge that draws the icons, the labels and
/// the running dots, owns the magnification / auto-hide / bounce animations and drives the
/// backdrop plate. Only the dock area is hit-testable; elsewhere clicks fall through.
/// </summary>
internal sealed class DockWindow : Window
{
    const double RevealDelayMs = 150;
    const double HideDelayMs = 350;

    readonly DockConfig _config;
    readonly BackdropWindow _backdrop;
    readonly List<DockItem> _pinned = [];
    readonly List<DockItem> _running = [];      // open but not pinned, after the separator
    readonly List<DockItem> _folders = [];      // stacks, between the open apps and the trash
    readonly DockItem _separator = DockItem.CreateSeparator();
    readonly DockItem _folderSeparator = DockItem.CreateSeparator();
    readonly DockItem _trash = DockItem.CreateTrash();
    readonly List<DockItem> _entries = [];      // what is on screen, in order
    readonly List<DockSlot> _slots = [];
    readonly Dictionary<DockItem, DockItemView> _views = [];
    readonly Canvas _canvas = new();
    readonly Rectangle _hitArea = new() { Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
    readonly Border _label;
    readonly TextBlock _labelText;
    readonly DockLayout _layout = new();
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly DispatcherTimer _revealTimer;
    readonly DispatcherTimer _hideTimer;

    const int TaskbarHotkeyId = 1;

    DockMetrics _m;
    AppBar? _appBar;
    WindowTracker? _tracker;
    TaskbarReplacer? _replacer;
    Hotkey? _taskbarHotkey;
    NumberShortcuts? _numberShortcuts;
    StackWindow? _stack;
    DockItem? _stackClosedItem;
    long _stackClosedAt;
    DispatcherTimer? _trashTimer;
    readonly DispatcherTimer _trashDebounce = new() { Interval = TimeSpan.FromMilliseconds(300) };
    RecycleBin.Watch? _trashWatch;
    bool _trashEmpty = true;
    bool _trashChecking, _trashRecheck;
    long _trashCheckedAt;
    bool _trashDropHover;
    MenuBars? _menuBars;
    IntPtr _hwnd;
    uint _taskbarCreatedMessage;
    uint _genieTestMessage;
    uint _quitMessage;
    uint _explorerPid;
    RECT _windowPx;
    double _scale = 1;

    // Monitors: the dock's monitor (empty = the primary), where it was last placed, and the layout of all monitors
    string _displayName;
    string _chosenDisplay;
    IntPtr _placedMonitor;
    string _displayLayout = "";
    readonly DispatcherTimer _displayCheck = new() { Interval = TimeSpan.FromMilliseconds(400) };
    readonly DispatcherTimer _followTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    int _edgeDwell;

    // Sleep, lock, screen off: why the dock is paused (it runs again when none is left)
    readonly HashSet<string> _pauseReasons = [];
    readonly DispatcherTimer _settleTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    // Started at sign-in the shell may not be ready: for the first seconds, retry what failed to load.
    readonly DispatcherTimer _startupCheck = new() { Interval = TimeSpan.FromSeconds(5) };
    int _startupPasses;
    IntPtr _powerNotify;
    DispatcherTimer? _diagTimer;
    double _length;     // along the edge, DIP
    double _thickness;  // away from the edge, DIP

    // Interaction state
    double? _pointer;
    bool _hovered;
    int _hoverIndex = -1;
    DockItem? _pressed;
    IntPtr _foregroundAtPress;
    bool _startOpenAtPress;
    bool _menuOpen;
    bool _fullscreen;
    (double U0, double U1, double V0, double V1) _zone;

    // Drag state: reordering / removing a pinned icon, and dropping a file on the dock
    Point _pressPos;
    bool _dragging;
    bool _dragRemove;
    DockItem? _dragItem;
    double _dragU, _dragV, _dragFracU, _dragFracV, _iconBase;
    Rect _dragRect;
    DockItem? _dropGhost;

    // Animation state
    double _magnify;        // 0 rest .. 1 magnified
    double _hide;           // 0 shown .. 1 hidden
    bool _hideRequested;
    bool _animating;
    double _lastTick;
    bool _placePending;

    public DockWindow(DockConfig config, BackdropWindow backdrop)
    {
        _config = config;
        _backdrop = backdrop;
        _m = BuildMetrics();

        Title = "Gravitone";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        AllowDrop = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0;
        Top = 0;
        Width = 200;
        Height = 100;

        // The dock plate lives in its own window; keep it right below us in the z-order.
        new WindowInteropHelper(this).Owner = backdrop.Handle;

        _labelText = new TextBlock
        {
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 13,
        };
        _label = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 3, 10, 4),
            BorderThickness = new Thickness(1),
            Child = _labelText,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
            Effect = new DropShadowEffect { BlurRadius = 10, ShadowDepth = 1, Opacity = 0.25, Direction = 270 },
        };
        Panel.SetZIndex(_label, 1000);

        _canvas.Children.Add(_hitArea);
        _canvas.Children.Add(_label);
        foreach (var itemConfig in config.Items) _pinned.Add(new DockItem(itemConfig));
        foreach (var folderConfig in config.Folders) _folders.Add(new DockItem(folderConfig));
        _folderSeparator.Presence = _folderSeparator.TargetPresence = 1;
        RebuildEntries();
        Content = _canvas;

        _revealTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RevealDelayMs) };
        _revealTimer.Tick += (_, _) =>
        {
            _revealTimer.Stop();
            if (_hovered) SetHideRequested(false);
        };
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HideDelayMs) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (!_hovered && !_menuOpen && _config.Settings.AutoHide) SetHideRequested(true);
        };

        _hideRequested = config.Settings.AutoHide;
        _hide = _hideRequested ? 1 : 0;

        _displayName = _chosenDisplay = config.Settings.Display;
        _displayCheck.Tick += (_, _) =>
        {
            _displayCheck.Stop();
            CheckDisplays();
        };
        _followTimer.Tick += (_, _) => FollowPointer();
        _settleTimer.Tick += (_, _) =>
        {
            // Monitors and Explorer settle a few seconds after waking up: look once more.
            _settleTimer.Stop();
            Revalidate();
        };
        _trashDebounce.Tick += (_, _) =>
        {
            _trashDebounce.Stop();
            CheckTrash();
        };

        ApplyTheme();
        DpiChanged += (_, _) => SchedulePlace();
    }

    DockEdge Edge => _config.Settings.Edge;
    double Now => _clock.Elapsed.TotalSeconds;

    DockMetrics BuildMetrics()
    {
        var s = _config.Settings;
        return new DockMetrics(s.IconSize, s.Magnification ? Math.Max(s.MagnifiedSize, s.IconSize) : s.IconSize, s.Edge);
    }

    // ───────────────────────── Window setup & placement ─────────────────────────

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        MakeToolWindow(_hwnd);
        HwndSource.FromHwnd(_hwnd)!.AddHook(WndProc);
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        _genieTestMessage = RegisterWindowMessage("Gravitone.GenieTest");
        _quitMessage = RegisterWindowMessage(CommandLine.QuitMessageName);

        // Hide the Windows taskbar first, so the dock takes the space it leaves.
        _replacer = new TaskbarReplacer(_config.Settings.ReplaceTaskbar);
        _replacer.Start();
        _taskbarHotkey = Hotkey.Register(_hwnd, TaskbarHotkeyId, _config.Settings.TaskbarHotkey);

        _appBar = new AppBar(_hwnd);
        _appBar.Register();
        _explorerPid = TaskbarState.ExplorerProcessId();

        if (_config.Settings.NumberShortcuts) _numberShortcuts = new NumberShortcuts(OnNumberShortcut);
        StartTrashWatch();

        _tracker = new WindowTracker(_hwnd);
        _tracker.MinimizeTarget = MinimizeTargetFor;
        _tracker.Changed += SyncWindows;
        _tracker.Activated += OnWindowActivated;
        _tracker.AttentionRequested += OnAttentionRequested;
        SyncWindows();
        if (Environment.GetCommandLineArgs().Contains("--diag")) StartDiagnostics();

        // Lock, user switch (session), sleep and screen off (power): pause, then check everything again.
        WTSRegisterSessionNotification(_hwnd, 0);
        var displayState = GUID_CONSOLE_DISPLAY_STATE;
        _powerNotify = RegisterPowerSettingNotification(_hwnd, ref displayState, 0);

        _displayLayout = Displays.Layout();
        Place();
        if (_config.Settings.MenuBar) ShowMenuBar();
        UpdateFollowTimer();
        if (Environment.GetCommandLineArgs().Contains(AutoStart.AutoStartArg)) LaunchOpenAtStartItems();

        _startupCheck.Tick += (_, _) => StartupCheck();
        _startupCheck.Start();
    }

    /// <summary>
    /// Right after sign-in the shell can be slow to answer: icons come back empty, the glass is drawn
    /// flat. Load again what is missing; and if the dock still cannot show its apps, do not leave the
    /// user without any taskbar: give the Windows taskbar back for this run.
    /// </summary>
    void StartupCheck()
    {
        _startupPasses++;
        bool loaded = false;
        foreach (var item in _pinned.Concat(_folders).Append(_trash)) loaded |= item.ReloadIcon();
        _backdrop.ApplyTheme();
        _backdrop.Reactivate();
        if (loaded)
        {
            Log.Info("Icons that were missing at startup are loaded now");
            SyncWindows();
        }

        int withIcon = _pinned.Count(i => i.Icon is not null);
        bool complete = withIcon == _pinned.Count;
        bool usable = withIcon * 2 >= _pinned.Count;
        if (complete && _startupPasses >= 3)
        {
            _startupCheck.Stop();
        }
        else if (_startupPasses >= 8)
        {
            _startupCheck.Stop();
            if (!usable && _replacer is { Enabled: true })
            {
                Log.Info($"The dock has icons for {withIcon} of {_pinned.Count} apps after startup: giving the Windows taskbar back");
                _replacer.SetEnabled(false);
            }
        }
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        Place();
    }

    protected override void OnClosed(EventArgs e)
    {
        _tracker?.Dispose();
        ReleaseShell();
        base.OnClosed(e);
    }

    /// <summary>Gives the reserved screen space and the Windows taskbar back. Safe to call more than once.</summary>
    public void ReleaseShell()
    {
        // The taskbar first: it matters most, and nothing below may keep it from coming back.
        Try(() => _replacer?.Dispose(), "Restoring the taskbar");
        Try(HideMenuBar, "Closing the menu bar");
        _taskbarHotkey?.Dispose();
        _taskbarHotkey = null;
        _numberShortcuts?.Dispose();
        _numberShortcuts = null;
        _stack?.Close();
        Try(() => _appBar?.Dispose(), "Releasing the Dock's screen space");
        _appBar = null;
        _followTimer.Stop();
        _trashTimer?.Stop();
        _trashWatch?.Dispose();
        _trashWatch = null;
        if (_hwnd != IntPtr.Zero) WTSUnRegisterSessionNotification(_hwnd);
        if (_powerNotify != IntPtr.Zero) UnregisterPowerSettingNotification(_powerNotify);
        _powerNotify = IntPtr.Zero;

        static void Try(Action action, string context)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Log.Error(ex, context);
            }
        }
    }

    void ShowMenuBar()
    {
        if (_menuBars is not null) return;
        _menuBars = new MenuBars(DockNameFor, _config.Settings.MenuBarOnAllDisplays);
        if (_pauseReasons.Count > 0) _menuBars.SetPaused(true);
    }

    void HideMenuBar()
    {
        if (_menuBars is null) return;
        _menuBars.Dispose(); // gives the reserved strips and the tray back
        _menuBars = null;
    }

    /// <summary>The dock's name for the app that owns a window, so the menu bar and the dock agree.</summary>
    string? DockNameFor(IntPtr hwnd) =>
        _pinned.Concat(_running).FirstOrDefault(i => i.Windows.Any(w => w.Handle == hwnd))?.Name;

    void SchedulePlace()
    {
        if (_placePending) return;
        _placePending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _placePending = false;
            Place();
        });
    }

    /// <summary>The monitor the dock is on: the one chosen (or reached by the pointer), else the primary.</summary>
    Display CurrentDisplay() => Displays.ByNameOrPrimary(_displayName);

    /// <summary>Reserves the dock's strip of its monitor and sizes this window to cover it.</summary>
    void Place()
    {
        if (_hwnd == IntPtr.Zero || _appBar is null) return;
        var settings = _config.Settings;

        var display = CurrentDisplay();
        if (_placedMonitor != IntPtr.Zero && _placedMonitor != display.Handle)
        {
            // Another monitor: give the old strip back before reserving the new one.
            _appBar.Reset();
            _stack?.Close();
            Log.Info($"Dock moved to {display.DeviceName} ({display.Bounds}, scale {display.Scale:0.##})");
        }
        _placedMonitor = display.Handle;
        _scale = display.Scale;
        _m = BuildMetrics();

        int thick = (int)Math.Ceiling(_m.WindowThickness * _scale);
        int reserve = settings.AutoHide ? 0 : (int)Math.Ceiling((_m.EdgeGap + _m.PlateThickness) * _scale);
        var bar = _appBar.SetPosition(Edge, display.Bounds, reserve);
        var work = display.Work;

        _windowPx = Edge switch
        {
            DockEdge.Left => new RECT(bar.Left, work.Top, bar.Left + thick, work.Bottom),
            DockEdge.Right => new RECT(bar.Right - thick, work.Top, bar.Right, work.Bottom),
            _ => new RECT(display.Bounds.Left, bar.Bottom - thick, display.Bounds.Right, bar.Bottom),
        };
        // Moving an app bar notifies the others (the menu bar), which move and notify back.
        if (!GetWindowRect(_hwnd, out var current) || !current.Equals(_windowPx))
            SetWindowPos(_hwnd, HWND_TOPMOST, _windowPx.Left, _windowPx.Top, _windowPx.Width, _windowPx.Height, SWP_NOACTIVATE);

        double w = _windowPx.Width / _scale, h = _windowPx.Height / _scale;
        _canvas.Width = w;
        _canvas.Height = h;
        (_length, _thickness) = Edge == DockEdge.Bottom ? (w, h) : (h, w);
        UpdateFrame();
    }

    // ───────────────────────── Running apps ─────────────────────────

    /// <summary>Hands every open window to the dock item of its app, creating items for unpinned apps.</summary>
    void SyncWindows()
    {
        if (_tracker is null) return;
        foreach (var item in _pinned.Concat(_running)) item.Windows.Clear();
        PruneGenieCache();

        foreach (var window in _tracker.Windows.OrderByDescending(w => w.LastActivated))
        {
            var owner = _pinned.FirstOrDefault(p => p.Identity.Matches(window.Identity))
                        ?? _running.FirstOrDefault(r => r.Identity.Matches(window.Identity));
            if (owner is null)
            {
                if (window.Identity.Key is null) continue; // nothing to group it by
                owner = new DockItem(window);
                _running.Add(owner);
            }
            owner.Windows.Add(window);
        }

        double now = Now;
        foreach (var item in _running) item.TargetPresence = item.IsRunning ? 1 : 0;
        foreach (var item in _pinned.Concat(_running))
        {
            if (item.IsRunning && item.Bounce.Kind == BounceKind.Launch) item.Bounce.Stop(now);
        }
        _separator.TargetPresence = _running.Any(r => r.TargetPresence > 0) ? 1 : 0;

        RebuildEntries();
        foreach (var view in _views.Values) view.Refresh();
        EnsureAnimating();
    }

    /// <summary>The dock icon of the app that owns <paramref name="hwnd"/>, in screen pixels: where its minimize animation ends.</summary>
    RECT? MinimizeTargetFor(IntPtr hwnd)
    {
        var owner = _pinned.Concat(_running).FirstOrDefault(i => i.Windows.Any(w => w.Handle == hwnd));
        int index = owner is null ? -1 : _entries.IndexOf(owner);
        if (index < 0 || _layout.Sizes.Length <= index) return null;
        var r = ToWindow(_layout.Starts[index], _iconBase, _layout.Sizes[index], _layout.Sizes[index]);
        return new RECT(
            _windowPx.Left + (int)Math.Round(r.Left * _scale), _windowPx.Top + (int)Math.Round(r.Top * _scale),
            _windowPx.Left + (int)Math.Round(r.Right * _scale), _windowPx.Top + (int)Math.Round(r.Bottom * _scale));
    }

    // ───────────────────────── Genie effect ─────────────────────────

    readonly Dictionary<IntPtr, GenieCapture> _genieCache = [];   // a minimized window as it looked, for the way back
    readonly List<IntPtr> _genieOrder = [];
    GenieAnimation? _genie;
    const int GenieCacheMax = 5;                                   // a full-screen capture is ~8 MB

    static void SetTransitions(IntPtr hwnd, bool disabled)
    {
        int value = disabled ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref value, sizeof(int));
    }

    /// <summary>Minimizes a window: flowing into its dock icon, or the plain way when that cannot be shown.</summary>
    void MinimizeWindow(IntPtr hwnd)
    {
        if (!TryGenieMinimize(hwnd)) WindowActions.Minimize(hwnd);
    }

    /// <summary>Why the genie was skipped for a window, in the log (null = it plays).</summary>
    string? GenieBlocker(IntPtr hwnd)
    {
        if (!_config.Settings.Genie) return "turned off in settings";
        if (_genie is not null) return "another animation is running";
        if (_fullscreen) return "full-screen app";
        if (_pauseReasons.Count > 0) return "Dock paused";
        if (_hide >= 0.02) return "Dock hidden";
        if (!GenieRenderer.Instance.Available) return "graphics card not usable";
        if (MinimizeTargetFor(hwnd) is null) return "icon not found in the Dock";
        return null;
    }

    void LogGenieSkipped(IntPtr hwnd, string action, string why)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        string name;
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById((int)pid);
            name = p.ProcessName;
        }
        catch (Exception)
        {
            name = pid.ToString();
        }
        Log.Info($"Genie skipped ({action} of {name}): {why}");
    }

    bool TryGenieMinimize(IntPtr hwnd)
    {
        if (GenieBlocker(hwnd) is { } blocked)
        {
            LogGenieSkipped(hwnd, "minimize", blocked);
            return false;
        }
        if (IsIconic(hwnd) || !IsWindowVisible(hwnd) || IsCloaked(hwnd))
        {
            LogGenieSkipped(hwnd, "minimize", "window already minimized, hidden or on another desktop");
            return false;
        }
        var target = MinimizeTargetFor(hwnd)!.Value;
        var capture = GenieCapture.From(hwnd);
        if (capture is null)
        {
            LogGenieSkipped(hwnd, "minimize", GenieCapture.LastFailure);
            return false;
        }

        RememberCapture(hwnd, capture);
        SetTransitions(hwnd, true); // Windows' own shrinking would play under ours
        var animation = new GenieAnimation(capture, target, Edge, expand: false,
            started: () => WindowActions.Minimize(hwnd),
            finished: () => { },
            durationMs: 520, lingerMs: 0);
        animation.Ended += () =>
        {
            SetTransitions(hwnd, false);
            _genie = null;
            // A window that would not minimize is simply still there.
            if (!IsIconic(hwnd)) _genieCache.Remove(hwnd);
        };
        _genie = animation;
        if (animation.Start(_hwnd)) return true;
        _genie = null;
        SetTransitions(hwnd, false);
        return false;
    }

    /// <summary>Brings a window forward; a minimized one grows out of its dock icon.</summary>
    void ActivateWindow(IntPtr hwnd)
    {
        if (!(IsIconic(hwnd) && TryGenieRestore(hwnd))) WindowActions.Activate(hwnd);
    }

    bool TryGenieRestore(IntPtr hwnd)
    {
        if (GenieBlocker(hwnd) is { } blocked)
        {
            LogGenieSkipped(hwnd, "restore", blocked);
            return false;
        }
        if (!_genieCache.TryGetValue(hwnd, out var capture))
        {
            LogGenieSkipped(hwnd, "restore", "no captured image: the window was not minimized from the Dock");
            return false;
        }
        var target = MinimizeTargetFor(hwnd)!.Value;

        SetTransitions(hwnd, true);
        var animation = new GenieAnimation(capture, target, Edge, expand: true,
            started: null,
            finished: () => WindowActions.Activate(hwnd),
            durationMs: 460, lingerMs: 140);
        animation.Ended += () =>
        {
            SetTransitions(hwnd, false);
            _genieCache.Remove(hwnd);
            _genieOrder.Remove(hwnd);
            _genie = null;
        };
        _genie = animation;
        if (animation.Start(_hwnd)) return true;
        _genie = null;
        SetTransitions(hwnd, false);
        return false;
    }

    void RememberCapture(IntPtr hwnd, GenieCapture capture)
    {
        _genieCache[hwnd] = capture;
        _genieOrder.Remove(hwnd);
        _genieOrder.Add(hwnd);
        while (_genieOrder.Count > GenieCacheMax)
        {
            _genieCache.Remove(_genieOrder[0]);
            _genieOrder.RemoveAt(0);
        }
    }

    void PruneGenieCache()
    {
        foreach (var hwnd in _genieOrder.Where(h => !IsWindow(h) || !IsIconic(h)).ToList())
        {
            _genieCache.Remove(hwnd);
            _genieOrder.Remove(hwnd);
        }
    }

    void OnWindowActivated(TrackedWindow window)
    {
        SyncWindows(); // most-recently-used order changed
        var owner = OwnerOf(window);
        if (owner is not null && owner.Bounce.Kind == BounceKind.Attention) owner.Bounce.Stop(Now);
    }

    void OnAttentionRequested(TrackedWindow window)
    {
        // The app is already in front: nothing to call attention to.
        if (window.Handle == GetForegroundWindow()) return;
        OwnerOf(window)?.Bounce.Start(BounceKind.Attention, Now);
        EnsureAnimating();
    }

    DockItem? OwnerOf(TrackedWindow window) =>
        _pinned.Concat(_running).FirstOrDefault(i => i.Windows.Contains(window));

    /// <summary>Pinned apps, then (if any) the separator and the running unpinned apps.</summary>
    void RebuildEntries()
    {
        _entries.Clear();
        _entries.AddRange(_pinned);
        if (_running.Count > 0 || _separator.Presence > 0) _entries.Add(_separator);
        _entries.AddRange(_running);
        _entries.Add(_folderSeparator);
        _entries.AddRange(_folders);
        _entries.Add(_trash);

        foreach (var gone in _views.Keys.Where(k => !_entries.Contains(k)).ToList())
        {
            _views[gone].RemoveFrom(_canvas);
            _views.Remove(gone);
        }
        foreach (var item in _entries)
        {
            if (_views.ContainsKey(item)) continue;
            var view = new DockItemView(item);
            view.AddTo(_canvas);
            _views.Add(item, view);
        }
        if (_pressed is not null && !_entries.Contains(_pressed)) _pressed = null;
    }

    // ───────────────────────── Frame ─────────────────────────

    void EnsureAnimating()
    {
        if (_animating) return;
        _animating = true;
        _lastTick = Now;
        CompositionTarget.Rendering += OnRendering;
    }

    void OnRendering(object? sender, EventArgs e)
    {
        double now = Now;
        double dt = Math.Clamp(now - _lastTick, 0, 0.05);
        _lastTick = now;

        double magnifyTarget = _hovered && _hide < 0.02 && !_menuOpen && !_dragging ? 1 : 0;
        _magnify = Approach(_magnify, magnifyTarget, dt, magnifyTarget > _magnify ? 0.045 : 0.075);
        double hideTarget = _hideRequested ? 1 : 0;
        _hide = Approach(_hide, hideTarget, dt, 0.065);

        bool busy = false;
        foreach (var item in _entries)
        {
            item.Presence = Approach(item.Presence, item.TargetPresence, dt, 0.09);
            busy |= item.Presence != item.TargetPresence || item.Bounce.IsActive;
        }
        if (_pinned.RemoveAll(p => p.IsGhost && p.Presence == 0 && p.TargetPresence == 0)
            + _folders.RemoveAll(p => p.IsGhost && p.Presence == 0 && p.TargetPresence == 0) > 0)
        {
            RebuildEntries();
            busy = true;
        }
        bool separatorGone = _separator.Presence == 0 && _running.Count == 0 && _entries.Contains(_separator);
        if (_running.RemoveAll(r => r.Presence == 0 && r.TargetPresence == 0) > 0 || separatorGone)
            RebuildEntries();

        UpdateFrame();

        if (!busy && _magnify == magnifyTarget && _hide == hideTarget)
        {
            CompositionTarget.Rendering -= OnRendering;
            _animating = false;
        }
    }

    /// <summary>Exponential ease towards the target; snaps when close enough.</summary>
    static double Approach(double current, double target, double dt, double tau)
    {
        double next = current + (target - current) * (1 - Math.Exp(-dt / tau));
        return Math.Abs(target - next) < 0.002 ? target : next;
    }

    void UpdateFrame()
    {
        if (_hwnd == IntPtr.Zero || _length <= 0) return;

        _slots.Clear();
        foreach (var item in _entries) _slots.Add(new DockSlot(item.Kind == DockItemKind.App, item.Presence));
        _layout.Compute(_m, _slots, _length, _pointer, _magnify);

        _hoverIndex = -1;
        if (_hovered && _pointer is double p && _hide < 0.02 && !_menuOpen && !_dragging)
        {
            int i = _layout.ItemAt(p);
            if (i >= 0 && _entries[i].Kind == DockItemKind.App && _entries[i].TargetPresence > 0) _hoverIndex = i;
        }

        double now = Now;
        double slide = _hide * (_m.EdgeGap + _m.PlateThickness + 6);
        double iconBase = _m.EdgeGap + _m.Pad - slide;
        _iconBase = iconBase;
        double dot = _m.DotSize;
        double dotV = _m.EdgeGap + (_m.Pad - dot) / 2 - slide;

        for (int i = 0; i < _entries.Count; i++)
        {
            var item = _entries[i];
            double start = _layout.Starts[i], size = _layout.Sizes[i];
            var view = _views[item];
            if (item.Kind == DockItemKind.Separator)
            {
                // A crisp one-pixel line across most of the plate height.
                double x = Math.Round((start + size / 2) * _scale) / _scale;
                double inset = _m.Pad * 0.4;
                view.Arrange(ToWindow(x, _m.EdgeGap + inset - slide, 1 / _scale, _m.PlateThickness - 2 * inset),
                    Rect.Empty, item.Presence);
                continue;
            }
            if (_dragging && item == _dragItem)
            {
                double sz = _m.IconSize;
                double maxV = _thickness - sz - (Edge == DockEdge.Bottom ? 34 : 0); // keep the icon and its label in the window
                _dragRect = ToWindow(_dragU - _dragFracU * sz, Math.Min(_dragV - _dragFracV * sz, maxV), sz, sz);
                view.Arrange(_dragRect, new Rect(0, 0, 0, 0), 1);
                continue;
            }
            double lift = item.Bounce.Offset(now) * _m.IconSize;
            view.Arrange(
                ToWindow(start, iconBase + lift, size, size),
                ToWindow(start + (size - dot) / 2, dotV, dot, dot),
                item.Presence);
        }

        UpdatePlate(slide);
        UpdateHitZone();
        UpdateLabel(iconBase);
    }

    void UpdatePlate(double slide)
    {
        double v0 = Math.Max(0, _m.EdgeGap - slide);
        double v1 = _m.EdgeGap + _m.PlateThickness - slide;
        bool visible = !_fullscreen && v1 - v0 > 0.5;
        if (visible)
        {
            var r = ToWindow(_layout.PlateStart, v0, _layout.PlateEnd - _layout.PlateStart, v1 - v0);
            _backdrop.SetBounds(new RECT(
                _windowPx.Left + (int)Math.Round(r.Left * _scale),
                _windowPx.Top + (int)Math.Round(r.Top * _scale),
                _windowPx.Left + (int)Math.Round(r.Right * _scale),
                _windowPx.Top + (int)Math.Round(r.Bottom * _scale)));
        }
        _backdrop.SetVisible(visible);
    }

    void UpdateHitZone()
    {
        if (_hide > 0.99)
        {
            // Hidden: a thin strip on the screen edge brings the dock back.
            _zone = (_layout.RestPlateStart, _layout.RestPlateEnd, 0, 2);
        }
        else
        {
            double top = _m.EdgeGap + _m.PlateThickness;
            if (_hovered || _magnify > 0.001)
            {
                double tallest = _layout.Sizes.Length > 0 ? _layout.Sizes.Max() : _m.IconSize;
                top = Math.Max(top, _m.EdgeGap + _m.Pad + tallest + 6);
            }
            _zone = (_layout.PlateStart, _layout.PlateEnd, 0, top);
        }

        var r = ToWindow(_zone.U0, _zone.V0, _zone.U1 - _zone.U0, _zone.V1 - _zone.V0);
        Canvas.SetLeft(_hitArea, r.X);
        Canvas.SetTop(_hitArea, r.Y);
        _hitArea.Width = Math.Max(0, r.Width);
        _hitArea.Height = Math.Max(0, r.Height);
    }

    void UpdateLabel(double iconBase)
    {
        if (_dragging)
        {
            if (!_dragRemove)
            {
                _label.Visibility = Visibility.Collapsed;
                return;
            }
            _labelText.Tag = null;
            _labelText.Text = Loc.T("Remove");
            _label.Visibility = Visibility.Visible;
            _label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var need = _label.DesiredSize;
            Canvas.SetLeft(_label, Math.Round(Math.Clamp(_dragRect.X + _dragRect.Width / 2 - need.Width / 2, 0, Math.Max(0, _canvas.Width - need.Width))));
            Canvas.SetTop(_label, Math.Round(Math.Max(0, _dragRect.Y - need.Height - 6)));
            return;
        }
        int i = _hoverIndex;
        if (i < 0 || _hide > 0.02 || _menuOpen || _entries[i].Bounce.IsActive)
        {
            _label.Visibility = Visibility.Collapsed;
            return;
        }

        var item = _entries[i];
        if (!ReferenceEquals(_labelText.Tag, item))
        {
            _labelText.Tag = item;
            _labelText.Text = item.Name;
        }
        _label.Visibility = Visibility.Visible;
        _label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _label.DesiredSize;

        double center = _layout.Starts[i] + _layout.Sizes[i] / 2;
        double beyond = iconBase + _layout.Sizes[i] + 10;
        double x, y;
        switch (Edge)
        {
            case DockEdge.Left:
                x = beyond;
                y = center - size.Height / 2;
                break;
            case DockEdge.Right:
                x = _thickness - beyond - size.Width;
                y = center - size.Height / 2;
                break;
            default:
                x = Math.Clamp(center - size.Width / 2, 4, Math.Max(4, _length - size.Width - 4));
                y = _thickness - beyond - size.Height;
                break;
        }
        Canvas.SetLeft(_label, Math.Round(x));
        Canvas.SetTop(_label, Math.Round(y));
    }

    /// <summary>Dock space (u along the edge, v away from it) to window coordinates.</summary>
    Rect ToWindow(double u, double v, double lengthU, double lengthV) => Edge switch
    {
        DockEdge.Left => new Rect(v, u, lengthV, lengthU),
        DockEdge.Right => new Rect(_thickness - v - lengthV, u, lengthV, lengthU),
        _ => new Rect(u, _thickness - v - lengthV, lengthU, lengthV),
    };

    (double U, double V) ToDock(Point p) => Edge switch
    {
        DockEdge.Left => (p.Y, p.X),
        DockEdge.Right => (p.Y, _thickness - p.X),
        _ => (p.X, _thickness - p.Y),
    };

    bool InZone(double u, double v) =>
        u >= _zone.U0 && u <= _zone.U1 && v >= _zone.V0 - 1 && v <= _zone.V1;

    /// <summary>The app icon under the pointer, if any.</summary>
    DockItem? AppAt(MouseEventArgs e)
    {
        var (u, v) = ToDock(e.GetPosition(_canvas));
        if (!InZone(u, v) || _hide > 0.02) return null;
        int i = _layout.ItemAt(u);
        return i >= 0 && _entries[i].Kind == DockItemKind.App && _entries[i].TargetPresence > 0 ? _entries[i] : null;
    }

    // ───────────────────────── Input ─────────────────────────

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var pos = e.GetPosition(_canvas);
        var (u, v) = ToDock(pos);
        if (_pressed is { Pinned: true, IsStart: false } && e.LeftButton == MouseButtonState.Pressed)
        {
            if (!_dragging && (Math.Abs(pos.X - _pressPos.X) >= 5 || Math.Abs(pos.Y - _pressPos.Y) >= 5)) BeginDrag(u, v);
            if (_dragging)
            {
                UpdateDrag(u, v);
                return;
            }
        }
        if (!InZone(u, v) && _pressed is null)
        {
            SetHovered(false);
            return;
        }

        SetHovered(true);
        if (_hide < 0.99) _pointer = u;
        // The mouse reports far more often than the screen refreshes: lay out once per frame.
        EnsureAnimating();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (_pressed is null) SetHovered(false);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var item = AppAt(e);
        if (item is null) return;

        // The dock never takes focus, so this is still the app the user was working in.
        _foregroundAtPress = GetForegroundWindow();
        _startOpenAtPress = item.IsStart && StartMenu.IsOpen(_foregroundAtPress);
        _pressed = item;
        _pressPos = e.GetPosition(_canvas);
        _views[item].SetPressed(true);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_pressed is null) return;

        var pressed = _pressed;
        _pressed = null;
        if (_views.TryGetValue(pressed, out var view)) view.SetPressed(false);
        ReleaseMouseCapture();

        if (_dragging) FinishDrag(pressed, e.GetPosition(_canvas));
        else if (AppAt(e) == pressed) OnItemClicked(pressed);
        else SetHovered(false);
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        // Middle click opens a new window / instance, as on the taskbar.
        if (e.ChangedButton == MouseButton.Middle && AppAt(e) is { } item)
        {
            Launch(item);
            e.Handled = true;
        }
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        _foregroundAtPress = GetForegroundWindow();
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        var (u, v) = ToDock(e.GetPosition(_canvas));
        if (!InZone(u, v) || _hide > 0.02) return;

        var item = AppAt(e);
        if (item is { IsStart: true })
        {
            // As on the Windows Start button.
            StartMenu.ShowQuickLinkMenu();
            e.Handled = true;
            return;
        }

        var menu = item is not null ? BuildItemMenu(item) : BuildDockMenu();
        menu.Closed += () =>
        {
            _menuOpen = false;
            if (!IsMouseOver) SetHovered(false);
            EnsureAnimating();
        };
        _menuOpen = true;
        menu.Show(this, _hwnd, _foregroundAtPress);
        EnsureAnimating();
        e.Handled = true;
    }

    // ───────────────────────── Editing the dock ─────────────────────────

    void BeginDrag(double u, double v)
    {
        var item = _pressed!;
        int i = _entries.IndexOf(item);
        if (i < 0) return;
        double size = Math.Max(1, _layout.Sizes[i]);
        _dragFracU = Math.Clamp((u - _layout.Starts[i]) / size, 0, 1);
        _dragFracV = Math.Clamp((v - _iconBase) / size, 0, 1);
        _dragItem = item;
        _dragging = true;
        _dragRemove = false;
        _views[item].SetPressed(false);
        _views[item].SetTopmost(true);
        EnsureAnimating();
    }

    void UpdateDrag(double u, double v)
    {
        var item = _dragItem!;
        _dragU = u;
        _dragV = v;
        _pointer = u;
        bool remove = v > _m.EdgeGap + _m.PlateThickness + _m.IconSize * 0.6;
        if (remove != _dragRemove)
        {
            // Out of the dock its slot closes up; back in, it opens again.
            _dragRemove = remove;
            item.TargetPresence = remove ? 0 : 1;
            EnsureAnimating();
        }
        if (!remove) MovePinnedTo(item, u);
        UpdateFrame();
    }

    void FinishDrag(DockItem item, Point pos)
    {
        bool remove = _dragRemove;
        _dragging = false;
        _dragRemove = false;
        _dragItem = null;
        if (_views.TryGetValue(item, out var view)) view.SetTopmost(false);
        item.TargetPresence = 1;
        if (remove)
        {
            Puff(new Point(_dragRect.X + _dragRect.Width / 2, _dragRect.Y + _dragRect.Height / 2));
            Unpin(item);
        }
        else
        {
            CommitPinnedOrder();
        }
        var (u, v) = ToDock(pos);
        if (!InZone(u, v)) SetHovered(false);
        UpdateFrame();
        EnsureAnimating();
    }

    List<DockItem> ListOf(DockItem item) => _folders.Contains(item) ? _folders : _pinned;

    /// <summary>Moves <paramref name="item"/> to the slot under <paramref name="u"/> within its own section. The Start button stays first.</summary>
    void MovePinnedTo(DockItem item, double u)
    {
        var list = ListOf(item);
        int from = list.IndexOf(item);
        if (from < 0) return;
        int first = list == _pinned ? _pinned.TakeWhile(p => p.IsStart).Count() : 0;
        int last = list.Count - 1;
        int i = _layout.ItemAt(u);
        int to;
        if (i >= 0 && list.IndexOf(_entries[i]) is >= 0 and var index) to = index;
        else if (i >= 0) to = i > _entries.IndexOf(item) ? last : first; // over another section
        else to = u < (_layout.PlateStart + _layout.PlateEnd) / 2 ? first : last;
        to = Math.Clamp(to, first, last);
        if (to == from) return;
        list.RemoveAt(from);
        list.Insert(to, item);
        RebuildEntries();
    }

    /// <summary>Writes the pinned items, in their current order, to config.json.</summary>
    void CommitPinnedOrder()
    {
        _config.Items.Clear();
        _config.Items.AddRange(_pinned.Where(p => !p.IsGhost).Select(p => p.Config));
        _config.Folders.Clear();
        _config.Folders.AddRange(_folders.Where(p => !p.IsGhost).Select(p => p.Config));
        _config.Save();
    }

    /// <summary>The little cloud that replaces an icon dragged off the dock.</summary>
    void Puff(Point at)
    {
        double size = _m.IconSize;
        const int puffs = 7;
        var rng = new Random();
        for (int n = 0; n < puffs; n++)
        {
            double angle = (n + rng.NextDouble() * 0.6) * 2 * Math.PI / puffs;
            double diameter = size * (0.4 + rng.NextDouble() * 0.2);
            var move = new TranslateTransform();
            var grow = new ScaleTransform(0.5, 0.5, diameter / 2, diameter / 2);
            var cloud = new Ellipse
            {
                Width = diameter,
                Height = diameter,
                IsHitTestVisible = false,
                Fill = new RadialGradientBrush(Color.FromArgb(0xE6, 0xF4, 0xF4, 0xF6), Color.FromArgb(0x00, 0xF4, 0xF4, 0xF6)),
                RenderTransform = new TransformGroup { Children = { grow, move } },
            };
            Canvas.SetLeft(cloud, at.X - diameter / 2);
            Canvas.SetTop(cloud, at.Y - diameter / 2);
            Panel.SetZIndex(cloud, 900);
            _canvas.Children.Add(cloud);

            var ease = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(380 + rng.Next(120));
            move.BeginAnimation(TranslateTransform.XProperty, new System.Windows.Media.Animation.DoubleAnimation(0, Math.Cos(angle) * size * 0.6, duration) { EasingFunction = ease });
            move.BeginAnimation(TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(0, Math.Sin(angle) * size * 0.6, duration) { EasingFunction = ease });
            grow.BeginAnimation(ScaleTransform.ScaleXProperty, new System.Windows.Media.Animation.DoubleAnimation(0.5, 1.6, duration) { EasingFunction = ease });
            grow.BeginAnimation(ScaleTransform.ScaleYProperty, new System.Windows.Media.Animation.DoubleAnimation(0.5, 1.6, duration) { EasingFunction = ease });
            var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, duration);
            fade.Completed += (_, _) => _canvas.Children.Remove(cloud);
            cloud.BeginAnimation(OpacityProperty, fade);
        }
    }

    // Dropping files, shortcuts and folders on the dock pins them.

    static readonly string[] DroppableExtensions = [".exe", ".lnk", ".appref-ms", ".bat", ".cmd", ".url"];

    const string ShellIdListFormat = "Shell IDList Array";

    static List<string> DroppedPaths(IDataObject data)
    {
        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
            return files.Where(f => Directory.Exists(f)
                                    || DroppableExtensions.Contains(System.IO.Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).ToList();
        // This PC, Control Panel, Network…: shell items with no file path, only an ID list.
        if (data.GetDataPresent(ShellIdListFormat) && data.GetData(ShellIdListFormat) is MemoryStream stream)
            return ShellItems.FromIdListArray(stream.ToArray()).Where(ShellItems.IsVirtual).ToList();
        return [];
    }

    bool IsPinnedTarget(string path)
    {
        var identity = AppIdentity.FromTarget(path);
        return _pinned.Concat(_folders).Any(p => !p.IsGhost
            && (string.Equals(p.Target, path, StringComparison.OrdinalIgnoreCase) || (identity.Key is not null && p.Identity.Matches(identity))));
    }

    protected override void OnDragEnter(DragEventArgs e)
    {
        base.OnDragEnter(e);
        HandleDragOver(e);
    }

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        HandleDragOver(e);
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        SetTrashDropHover(false);
        DropGhostAway();
    }

    static List<string> AllDroppedPaths(IDataObject data) =>
        data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files ? [.. files] : [];

    /// <summary>Whether the pointer of a drag is over the Recycle Bin.</summary>
    bool OverTrash(DragEventArgs e)
    {
        var (u, v) = ToDock(e.GetPosition(_canvas));
        if (!InZone(u, v)) return false;
        int i = _layout.ItemAt(u);
        return i >= 0 && _entries[i].IsTrash;
    }

    void SetTrashDropHover(bool hover)
    {
        if (_trashDropHover == hover) return;
        _trashDropHover = hover;
        if (_views.TryGetValue(_trash, out var view)) view.SetPressed(hover);
    }

    void HandleDragOver(DragEventArgs e)
    {
        e.Handled = true;
        if (AllDroppedPaths(e.Data).Count > 0 && OverTrash(e))
        {
            // Files dropped on the Recycle Bin are deleted (recoverably).
            e.Effects = e.AllowedEffects.HasFlag(DragDropEffects.Move) ? DragDropEffects.Move : DragDropEffects.Link;
            SetTrashDropHover(true);
            DropGhostAway();
            return;
        }
        SetTrashDropHover(false);

        var paths = DroppedPaths(e.Data);
        if (paths.Count == 0 || IsPinnedTarget(paths[0]))
        {
            e.Effects = DragDropEffects.None;
            DropGhostAway();
            return;
        }
        e.Effects = DragDropEffects.Link;
        if (_dropGhost is null)
        {
            _dropGhost = NewPinned(paths[0], ghost: true);
            (Directory.Exists(paths[0]) ? _folders : _pinned).Add(_dropGhost);
            RebuildEntries();
        }
        MovePinnedTo(_dropGhost, ToDock(e.GetPosition(_canvas)).U);
        UpdateFrame();
        EnsureAnimating();
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        e.Handled = true;
        if (_trashDropHover)
        {
            SetTrashDropHover(false);
            RecycleBin.Recycle(AllDroppedPaths(e.Data), _hwnd);
            CheckTrash();
            e.Effects = DragDropEffects.Link; // done: the source has nothing left to clean up
            return;
        }

        var paths = DroppedPaths(e.Data).Where(p => !IsPinnedTarget(p)).ToList();
        var ghost = _dropGhost;
        _dropGhost = null;
        if (paths.Count == 0)
        {
            if (ghost is not null) ghost.TargetPresence = 0;
            EnsureAnimating();
            return;
        }

        if (ghost is not null)
        {
            ghost.IsGhost = false;
        }
        else
        {
            ghost = NewPinned(paths[0], ghost: false);
            (Directory.Exists(paths[0]) ? _folders : _pinned).Add(ghost);
        }
        var list = ListOf(ghost);
        int at = list.IndexOf(ghost);
        foreach (var path in paths.Skip(1))
        {
            var other = NewPinned(path, ghost: false);
            if (Directory.Exists(path) == (list == _folders)) list.Insert(++at, other);
            else (Directory.Exists(path) ? _folders : _pinned).Add(other);
        }
        CommitPinnedOrder();
        RebuildEntries();
        SyncWindows();
        e.Effects = DragDropEffects.Link;
    }

    void DropGhostAway()
    {
        if (_dropGhost is null) return;
        _dropGhost.TargetPresence = 0;
        _dropGhost = null;
        EnsureAnimating();
    }

    /// <summary>A pinned item for a dropped file; it grows in like an app that just opened.</summary>
    static DockItem NewPinned(string path, bool ghost) =>
        new(new DockItemConfig { Target = path }) { IsGhost = ghost, Presence = 0, TargetPresence = 1 };

    /// <summary>
    /// Not running: launch. Running in the background: bring its last used window forward.
    /// Already in front: minimize it, or with several windows cycle to the next one.
    /// </summary>
    void OnItemClicked(DockItem item)
    {
        if (item.IsStart)
        {
            // Start may already have closed itself on the press; toggling then would reopen it.
            if (!_startOpenAtPress || StartMenu.IsOpen(GetForegroundWindow())) StartMenu.Toggle();
            return;
        }
        if (item.IsTrash)
        {
            Launcher.Launch(item);
            return;
        }
        if (item.IsFolder)
        {
            ToggleStack(item);
            return;
        }
        if (!item.IsRunning || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            Launch(item);
            return;
        }

        var windows = item.Windows; // most recently used first
        bool inFront = windows.Any(w => w.Handle == _foregroundAtPress);
        if (!inFront) ActivateWindow(windows[0].Handle);
        else if (windows.Count == 1) MinimizeWindow(windows[0].Handle);
        else ActivateWindow(windows[^1].Handle);
    }

    void Launch(DockItem item)
    {
        if (item.IsStart)
        {
            StartMenu.Toggle();
            return;
        }
        if (string.IsNullOrEmpty(item.Target)) return;
        Launcher.Launch(item);
        if (!item.IsRunning) item.Bounce.Start(BounceKind.Launch, Now);
        EnsureAnimating();
    }

    DockMenu BuildItemMenu(DockItem item)
    {
        var menu = new DockMenu();
        if (item.IsTrash)
        {
            menu.Add(Loc.T("Open"), () => Launcher.Launch(item));
            menu.Add(Loc.T("Empty Recycle Bin"), () =>
            {
                RecycleBin.Empty(_hwnd);
                CheckTrash();
            }, enabled: !_trashEmpty);
            return menu;
        }
        if (item.IsFolder)
        {
            menu.Add(Loc.T("Open"), () => Launcher.Launch(item));
            menu.Add(Loc.T("Show in File Explorer"), () => Reveal(Environment.ExpandEnvironmentVariables(item.Target)));
            menu.AddSeparator();
            menu.Add(Loc.T("Remove from Dock"), () => Unpin(item));
            return menu;
        }
        if (!item.IsStart)
        {
            var options = menu.AddSubmenu(Loc.T("Options"));
            bool canPin = item.Pinned || !string.IsNullOrEmpty(item.Target);
            options.Add(Loc.T("Keep in Dock"), () => { if (item.Pinned) Unpin(item); else Pin(item); },
                isChecked: item.Pinned, enabled: canPin);
            if (item.Pinned)
            {
                bool atStart = item.Config.OpenAtStart;
                options.Add(Loc.T("Open at Login"), () =>
                {
                    item.Config.OpenAtStart = !atStart;
                    _config.Save();
                }, isChecked: atStart);
            }
            var revealPath = RevealPathOf(item);
            options.Add(Loc.T("Show in File Explorer"), () => Reveal(revealPath!), enabled: revealPath is not null);
            menu.AddSeparator();
        }

        var recent = item.IsStart ? [] : JumpList.Recent(item.Identity.Aumid);
        if (recent.Count > 0)
        {
            menu.AddHeader(Loc.T("Recent"));
            foreach (var doc in recent)
            {
                var path = doc.Path;
                menu.Add(doc.Name.Length > 50 ? doc.Name[..47] + "…" : doc.Name, () => JumpList.Open(path));
            }
            menu.AddSeparator();
        }

        if (item.Windows.Count > 0)
        {
            foreach (var window in item.Windows)
            {
                window.Title = GetWindowTitle(window.Handle); // titles are not followed live
                var title = window.Title.Length > 60 ? window.Title[..57] + "…" : window.Title;
                var handle = window.Handle;
                menu.Add(title.Length > 0 ? title : item.Name, () => ActivateWindow(handle),
                    isChecked: handle == _foregroundAtPress);
            }
            menu.AddSeparator();
        }

        if (!string.IsNullOrEmpty(item.Target))
            menu.Add(item.IsRunning ? Loc.T("New Window") : Loc.T("Open"), () => Launch(item));
        if (Launcher.CanRunAsAdmin(item))
            menu.Add(item.IsRunning ? Loc.T("New Window as Administrator") : Loc.T("Open as Administrator"), () =>
            {
                // WPF runs a menu command after the menu has closed, when the focus has gone back to the
                // app in front: take it again (the click was ours) or UAC hides its prompt in the taskbar.
                SetForegroundWindow(_hwnd);
                Launcher.LaunchAsAdmin(item, _hwnd);
            });
        if (item.IsRunning)
        {
            var handles = item.Windows.Select(w => w.Handle).ToList();
            menu.Add(Loc.T("Show All Windows"), () =>
            {
                // Least recently used first, so the last used ends on top.
                for (int i = handles.Count - 1; i >= 0; i--) WindowActions.Activate(handles[i]);
            });
            menu.Add(Loc.T("Hide"), () => handles.ForEach(WindowActions.Minimize));
            menu.AddSeparator();
            menu.Add(Loc.T("Quit"), () => handles.ForEach(WindowActions.Close));
        }
        return menu;
    }

    /// <summary>The file "Show in Explorer" selects: the shortcut or program of the item, if it is a file.</summary>
    static string? RevealPathOf(DockItem item)
    {
        var target = Environment.ExpandEnvironmentVariables(item.Target);
        if (File.Exists(target) || Directory.Exists(target)) return target;
        var exe = item.Identity.ExePath;
        return exe is not null && File.Exists(exe) ? exe : null;
    }

    static void Reveal(string path)
    {
        try
        {
            var arg = Directory.Exists(path) ? $"\"{path}\"" : $"/select,\"{path}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", arg) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Show in Explorer for {path}");
        }
    }

    // ───────────────────────── Stacks, trash, Win+number ─────────────────────────

    void ToggleStack(DockItem item)
    {
        bool wasOpen = _stack?.Item == item;
        // A click outside closes the stack on the press; that same click on its icon must not reopen it.
        bool justClosed = _stackClosedItem == item && Environment.TickCount64 - _stackClosedAt < 400;
        _stack?.Close();
        if (wasOpen || justClosed) return;

        int index = _entries.IndexOf(item);
        if (index < 0) return;
        var r = ToWindow(_layout.Starts[index], _iconBase, _layout.Sizes[index], _layout.Sizes[index]);
        var anchor = new RECT(
            _windowPx.Left + (int)Math.Round(r.Left * _scale), _windowPx.Top + (int)Math.Round(r.Top * _scale),
            _windowPx.Left + (int)Math.Round(r.Right * _scale), _windowPx.Top + (int)Math.Round(r.Bottom * _scale));
        var stack = new StackWindow(item, anchor, _scale, Edge, byOutsideClick =>
        {
            if (byOutsideClick)
            {
                _stackClosedItem = item;
                _stackClosedAt = Environment.TickCount64;
            }
            if (_stack?.Item == item) _stack = null;
        });
        _stack = stack;
        stack.Show();
    }

    /// <summary>
    /// The file system says when the bin's folders change. Asking the bin itself looks at every drive
    /// (several ms, and it keeps sleeping disks awake), so that is only done on those changes, when the
    /// pointer comes to the dock, and once a minute as a safety net (removable drives, new drives).
    /// </summary>
    void StartTrashWatch()
    {
        _trashWatch = RecycleBin.WatchChanges(() => Dispatcher.BeginInvoke(() =>
        {
            // A delete or an emptying is a burst of changes: look once it is over.
            if (TrashDiagnostics && !_trashDebounce.IsEnabled) Log.Info("Recycle Bin: change on disk");
            _trashDebounce.Stop();
            _trashDebounce.Start();
        }));
        if (_trashWatch.Drives == 0) Log.Info("Recycle Bin: folders cannot be watched, polling instead");
        _trashTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_trashWatch.Drives > 0 ? 60 : 5) };
        _trashTimer.Tick += (_, _) => CheckTrash();
        _trashTimer.Start();
        CheckTrash();
    }

    /// <summary>Full or empty bin icon, following what is in it. The bin is read on a worker thread.</summary>
    void CheckTrash()
    {
        if (_trashChecking)
        {
            _trashRecheck = true;
            return;
        }
        _trashChecking = true;
        _trashCheckedAt = Environment.TickCount64;
        Task.Run(() => RecycleBin.IsEmpty).ContinueWith(t => Dispatcher.BeginInvoke(() =>
        {
            _trashChecking = false;
            if (t.IsCompletedSuccessfully && t.Result != _trashEmpty)
            {
                _trashEmpty = t.Result;
                _trash.Icon = ShellItems.Icon(RecycleBin.ShellPath) ?? _trash.Icon;
                if (_views.TryGetValue(_trash, out var view)) view.Refresh();
                if (TrashDiagnostics) Log.Info($"Recycle Bin: {(_trashEmpty ? "empty" : "full")}");
            }
            if (_trashRecheck)
            {
                _trashRecheck = false;
                CheckTrash();
            }
        }));
    }

    static bool TrashDiagnostics => WindowTracker.Diagnostics;

    /// <summary>Win+1 … Win+9: the n-th app of the dock, as on the taskbar.</summary>
    void OnNumberShortcut(int number)
    {
        var apps = _entries.Where(e => e.Kind == DockItemKind.App && !e.IsStart && !e.IsTrash && !e.IsFolder
                                       && e.TargetPresence > 0 && !e.IsGhost).ToList();
        if (number > apps.Count) return;
        _foregroundAtPress = GetForegroundWindow();
        OnItemClicked(apps[number - 1]);
    }

    SettingsWindow? _settingsWindow;

    void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow(_config.Settings, ApplySettings);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>Puts the settings into effect and saves them (called by the settings window after every change).</summary>
    void ApplySettings()
    {
        var settings = _config.Settings;
        settings.Clamp();
        _config.Save();
        Loc.Init(settings.Language);
        _m = BuildMetrics();

        if (_replacer is { } replacer && replacer.Enabled != settings.ReplaceTaskbar) replacer.SetEnabled(settings.ReplaceTaskbar);
        if (settings.MenuBar) ShowMenuBar();
        else HideMenuBar();
        _menuBars?.SetAllDisplays(settings.MenuBarOnAllDisplays);
        if (_chosenDisplay != settings.Display) _displayName = _chosenDisplay = settings.Display; // not on other changes: the dock may have followed the pointer
        UpdateFollowTimer();
        if (AutoStart.IsEnabled != settings.StartWithWindows) AutoStart.Set(settings.StartWithWindows);
        if (settings.NumberShortcuts && _numberShortcuts is null) _numberShortcuts = new NumberShortcuts(OnNumberShortcut);
        else if (!settings.NumberShortcuts)
        {
            _numberShortcuts?.Dispose();
            _numberShortcuts = null;
        }

        SetHideRequested(settings.AutoHide);
        Place();
        UpdateFrame();
        EnsureAnimating();
    }

    /// <summary>Launches, once, the pinned apps marked "Open at start" (only when Gravitone itself was started at sign-in).</summary>
    void LaunchOpenAtStartItems()
    {
        foreach (var item in _pinned.Where(p => p.Config.OpenAtStart && !p.IsStart && !p.IsRunning).ToList())
            Launch(item);
    }

    /// <summary>Unpins a dock item. Still running, it drops after the separator like any other open app.</summary>
    void Unpin(DockItem item)
    {
        if (_folders.Remove(item))
        {
            _config.Folders.Remove(item.Config);
            _config.Save();
            RebuildEntries();
            return;
        }
        if (!_pinned.Remove(item)) return;
        _config.Items.Remove(item.Config);
        _config.Save();
        SyncWindows();
    }

    /// <summary>Pins a running-but-unpinned app at the end of the pinned apps.</summary>
    void Pin(DockItem item)
    {
        if (!_running.Remove(item)) return;
        _pinned.Add(new DockItem(new DockItemConfig { Name = item.Name, Target = item.Target }));
        CommitPinnedOrder();
        SyncWindows();
    }

    DockMenu BuildDockMenu()
    {
        var menu = new DockMenu();
        var replacer = _replacer!;
        menu.Add(Loc.T("{0} Settings…", AppInfo.Name), ShowSettings);
        menu.AddSeparator();
        menu.Add(Loc.T("Replace the Windows Taskbar"), () =>
        {
            _config.Settings.ReplaceTaskbar = !replacer.Enabled;
            _config.Save();
            replacer.SetEnabled(_config.Settings.ReplaceTaskbar);
        }, isChecked: replacer.Enabled);
        if (replacer.Enabled)
        {
            menu.Add(replacer.TemporarilyShown ? Loc.T("Hide the Windows Taskbar") : Loc.T("Show the Windows Taskbar"),
                replacer.ToggleTemporarilyShown, gesture: _taskbarHotkey?.Text);
        }
        menu.Add(Loc.T("Menu Bar"), () =>
        {
            _config.Settings.MenuBar = _menuBars is null;
            _config.Save();
            if (_config.Settings.MenuBar) ShowMenuBar();
            else HideMenuBar();
        }, isChecked: _menuBars is not null);
        bool autoStart = AutoStart.IsEnabled;
        menu.Add(Loc.T("Start with Windows"), () =>
        {
            _config.Settings.StartWithWindows = !autoStart;
            _config.Save();
            AutoStart.Set(!autoStart);
        }, isChecked: autoStart);
        menu.AddSeparator();
        menu.Add(Loc.T("Quit {0}", AppInfo.Name), () => Application.Current.Shutdown());
        return menu;
    }

    void SetHovered(bool hovered)
    {
        if (_hovered == hovered) return;
        _hovered = hovered;
        if (hovered)
        {
            _hideTimer.Stop();
            if (_hideRequested) _revealTimer.Start();
            // The bin icon is about to be looked at: make sure it is right (cheap, off the UI thread).
            if (Environment.TickCount64 - _trashCheckedAt > 10_000) CheckTrash();
            // A pause whose end was never reported (the pointer is here, so someone is using the PC).
            if (_pauseReasons.Count > 0)
            {
                Log.Info($"Pause never ended ({string.Join(", ", _pauseReasons)}): resuming");
                var reasons = _pauseReasons.ToList();
                foreach (var r in reasons.Skip(1)) _pauseReasons.Remove(r);
                Resume(reasons[0]);
            }
        }
        else
        {
            _revealTimer.Stop();
            _hoverIndex = -1;
            if (_config.Settings.AutoHide) _hideTimer.Start();
        }
        EnsureAnimating();
    }

    void SetHideRequested(bool hide)
    {
        if (_hideRequested == hide) return;
        _hideRequested = hide;
        EnsureAnimating();
    }

    // ───────────────────────── Monitors ─────────────────────────

    /// <summary>Monitors added, removed, rescaled or a new primary: follow the change (debounced, they come in bursts).</summary>
    void ScheduleDisplayCheck()
    {
        _displayCheck.Stop();
        _displayCheck.Start();
    }

    void CheckDisplays()
    {
        var layout = Displays.Layout();
        if (layout == _displayLayout) return;
        _displayLayout = layout;
        Log.Info($"Displays changed: {layout}");
        _appBar?.ForgetPosition();
        Place();
        UpdateFrame();
        _menuBars?.Sync();
        UpdateFollowTimer();
    }

    /// <summary>With more than one monitor, watches for the pointer pushing against the dock's edge of another one.</summary>
    void UpdateFollowTimer()
    {
        bool wanted = _config.Settings.FollowPointer && _pauseReasons.Count == 0 && Displays.All().Count > 1;
        if (wanted && !_followTimer.IsEnabled) _followTimer.Start();
        else if (!wanted) _followTimer.Stop();
        _edgeDwell = 0;
    }

    /// <summary>
    /// As on macOS: holding the pointer against the bottom (or side) edge of another monitor for a
    /// moment brings the dock there. Not while a button is down (dragging a window) or when a
    /// full-screen app is in front on that monitor.
    /// </summary>
    void FollowPointer()
    {
        if (!GetCursorPos(out var p) || _dragging || _menuOpen || _fullscreen) return;
        if (MonitorFromPoint(p, MONITOR_DEFAULTTONEAREST) == _placedMonitor
            || (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0 || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0)
        {
            _edgeDwell = 0;
            return;
        }
        var target = Displays.FromPoint(p);
        bool atEdge = Edge switch
        {
            DockEdge.Left => p.X <= target.Bounds.Left,
            DockEdge.Right => p.X >= target.Bounds.Right - 1,
            _ => p.Y >= target.Bounds.Bottom - 1,
        };
        // An edge shared with another monitor is a way through, not the end of the screen.
        if (!atEdge || target.HasNeighbourBeyond(Edge, p))
        {
            _edgeDwell = 0;
            return;
        }
        if (++_edgeDwell < 3) return; // about a third of a second against the edge
        _edgeDwell = 0;
        var foreground = GetForegroundWindow();
        if (foreground != IntPtr.Zero && GetWindowRect(foreground, out var r) && GetClassName(foreground) is not ("Progman" or "WorkerW")
            && r.Left <= target.Bounds.Left && r.Top <= target.Bounds.Top && r.Right >= target.Bounds.Right && r.Bottom >= target.Bounds.Bottom)
            return; // a full-screen game or video there
        _displayName = target.DeviceName;
        Place();
        SetHideRequested(false);
        if (_config.Settings.AutoHide) _hideTimer.Start(); // hides again unless the pointer goes into it
        UpdateFrame();
        EnsureAnimating();
    }

    // ───────────────────────── Sleep, lock, user switch ─────────────────────────

    /// <summary>
    /// Nobody sees the dock (asleep, locked, another user in front, screen off): stop the polling.
    /// The taskbar keeps being replaced: Windows may show it again at any time.
    /// </summary>
    void Pause(string reason)
    {
        if (!_pauseReasons.Add(reason) || _pauseReasons.Count > 1) return;
        Log.Info($"Paused ({reason})");
        _tracker?.SetPaused(true);
        _menuBars?.SetPaused(true);
        _trashTimer?.Stop();
        _followTimer.Stop();
        _stack?.Close();
        SetHovered(false);
    }

    void Resume(string reason)
    {
        if (!_pauseReasons.Remove(reason) || _pauseReasons.Count > 0) return;
        Log.Info($"Resumed ({reason})");
        _tracker?.SetPaused(false);
        _menuBars?.SetPaused(false);
        _trashTimer?.Start();
        Revalidate();
        _settleTimer.Stop();
        _settleTimer.Start();
    }

    /// <summary>Anything may have changed while away: monitors, theme, Explorer, the windows, the bin, the time.</summary>
    void Revalidate()
    {
        uint pid = TaskbarState.ExplorerProcessId();
        if (pid != 0 && pid != _explorerPid)
        {
            _explorerPid = pid;
            _replacer?.OnTaskbarCreated();
            _appBar?.Reregister();
        }
        Theme.Refresh();
        ApplyTheme();
        _tracker?.RefreshNow();
        SyncWindows();
        _displayLayout = Displays.Layout();
        _appBar?.ForgetPosition();
        Place();
        UpdateFrame();
        _backdrop.Reactivate();
        _menuBars?.Refresh();
        CheckTrash();
        UpdateFollowTimer();
    }

    void OnSessionChange(int change)
    {
        switch (change)
        {
            case WTS_SESSION_LOCK:
                Pause("lock");
                break;
            case WTS_SESSION_UNLOCK:
                Resume("lock");
                break;
            case WTS_CONSOLE_DISCONNECT:
            case WTS_REMOTE_DISCONNECT:
                Pause("user switch");
                break;
            case WTS_CONSOLE_CONNECT:
            case WTS_REMOTE_CONNECT:
                Resume("user switch");
                break;
        }
    }

    void OnPowerBroadcast(int kind, IntPtr data)
    {
        switch (kind)
        {
            case PBT_APMSUSPEND:
                Pause("sleep");
                break;
            case PBT_APMRESUMEAUTOMATIC:
            case PBT_APMRESUMESUSPEND:
                Resume("sleep");
                break;
            case PBT_POWERSETTINGCHANGE when data != IntPtr.Zero:
                // POWERBROADCAST_SETTING: the setting's GUID, the data length, then the data (0 off, 1 on, 2 dimmed).
                if (Marshal.PtrToStructure<Guid>(data) != GUID_CONSOLE_DISPLAY_STATE) break;
                int state = Marshal.ReadInt32(data, 20);
                if (state == 0) Pause("display off");
                else if (state == 1) Resume("display off");
                break;
        }
    }

    void StartDiagnostics()
    {
        LogDiagnostics();
        _diagTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _diagTimer.Tick += (_, _) => Log.Info($"RESOURCES {Diagnostics.Resources()} | events: {_tracker?.TakeEventCounts()}");
        _diagTimer.Start();
        Log.Info($"RESOURCES {Diagnostics.Resources()}");
    }

    // ───────────────────────── System events ─────────────────────────

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_MOUSEACTIVATE:
                handled = true;
                return MA_NOACTIVATE;
            case WM_SETTINGCHANGE:
                if (lParam != IntPtr.Zero && Marshal.PtrToStringUni(lParam) == "ImmersiveColorSet")
                {
                    Theme.Refresh();
                    ApplyTheme();
                }
                break;
            case WM_DISPLAYCHANGE:
                SchedulePlace();
                ScheduleDisplayCheck();
                break;
            case WM_DPICHANGED:
                ScheduleDisplayCheck(); // the scale of a monitor changed
                break;
            case WM_WTSSESSION_CHANGE:
                OnSessionChange((int)wParam);
                break;
            case WM_POWERBROADCAST:
                OnPowerBroadcast((int)wParam, lParam);
                break;
            case WM_TIMECHANGE:
                _menuBars?.UpdateClock();
                break;
            case WM_WINDOWPOSCHANGED:
                _appBar?.NotifyWindowPosChanged();
                break;
            case WM_HOTKEY when (int)wParam == TaskbarHotkeyId:
                _replacer?.ToggleTemporarilyShown();
                handled = true;
                return IntPtr.Zero;
            case WM_QUERYENDSESSION:
                // Every app is asked before any is closed, so Explorer is still there to take its
                // taskbar back. Waiting for WM_ENDSESSION can be too late: Explorer may be gone and
                // the taskbar would stay in auto-hide after the restart.
                Log.Info("Session ending: restoring the taskbar");
                _replacer?.SetSessionEnding(true);
                break;
            case WM_ENDSESSION when wParam == IntPtr.Zero:
                // The shutdown was cancelled (an app refused it).
                Log.Info("Session end cancelled");
                _replacer?.SetSessionEnding(false);
                break;
            case WM_ENDSESSION:
                // Signing out or shutting down: nothing after this is guaranteed to run.
                ReleaseShell();
                break;
        }

        if (_tracker?.HandleMinimizeRect(msg, wParam, lParam) is { } minimizeReply)
        {
            handled = true;
            return minimizeReply;
        }
        if (_tracker is not null && _tracker.HandleMessage(msg, wParam, lParam))
        {
            handled = true;
        }
        else if (msg == _taskbarCreatedMessage && _taskbarCreatedMessage != 0)
        {
            // Explorer restarted. The menu bar's tray service broadcasts this message too
            // (so apps re-add their icons); then Explorer and our app bar registration are unchanged.
            uint pid = TaskbarState.ExplorerProcessId();
            if (pid != 0 && pid != _explorerPid)
            {
                _explorerPid = pid;
                _replacer?.OnTaskbarCreated();
                _appBar?.Reregister();
                SchedulePlace();
            }
        }
        else if (msg == _quitMessage && _quitMessage != 0)
        {
            // "Gravitone.exe --quit" (installer, uninstaller): the same as "Quit" in the menu.
            Log.Info("Quit requested from the command line");
            Dispatcher.BeginInvoke(() => Application.Current.Shutdown());
            handled = true;
        }
        else if (WindowTracker.Diagnostics && msg == _genieTestMessage && _genieTestMessage != 0)
        {
            // Test hook (--diag only): wParam 0 minimizes the window in lParam as a dock click would, 1 restores it.
            if (wParam == IntPtr.Zero) MinimizeWindow(lParam);
            else ActivateWindow(lParam);
            handled = true;
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

    /// <summary>Like the Dock, step aside while a full-screen app (video, game, presentation) is in front.</summary>
    void OnFullscreenApp(bool fullscreen)
    {
        // Explorer tells every app bar, whatever monitor the app is on (and sometimes reports the desktop).
        if (fullscreen && !Displays.IsFullscreenAppOn(_hwnd)) return;
        if (_fullscreen == fullscreen) return;
        _fullscreen = fullscreen;

        if (fullscreen)
        {
            ShowWindow(_hwnd, SW_HIDE);
            _backdrop.SetVisible(false);
        }
        else
        {
            ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            UpdateFrame();
        }
    }

    void ApplyTheme()
    {
        bool dark = Theme.IsDark;
        _label.Background = new SolidColorBrush(dark ? Color.FromArgb(0xEB, 0x2B, 0x2B, 0x2D) : Color.FromArgb(0xF2, 0xEE, 0xEE, 0xEE));
        _label.BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1F, 0x00, 0x00, 0x00));
        _labelText.Foreground = new SolidColorBrush(dark ? Color.FromRgb(0xF5, 0xF5, 0xF7) : Color.FromRgb(0x1D, 0x1D, 0x1F));
        foreach (var view in _views.Values) view.Refresh();
        _backdrop.ApplyTheme();
        _menuBars?.ApplyTheme();
    }

    void LogDiagnostics()
    {
        foreach (var item in _pinned)
            Log.Info($"PINNED  {item.Name,-28} aumid={item.Identity.Aumid} exe={item.Identity.ExePath}");
        foreach (var window in _tracker!.Windows)
        {
            var owner = OwnerOf(window);
            Log.Info($"WINDOW  {window.Title,-40} aumid={window.Identity.Aumid} exe={window.Identity.ExePath} -> {owner?.Name ?? "(none)"}{(owner is { Pinned: false } ? " [not pinned]" : "")}");
        }
    }
}
