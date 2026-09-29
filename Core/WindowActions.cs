using static Gravitone.Interop.NativeMethods;

namespace Gravitone.Core;

internal static class WindowActions
{
    /// <summary>Restores (if minimized) and brings a window to the front.</summary>
    public static void Activate(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindowAsync(hwnd, SW_RESTORE);
        // Allowed because the click on the dock was the last input; the fallback covers the rest.
        if (!SetForegroundWindow(hwnd)) SwitchToThisWindow(hwnd, true);
    }

    /// <summary>Minimizes through the window's own system command, so the app sees it and animates.</summary>
    public static void Minimize(IntPtr hwnd) => PostMessage(hwnd, WM_SYSCOMMAND, SC_MINIMIZE, IntPtr.Zero);

    /// <summary>Asks the window to close, exactly like its close button.</summary>
    public static void Close(IntPtr hwnd) => PostMessage(hwnd, WM_SYSCOMMAND, SC_CLOSE, IntPtr.Zero);
}
