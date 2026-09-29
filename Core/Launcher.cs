using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Gravitone.Core;

internal static class Launcher
{
    /// <summary>Whether the item can be started elevated: a program, shortcut or script (not a Store app or a folder).</summary>
    public static bool CanRunAsAdmin(DockItem item)
    {
        var target = Environment.ExpandEnvironmentVariables(item.Target);
        return !item.IsStart && !item.IsFolder && !item.IsTrash && (File.Exists(target) || IsWindowsTerminal(item));
    }

    static bool IsWindowsTerminal(DockItem item) =>
        item.Target.Contains("Microsoft.WindowsTerminal", StringComparison.OrdinalIgnoreCase);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHELLEXECUTEINFO
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public string? lpVerb, lpFile, lpParameters, lpDirectory;
        public int nShow;
        public IntPtr hInstApp, lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon, hProcess;
    }

    const uint SEE_MASK_NOASYNC = 0x00000100;
    const uint SEE_MASK_FLAG_NO_UI = 0x00000400;
    const int SW_SHOWNORMAL = 1;
    const int ERROR_CANCELLED = 1223;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);

    /// <summary>
    /// Starts the item with the "Run as administrator" verb. <paramref name="owner"/> must be a window of
    /// ours in front: UAC puts its prompt in front only when asked from the foreground window, otherwise
    /// it waits as a flashing taskbar button — and the taskbar is hidden.
    /// </summary>
    public static void LaunchAsAdmin(DockItem item, IntPtr owner)
    {
        var target = ElevationTarget(item);
        var dir = Path.GetDirectoryName(target);
        var info = new SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
            fMask = SEE_MASK_NOASYNC | SEE_MASK_FLAG_NO_UI,
            hwnd = owner,
            lpVerb = "runas",
            lpFile = target,
            lpParameters = string.IsNullOrEmpty(item.Config.Arguments) ? null : item.Config.Arguments,
            lpDirectory = File.Exists(target) && !string.IsNullOrEmpty(dir) ? dir : null,
            nShow = SW_SHOWNORMAL,
        };
        if (ShellExecuteEx(ref info)) return;
        int error = Marshal.GetLastWin32Error();
        if (error != ERROR_CANCELLED) // the user said no
            Log.Info($"Launching {item.Name} as administrator ({target}) failed: error {error}");
    }

    /// <summary>
    /// What to elevate. Store apps cannot be elevated through their shell path: Windows Terminal through
    /// its program alias.
    /// </summary>
    static string ElevationTarget(DockItem item)
    {
        if (IsWindowsTerminal(item))
        {
            var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\wt.exe");
            return File.Exists(alias) ? alias : "wt.exe";
        }
        return Environment.ExpandEnvironmentVariables(item.Target);
    }

    public static void Launch(DockItem item)
    {
        try
        {
            var target = Environment.ExpandEnvironmentVariables(item.Target);
            ProcessStartInfo psi;
            if (target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || ShellItems.IsVirtual(target))
            {
                // Packaged apps and shell folders open reliably through Explorer.
                psi = new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true };
            }
            else
            {
                psi = new ProcessStartInfo(target) { UseShellExecute = true, Arguments = item.Config.Arguments ?? "" };
                var dir = Path.GetDirectoryName(target);
                if (File.Exists(target) && !string.IsNullOrEmpty(dir)) psi.WorkingDirectory = dir;
            }
            Process.Start(psi)?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Launching {item.Name}");
        }
    }
}
