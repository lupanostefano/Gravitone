using System.Runtime.InteropServices;
using System.Windows.Threading;
using Gravitone.Interop;
using static Gravitone.Interop.NativeMethods;

namespace Gravitone.Core;

internal sealed class TrackedWindow(IntPtr handle, AppIdentity identity)
{
    public IntPtr Handle { get; } = handle;
    public AppIdentity Identity { get; } = identity;
    public string Title { get; set; } = "";
    /// <summary>Monotonic activation counter: higher = used more recently.</summary>
    public long LastActivated { get; set; }
}

/// <summary>
/// Keeps the list of windows Windows would show on its taskbar, live, using the shell hook
/// plus WinEvents (for cloaking and show/hide, which the shell hook does not report).
/// </summary>
internal sealed class WindowTracker : IDisposable
{
    static readonly HashSet<string> IgnoredClasses =
    [
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow",
    ];

    readonly IntPtr _hwnd;
    readonly uint _shellHookMessage;
    readonly uint _ownPid = (uint)Environment.ProcessId;
    readonly Dictionary<IntPtr, TrackedWindow> _windows = [];
    readonly DispatcherTimer _refreshTimer;
    readonly DispatcherTimer _safetyTimer;
    readonly List<IntPtr> _hooks = [];
    readonly WinEventProc _winEventProc; // kept alive: native code calls it
    long _activation;

    public WindowTracker(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _shellHookMessage = RegisterWindowMessage("SHELLHOOK");
        RegisterShellHookWindow(hwnd);

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            Refresh();
        };
        // Safety net for anything no notification reports.
        _safetyTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _safetyTimer.Tick += (_, _) => Refresh();
        _safetyTimer.Start();

        _winEventProc = OnWinEvent;
        // No EVENT_OBJECT_NAMECHANGE: every accessible element of every app raises it (a timer, a
        // progress text, a web page), each one a call into this thread. Window titles come with the
        // shell hook's HSHELL_REDRAW and every refresh, and are read again when a menu shows them.
        foreach (var (min, max) in new[]
                 {
                     (EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND),
                     (EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND),
                     (EVENT_OBJECT_SHOW, EVENT_OBJECT_HIDE),
                     (EVENT_OBJECT_CLOAKED, EVENT_OBJECT_UNCLOAKED),
                 })
        {
            var hook = SetWinEventHook(min, max, IntPtr.Zero, _winEventProc, 0, 0,
                WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            if (hook != IntPtr.Zero) _hooks.Add(hook);
        }

        Refresh();
        MarkActivated(GetForegroundWindow());
    }

    /// <summary>The window list changed (opened, closed, grouped differently).</summary>
    public event Action? Changed;

    /// <summary>A window asks for attention (it would flash on the taskbar).</summary>
    public event Action<TrackedWindow>? AttentionRequested;

    /// <summary>A tracked window became the foreground window.</summary>
    public event Action<TrackedWindow>? Activated;

    public IReadOnlyCollection<TrackedWindow> Windows => _windows.Values;

    /// <summary>Logs every shell notification (started with <c>--diag</c>).</summary>
    public static bool Diagnostics { get; } = Environment.GetCommandLineArgs().Contains("--diag");

    const int HSHELL_GETMINRECT = 5;

    /// <summary>Where a window minimizing to the dock should shrink to (screen pixels), or null to leave it to Windows.</summary>
    public Func<IntPtr, RECT?>? MinimizeTarget { get; set; }

