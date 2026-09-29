using System.Runtime.InteropServices;
using Gravitone.Interop;
using static Gravitone.Interop.NativeMethods;

namespace Gravitone.Core;

/// <summary>A monitor: its device name (<c>\\.\DISPLAY2</c>), its area and work area in physical pixels, and its scale.</summary>
internal readonly record struct Display(IntPtr Handle, string DeviceName, RECT Bounds, RECT Work, bool IsPrimary, double Scale)
{
    /// <summary>Whether another monitor continues the screen beyond <paramref name="edge"/> at <paramref name="p"/>.</summary>
    public bool HasNeighbourBeyond(DockEdge edge, POINT p)
    {
        var beyond = edge switch
        {
            DockEdge.Left => new POINT { X = Bounds.Left - 1, Y = p.Y },
            DockEdge.Right => new POINT { X = Bounds.Right, Y = p.Y },
            _ => new POINT { X = p.X, Y = Bounds.Bottom },
        };
        return MonitorFromPoint(beyond, MONITOR_DEFAULTTONULL) != IntPtr.Zero;
    }
}

/// <summary>The connected monitors, in the order Windows enumerates them (the primary first).</summary>
internal static class Displays
{
    public static List<Display> All()
    {
        var result = new List<Display>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr _, ref RECT _, IntPtr _) =>
        {
            if (Describe(monitor) is { } display) result.Add(display);
            return true;
        }, IntPtr.Zero);
        result.Sort((a, b) => a.IsPrimary != b.IsPrimary ? (a.IsPrimary ? -1 : 1)
            : a.Bounds.Left != b.Bounds.Left ? a.Bounds.Left.CompareTo(b.Bounds.Left) : a.Bounds.Top.CompareTo(b.Bounds.Top));
        if (result.Count == 0 && Describe(MonitorFromPoint(new POINT(), MONITOR_DEFAULTTOPRIMARY)) is { } primary) result.Add(primary);
        return result;
    }

    public static Display Primary() =>
        Describe(MonitorFromPoint(new POINT(), MONITOR_DEFAULTTOPRIMARY)) ?? All()[0];

    /// <summary>The monitor with this device name, or the primary one when it is not connected (or the name is empty).</summary>
    public static Display ByNameOrPrimary(string? deviceName)
    {
        if (!string.IsNullOrEmpty(deviceName))
        {
            foreach (var d in All())
                if (string.Equals(d.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase)) return d;
        }
        return Primary();
    }

    public static Display FromPoint(POINT p) =>
        Describe(MonitorFromPoint(p, MONITOR_DEFAULTTONEAREST)) ?? Primary();

    /// <summary>
    /// A key that changes whenever monitors are added, removed, moved, rescaled or the primary changes
    /// (not the work areas: our own app bars change those).
    /// </summary>
    public static string Layout() =>
        string.Join(";", All().Select(d => $"{d.DeviceName}{d.Bounds}{d.Scale}{(d.IsPrimary ? "*" : "")}"));

    /// <summary>
    /// Whether the full-screen app Explorer just reported (<c>ABN_FULLSCREENAPP</c>) is on the monitor of
    /// <paramref name="ourWindow"/>. Explorer tells every app bar, whatever monitor the app is on.
    /// </summary>
    public static bool IsFullscreenAppOn(IntPtr ourWindow)
    {
        var foreground = GetForegroundWindow();
        // The desktop itself is sometimes reported as a full-screen window.
        if (foreground != IntPtr.Zero && GetClassName(foreground) is "Progman" or "WorkerW") return false;
        // One monitor: trust the notification (the app may not be in front yet).
        if (All().Count <= 1) return true;
        return foreground != IntPtr.Zero
               && MonitorFromWindow(foreground, MONITOR_DEFAULTTONEAREST) == MonitorFromWindow(ourWindow, MONITOR_DEFAULTTONEAREST);
    }

    /// <summary>"Display 2 (1920 × 1080)", with ", primary" for the primary one.</summary>
    public static string FriendlyName(Display d, int index) =>
        d.IsPrimary ? Loc.T("Display {0} ({1} × {2}), primary", index + 1, d.Bounds.Width, d.Bounds.Height) : Loc.T("Display {0} ({1} × {2})", index + 1, d.Bounds.Width, d.Bounds.Height);

    static Display? Describe(IntPtr monitor)
    {
        if (monitor == IntPtr.Zero) return null;
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetMonitorInfoEx(monitor, ref info)) return null;
        double scale = GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1;
        return new Display(monitor, info.szDevice ?? "", info.rcMonitor, info.rcWork, (info.dwFlags & MONITORINFOF_PRIMARY) != 0, scale);
    }
}
