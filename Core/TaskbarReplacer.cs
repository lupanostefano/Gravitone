using System.Diagnostics;
using System.Windows.Threading;
using static Gravitone.Interop.NativeMethods;

namespace Gravitone.Core;

/// <summary>
/// Replaces the Windows taskbar while the dock runs. Windows shows its taskbar again on its own
/// (Start, display changes, Explorer restarts), so every show is caught and undone; a guard
/// process is kept alive to restore it if the dock dies. The real taskbar can be brought back
/// for a while with the hotkey.
/// </summary>
internal sealed class TaskbarReplacer : IDisposable
{
    static readonly TimeSpan GuardRetryDelay = TimeSpan.FromSeconds(30);

    readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    readonly DispatcherTimer _enforceTimer;
    readonly WinEventProc _winEventProc; // kept alive: native code calls it
    IntPtr _hook;
    uint _hookedPid;
    Process? _guard;
    bool _guardStarting;
    DateTime _nextGuardAttempt;
    bool _disposed;

    public TaskbarReplacer(bool enabled)
    {
        Enabled = enabled;
        _winEventProc = OnWinEvent;
        // Safety net for shows the event hook misses (and for a guard that died).
        _enforceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _enforceTimer.Tick += (_, _) => Enforce();
    }

    /// <summary>The "replace the taskbar" option.</summary>
    public bool Enabled { get; private set; }

    /// <summary>The user brought the Windows taskbar back for a while.</summary>
    public bool TemporarilyShown { get; private set; }

    bool _sessionEnding;

    bool Active => Enabled && !TemporarilyShown && !_sessionEnding && !_disposed;

    public void Start()
    {
        if (Active) Apply();
        else TaskbarState.Restore(); // left hidden by a run that could not clean up
    }

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        TemporarilyShown = false;
        Update();
    }

    public void ToggleTemporarilyShown()
    {
        if (!Enabled) return;
        TemporarilyShown = !TemporarilyShown;
        Update();
    }

    /// <summary>
    /// Windows is about to sign out or shut down: give the taskbar back while Explorer can still
    /// take it (and save it), hide it again if the shutdown is cancelled.
    /// </summary>
    public void SetSessionEnding(bool ending)
    {
        if (_sessionEnding == ending) return;
        _sessionEnding = ending;
        Update();
    }

    /// <summary>Explorer restarted and created a new taskbar.</summary>
    public void OnTaskbarCreated()
    {
        if (!Active) return;
        Unhook();
        Apply();
    }

    /// <summary>Gives the user's taskbar back (exit, end of session). Safe to call more than once.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Release();
    }

    void Update()
    {
        if (Active) Apply();
        else Release();
    }

    void Apply()
    {
        TaskbarState.Hide();
        Hook();
        _enforceTimer.Start();
        EnsureGuard();
    }

    void Release()
    {
        _enforceTimer.Stop();
        Unhook();
        TaskbarState.Restore();
    }

    void Enforce()
    {
        if (!Active) return;
        TaskbarState.Enforce();
        if (TaskbarState.ExplorerProcessId() != _hookedPid)
        {
            Unhook();
            Hook();
        }
        EnsureGuard();
    }

    /// <summary>Hides a taskbar the moment Explorer shows it, before it gets painted.</summary>
    void Hook()
    {
        uint pid = TaskbarState.ExplorerProcessId();
        if (pid == 0 || _hook != IntPtr.Zero) return;
        _hook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _winEventProc, pid, 0, WINEVENT_OUTOFCONTEXT);
        _hookedPid = _hook != IntPtr.Zero ? pid : 0;
    }

    void Unhook()
    {
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
        _hookedPid = 0;
    }

    void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != OBJID_WINDOW || idChild != 0 || !Active) return;
        if (TaskbarState.IsTaskbarWindow(hwnd)) ShowWindow(hwnd, SW_HIDE);
    }

    void EnsureGuard()
    {
        if (_guardStarting || DateTime.UtcNow < _nextGuardAttempt) return;
        if (_guard is not null && !HasExited(_guard)) return;
        if (_guard is not null) Log.Info("Guard ended: starting another");

        _guardStarting = true;
        Task.Run(Guard.Start).ContinueWith(t => _dispatcher.BeginInvoke(() =>
        {
            _guardStarting = false;
            _guard?.Dispose();
            _guard = t.Result;
            if (_guard is null) _nextGuardAttempt = DateTime.UtcNow + GuardRetryDelay;
        }));
    }

    static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (Exception)
        {
            return true;
        }
    }
}