    /// <summary>
    /// Windows asks the shell where a minimizing window should go (<c>HSHELL_GETMINRECT</c>): the reply
    /// is written into the message's <c>SHELLHOOKINFO</c> (window, then rectangle). Returns 1 when answered.
    /// </summary>
    public IntPtr? HandleMinimizeRect(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != _shellHookMessage || _shellHookMessage == 0 || (int)wParam != HSHELL_GETMINRECT || lParam == IntPtr.Zero) return null;
        try
        {
            var hwnd = Marshal.ReadIntPtr(lParam);
            if (MinimizeTarget?.Invoke(hwnd) is not RECT r) return null;
            int offset = IntPtr.Size;
            Marshal.WriteInt32(lParam, offset, r.Left);
            Marshal.WriteInt32(lParam, offset + 4, r.Top);
            Marshal.WriteInt32(lParam, offset + 8, r.Right);
            Marshal.WriteInt32(lParam, offset + 12, r.Bottom);
            return (IntPtr)1;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Minimize target");
            return null;
        }
    }

    /// <summary>Call from the owner window's WndProc; returns true when the message was ours.</summary>
    public bool HandleMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != _shellHookMessage || _shellHookMessage == 0) return false;
        if (Diagnostics) Log.Info($"SHELLHOOK 0x{(int)wParam:X} hwnd={lParam} tracked={_windows.ContainsKey(lParam)}");
        switch ((int)wParam)
        {
            case HSHELL_WINDOWACTIVATED:
            case HSHELL_RUDEAPPACTIVATED:
                MarkActivated(lParam);
                break;
            case HSHELL_FLASH:
                if (_windows.TryGetValue(lParam, out var flashing)) AttentionRequested?.Invoke(flashing);
                break;
            case HSHELL_REDRAW:
                UpdateTitle(lParam);
                break;
            default:
                ScheduleRefresh();
                break;
        }
        return true;
    }

    public void Dispose()
    {
        _refreshTimer.Stop();
        _safetyTimer.Stop();
        foreach (var hook in _hooks) UnhookWinEvent(hook);
        _hooks.Clear();
        DeregisterShellHookWindow(_hwnd);
    }

    /// <summary>Screen off or session locked: no safety-net polling (notifications still arrive).</summary>
    public void SetPaused(bool paused)
    {
        if (paused) _safetyTimer.Stop();
        else if (!_safetyTimer.IsEnabled) _safetyTimer.Start();
    }

    /// <summary>Reads the window list again now (back from sleep, lock, a user switch).</summary>
    public void RefreshNow() => Refresh();

    /// <summary>With <c>--diag</c>: how many notifications arrived, by kind, since the last call.</summary>
    public string TakeEventCounts()
    {
        var text = string.Join(", ", _eventCounts.OrderByDescending(kv => kv.Value).Select(kv => $"0x{kv.Key:X}={kv.Value}"));
        _eventCounts.Clear();
        return text.Length > 0 ? text : "nessuno";
    }

    readonly Dictionary<uint, int> _eventCounts = [];

    void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (Diagnostics) _eventCounts[eventType] = _eventCounts.GetValueOrDefault(eventType) + 1;
        if (idObject != OBJID_WINDOW || idChild != 0 || hwnd == IntPtr.Zero) return;
        switch (eventType)
        {
            case EVENT_SYSTEM_FOREGROUND:
                MarkActivated(hwnd);
                break;
            default:
                // Only top-level windows can change the taskbar list.
                if (_windows.ContainsKey(hwnd) || GetAncestor(hwnd, GA_ROOT) == hwnd) ScheduleRefresh();
                break;
        }
    }

    void ScheduleRefresh()
    {
        if (!_refreshTimer.IsEnabled) _refreshTimer.Start();
    }

    void MarkActivated(IntPtr hwnd)
    {
        if (!_windows.TryGetValue(hwnd, out var window))
        {
            // Possibly a window we have not seen yet (just created): pick it up first.
            Refresh();
            if (!_windows.TryGetValue(hwnd, out window)) return;
        }
        window.LastActivated = ++_activation;
        Activated?.Invoke(window);
    }

    void UpdateTitle(IntPtr hwnd)
    {
        if (_windows.TryGetValue(hwnd, out var window)) window.Title = GetWindowTitle(hwnd);
    }

    void Refresh()
    {
        var seen = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            if (IsTaskbarWindow(hwnd)) seen.Add(hwnd);
            return true;
        }, IntPtr.Zero);

        bool changed = false;
        var alive = new HashSet<IntPtr>(seen);
        foreach (var gone in _windows.Keys.Where(h => !alive.Contains(h)).ToList())
        {
            _windows.Remove(gone);
            changed = true;
        }

        // EnumWindows walks top to bottom of the z-order: give unseen windows an activation
        // order that matches it, so "most recently used" is right from the first frame.
        for (int i = seen.Count - 1; i >= 0; i--)
        {
            var hwnd = seen[i];
            if (_windows.TryGetValue(hwnd, out var existing))
            {
                existing.Title = GetWindowTitle(hwnd);
                continue;
            }
            var window = new TrackedWindow(hwnd, AppIdentity.FromWindow(hwnd, GetClassName(hwnd)))
            {
                Title = GetWindowTitle(hwnd),
                LastActivated = ++_activation,
            };
            _windows.Add(hwnd, window);
            changed = true;
        }

        if (changed) Changed?.Invoke();
    }

    /// <summary>The taskbar's rules: visible, not cloaked, unowned, not a tool window — unless it opts in with WS_EX_APPWINDOW.</summary>
    bool IsTaskbarWindow(IntPtr hwnd)
    {
        if (hwnd == _hwnd || !IsWindowVisible(hwnd)) return false;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == _ownPid) return false;

        long ex = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        bool appWindow = (ex & WS_EX_APPWINDOW) != 0;
        if (!appWindow)
        {
            if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return false;
            if ((ex & (WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE)) != 0) return false;
            if (GetWindowTextLength(hwnd) == 0) return false;
        }
        if (IsCloaked(hwnd)) return false;
        return !IgnoredClasses.Contains(GetClassName(hwnd));
    }
}
