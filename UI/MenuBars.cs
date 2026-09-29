using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using Gravitone.Core;
using static Gravitone.Interop.NativeMethods;

namespace Gravitone.UI;

/// <summary>
/// The menu bars: one on the primary monitor (with the tray icons) and, as on macOS, one on every
/// other monitor. What they show is shared: the app in front, Wi-Fi / volume / battery, the clock.
/// </summary>
internal sealed class MenuBars : IDisposable
{
    static string DesktopName => Loc.T("File Explorer"); // the desktop belongs to the file manager, as on macOS

    // Shell surfaces that come and go in front (Start, search, flyouts): the app name stays put.
    static readonly HashSet<string> TransientProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "StartMenuExperienceHost", "SearchHost", "SearchApp", "ShellExperienceHost", "ShellHost", "LockApp",
    };

    readonly Func<IntPtr, string?> _dockNameFor;
    readonly SystemStatus _status = new();
    readonly DispatcherTimer _clockTimer = new();
    readonly WinEventProc _winEventProc; // kept alive: native code calls it
    readonly Dictionary<string, string> _nameCache = [];
    readonly List<MenuBarWindow> _bars = [];
    IntPtr _foregroundHook;
    bool _allDisplays;
    string _barsKey = "";
    bool _disposed;

    /// <param name="dockNameFor">The dock's name for the app owning a window, when the dock shows it.</param>
    public MenuBars(Func<IntPtr, string?> dockNameFor, bool allDisplays)
    {
        _dockNameFor = dockNameFor;
        _allDisplays = allDisplays;
        _status.Changed += () => _bars.ForEach(b => b.ShowStatus(_status));
        _clockTimer.Tick += (_, _) => UpdateClock();
        _winEventProc = (_, _, hwnd, idObject, _, _, _) =>
        {
            if (idObject == OBJID_WINDOW) UpdateActiveApp(hwnd);
        };
        _foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
            _winEventProc, 0, 0, WINEVENT_OUTOFCONTEXT);
        AppName = "";
        UpdateActiveApp(GetForegroundWindow());
        Sync();
        UpdateClock();
    }

    public string AppName { get; private set; }
    public string ClockText { get; private set; } = "";
    public string ClockTip { get; private set; } = "";

    public void SetAllDisplays(bool all)
    {
        if (_allDisplays == all) return;
        _allDisplays = all;
        Sync();
    }

    /// <summary>
    /// One bar per wanted monitor. Bars are only recreated when the monitors or the primary change
    /// (the tray icons must follow the primary); otherwise they are just placed again.
    /// </summary>
    public void Sync()
    {
        if (_disposed) return;
        var wanted = _allDisplays ? Displays.All() : [Displays.Primary()];
        var key = string.Join(";", wanted.Select(d => d.DeviceName + (d.IsPrimary ? "*" : "")));
        if (key == _barsKey && _bars.Count > 0)
        {
            foreach (var bar in _bars) bar.SchedulePlace(forget: true);
            return;
        }
        _barsKey = key;

        foreach (var bar in _bars) bar.Close(); // gives the reserved strip (and the tray) back
        _bars.Clear();
        // The primary first: its tray service must be the one apps find.
        foreach (var display in wanted.OrderByDescending(d => d.IsPrimary))
        {
            var bar = new MenuBarWindow(this, display.DeviceName, display.IsPrimary);
            _bars.Add(bar);
            bar.Show();
            bar.ShowStatus(_status);
        }
        Log.Info($"Menu bar on {_bars.Count} displays");
    }

    public void ApplyTheme() => _bars.ForEach(b => b.ApplyTheme());

    /// <summary>Back from sleep, lock or a user switch: everything may be stale.</summary>
    public void Refresh()
    {
        UpdateClock();
        UpdateActiveApp(GetForegroundWindow());
        _status.Poll(force: true);
        Sync();
        foreach (var bar in _bars) bar.Reactivate();
    }

    /// <summary>Screen off or session locked: nobody sees the status icons, stop polling them.</summary>
    public void SetPaused(bool paused) => _status.SetPaused(paused);

    public void UpdateClock()
    {
        var now = DateTime.Now;
        var culture = CultureInfo.CurrentCulture;
        var day = now.ToString("ddd", culture);
        if (day.Length > 0) day = char.ToUpper(day[0], culture) + day[1..];
        ClockText = $"{day} {now.ToString("d MMM", culture)}   {now.ToString("t", culture)}";
        ClockTip = now.ToString("D", culture);
        foreach (var bar in _bars) bar.ShowClock(ClockText, ClockTip);
        // Next tick right after the minute turns.
        _clockTimer.Stop();
        _clockTimer.Interval = TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond - 20);
        _clockTimer.Start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _clockTimer.Stop();
        if (_foregroundHook != IntPtr.Zero) UnhookWinEvent(_foregroundHook);
        _foregroundHook = IntPtr.Zero;
        foreach (var bar in _bars) bar.Close();
        _bars.Clear();
        _status.Dispose();
    }

    void UpdateActiveApp(IntPtr foreground)
    {
        if (foreground == IntPtr.Zero) return;
        var root = GetAncestor(foreground, GA_ROOTOWNER);
        if (root == IntPtr.Zero) root = foreground;
        var className = GetClassName(root);
        string? name;
        if (className is "Progman" or "WorkerW")
        {
            name = DesktopName;
        }
        else
        {
            GetWindowThreadProcessId(root, out uint pid);
            if (pid == Environment.ProcessId) return;
            string process;
            try
            {
                using var p = Process.GetProcessById((int)pid);
                process = p.ProcessName;
            }
            catch (Exception)
            {
                return;
            }
            // Explorer's own surfaces (taskbar, Alt+Tab, Task View) are not an app; its folders are.
            if (TransientProcesses.Contains(process)) return;
            if (process.Equals("explorer", StringComparison.OrdinalIgnoreCase) && className != "CabinetWClass") return;

            name = _dockNameFor(root);
            if (name is null)
            {
                var identity = AppIdentity.FromWindow(root, className);
                var key = identity.Key ?? process;
                if (!_nameCache.TryGetValue(key, out name))
                {
                    if (_nameCache.Count > 200) _nameCache.Clear(); // bounded: apps come and go for weeks
                    _nameCache[key] = name = DockItem.ResolveName(identity, process);
                }
            }
        }
        if (name == AppName) return;
        AppName = name;
        foreach (var bar in _bars) bar.ShowAppName(name);
    }
}
