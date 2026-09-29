using System.Runtime.InteropServices;
using System.Text.Json;
using Gravitone.Interop;
using static Gravitone.Interop.NativeMethods;

namespace Gravitone.Core;

/// <summary>
/// Hides and restores the Windows taskbars. Shared by the dock and the guard process, so it never
/// touches WPF. While the taskbar is hidden a marker file records the user's own taskbar state and
/// the process that hid it: whoever finds the marker after that process is gone puts things back.
/// </summary>
internal static class TaskbarState
{
    const string PrimaryClass = "Shell_TrayWnd";
    const string SecondaryClass = "Shell_SecondaryTrayWnd";

    sealed record Marker(int OriginalState, int OwnerPid);

    static string MarkerPath => Path.Combine(DockConfig.Folder, "taskbar.json");

    public static bool IsTaskbarWindow(IntPtr hwnd) => GetClassName(hwnd) is PrimaryClass or SecondaryClass;

    /// <summary>The Explorer process that owns the taskbar, or 0 while there is none.</summary>
    public static uint ExplorerProcessId()
    {
        var tray = FindExplorerTray();
        if (tray == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(tray, out uint pid);
        return pid;
    }

    /// <summary>
    /// Explorer's own taskbar. The menu bar's tray service creates a <c>Shell_TrayWnd</c> too, kept on
    /// top so that apps find it first, so a plain <c>FindWindow</c> is not enough.
    /// </summary>
    public static IntPtr FindExplorerTray()
    {
        // Asked every second while the taskbar is hidden: reuse the last answer while that window lives.
        if (_lastTray is { } last && IsWindow(last.Hwnd) && GetClassName(last.Hwnd) == PrimaryClass
            && GetWindowThreadProcessId(last.Hwnd, out uint lastPid) != 0 && lastPid == last.Pid)
            return last.Hwnd;

        var hwnd = IntPtr.Zero;
        while ((hwnd = FindWindowEx(IntPtr.Zero, hwnd, PrimaryClass, null)) != IntPtr.Zero)
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == Environment.ProcessId) continue; // the menu bar's tray service
            if (IsExplorer(pid))
            {
                _lastTray = new FoundTray(hwnd, pid);
                return hwnd;
            }
        }
        _lastTray = null;
        return IntPtr.Zero;
    }

    sealed record FoundTray(IntPtr Hwnd, uint Pid);
    static volatile FoundTray? _lastTray;

    static bool IsExplorer(uint pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return string.Equals(process.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Hides every taskbar and has Explorer release its screen space, by switching it to auto-hide
    /// (the only way Explorer stops reserving it). Returns false while there is no taskbar yet.
    /// </summary>
    public static bool Hide()
    {
        var tray = FindExplorerTray();
        if (tray == IntPtr.Zero) return false;

        using (Lock())
        {
            var marker = ReadMarker();
            int pid = Environment.ProcessId;
            if (marker is null || marker.OwnerPid != pid)
            {
                // A marker left by a run that could not clean up still holds the user's real state.
                int original = marker?.OriginalState ?? GetState(tray);
                WriteMarker(new Marker(original, pid));
                AutoStart.SetRestoreFallback(true);
                Log.Info($"Taskbar hidden (original state {original})");
            }
            if ((GetState(tray) & ABS_AUTOHIDE) == 0) SetState(tray, ABS_AUTOHIDE | ABS_ALWAYSONTOP);
        }
        HideWindows(tray);
        return true;
    }

    /// <summary>Cheap re-check while hidden: hides any taskbar Windows has shown again.</summary>
    public static void Enforce()
    {
        var tray = FindExplorerTray();
        if (tray == IntPtr.Zero) return;
        // Someone (the user, Settings, a guard that saw a stale owner) turned auto-hide off.
        if ((GetState(tray) & ABS_AUTOHIDE) == 0) Hide();
        else HideWindows(tray);
    }

    /// <summary>
    /// Puts the user's taskbar state back and shows every taskbar, if one of our processes hid it.
    /// With <paramref name="onlyForPid"/>, only if that process was the one. <paramref name="force"/>
    /// shows the taskbars even without a marker (emergency command line).
    /// Returns true when something was restored.
    /// </summary>
    public static bool Restore(int? onlyForPid = null, bool force = false)
    {
        using (Lock())
        {
            var marker = ReadMarker();
            if (onlyForPid is int pid && marker?.OwnerPid != pid) return false;
            if (marker is null && !force) return false;

            // No Explorer right now: keep the marker, the next run (or Explorer's own start) sorts it out.
            var tray = FindExplorerTray();
            if (tray == IntPtr.Zero) return false;

            if (marker is not null)
            {
                SetState(tray, marker.OriginalState);
                PersistAutoHide((marker.OriginalState & ABS_AUTOHIDE) != 0);
            }
            foreach (var h in TaskbarWindows(tray)) ShowWindow(h, SW_SHOWNA);
            DeleteMarker();
            AutoStart.SetRestoreFallback(false);
            Log.Info($"Taskbar restored (state {marker?.OriginalState.ToString() ?? "unchanged"}, process {Environment.ProcessId})");
            return true;
        }
    }

    /// <summary>
    /// The sign-in fallback (see <see cref="AutoStart.SetRestoreFallback"/>): waits for Explorer's
    /// taskbar, then restores it unless a running Gravitone is the one keeping it hidden.
    /// </summary>
    public static void RestoreIfOrphaned()
    {
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (FindExplorerTray() == IntPtr.Zero && DateTime.UtcNow < deadline) Thread.Sleep(1000);

        int? owner;
        using (Lock()) owner = ReadMarker()?.OwnerPid;
        if (owner is not int pid) return;
        if (IsRunningDock(pid)) return;
        if (Restore(onlyForPid: pid)) Log.Info("Taskbar restored at sign-in: Gravitone was not running");
        else AutoStart.SetRestoreFallback(true); // no Explorer yet: try again at the next sign-in
    }

    static bool IsRunningDock(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return string.Equals(process.ProcessName, "Gravitone", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Explorer keeps the auto-hide state in memory and saves it when it pleases (at the latest when it
    /// exits). Writing it too means a restart or a crash of Explorer right after can never bring back
    /// the auto-hide we set. Byte 8 of <c>StuckRects3\Settings</c>, bit 0 = auto-hide.
    /// </summary>
    static void PersistAutoHide(bool autoHide)
    {
        const string key = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StuckRects3";
        try
        {
            using var stuck = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(key, writable: true);
            if (stuck?.GetValue("Settings") is not byte[] { Length: > 8 } settings) return;
            byte updated = (byte)(autoHide ? settings[8] | 1 : settings[8] & ~1);
            if (updated == settings[8]) return;
            settings[8] = updated;
            stuck.SetValue("Settings", settings, Microsoft.Win32.RegistryValueKind.Binary);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Saving the taskbar state to the registry");
        }
    }

    static void HideWindows(IntPtr tray)
    {
        foreach (var h in TaskbarWindows(tray))
            if (IsWindowVisible(h)) ShowWindow(h, SW_HIDE);
    }

    /// <summary>The primary taskbar and one per extra monitor.</summary>
    static List<IntPtr> TaskbarWindows(IntPtr tray)
    {
        // FindWindowEx filters by class itself: far cheaper, every second, than reading every window's class.
        var result = new List<IntPtr> { tray };
        var hwnd = IntPtr.Zero;
        while ((hwnd = FindWindowEx(IntPtr.Zero, hwnd, SecondaryClass, null)) != IntPtr.Zero) result.Add(hwnd);
        return result;
    }

    static int GetState(IntPtr tray)
    {
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = tray };
        return (int)SHAppBarMessage(ABM_GETSTATE, ref data);
    }

    static void SetState(IntPtr tray, int state)
    {
        var data = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = tray, lParam = state };
        SHAppBarMessage(ABM_SETSTATE, ref data);
    }

    static Marker? ReadMarker()
    {
        try
        {
            return File.Exists(MarkerPath) ? JsonSerializer.Deserialize<Marker>(File.ReadAllText(MarkerPath)) : null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Reading the taskbar state");
            return null;
        }
    }

    static void WriteMarker(Marker marker)
    {
        try
        {
            Directory.CreateDirectory(DockConfig.Folder);
            var tmp = MarkerPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(marker));
            File.Move(tmp, MarkerPath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Saving the taskbar state");
        }
    }

    static void DeleteMarker()
    {
        try
        {
            File.Delete(MarkerPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Deleting the taskbar state");
        }
    }

    /// <summary>Serializes marker updates between the dock and the guard.</summary>
    static MutexLock Lock()
    {
        var mutex = new Mutex(false, @"Local\Gravitone.TaskbarState");
        bool owned;
        try
        {
            owned = mutex.WaitOne(TimeSpan.FromSeconds(5));
        }
        catch (AbandonedMutexException)
        {
            owned = true; // its previous owner died mid-update; the marker file is still whole
        }
        return new MutexLock(mutex, owned);
    }

    readonly struct MutexLock(Mutex mutex, bool owned) : IDisposable
    {
        public void Dispose()
        {
            if (owned) mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}
