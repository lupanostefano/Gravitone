using System.Runtime.InteropServices;
using System.Text;
using Gravitone.Interop;
using static Gravitone.Interop.NativeMethods;

namespace Gravitone.Core;

/// <summary>
/// What makes two windows / shortcuts "the same app", the way the taskbar groups them:
/// the AppUserModelID when there is one, otherwise the executable path.
/// </summary>
internal sealed record AppIdentity(string? Aumid, string? ExePath)
{
    public static readonly AppIdentity None = new(null, null);

    const string ExplorerAumid = "Microsoft.Windows.Explorer";

    public string? Key => Aumid ?? ExePath;

    public bool Matches(AppIdentity other)
    {
        // Two different AUMIDs are two different apps even when they share an exe
        // (e.g. a browser and a web app installed from it).
        if (Aumid is not null && other.Aumid is not null)
            return string.Equals(Aumid, other.Aumid, StringComparison.OrdinalIgnoreCase);
        return ExePath is not null && string.Equals(ExePath, other.ExePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Identity of a dock target: a Store app, a shortcut or an executable.</summary>
    public static AppIdentity FromTarget(string target)
    {
        var t = Environment.ExpandEnvironmentVariables(target);
        if (t.StartsWith(ShellItems.AppsFolder, StringComparison.OrdinalIgnoreCase))
        {
            var aumid = t[ShellItems.AppsFolder.Length..];
            return new(aumid, NormalizePath(ShellItems.GetString(t, PKEY_Link_TargetParsingPath)));
        }
        if (t.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return new(ShellItems.GetString(t, PKEY_AppUserModel_ID),
                NormalizePath(ShellItems.GetString(t, PKEY_Link_TargetParsingPath)));
        }
        return t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? new(null, NormalizePath(t)) : None;
    }

    /// <summary>Identity of a top-level window.</summary>
    public static AppIdentity FromWindow(IntPtr hwnd, string className)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        var exe = ProcessPath(pid);
        var aumid = WindowAumid(hwnd) ?? PackageAumid(pid);

        if (aumid is null && className == "ApplicationFrameWindow")
        {
            // Store apps live in a CoreWindow of another process hosted inside the frame.
            uint childPid = HostedCoreWindowProcess(hwnd, pid);
            if (childPid != 0)
            {
                aumid = PackageAumid(childPid);
                exe = ProcessPath(childPid) ?? exe;
            }
        }

        if (aumid is null && className == "CabinetWClass"
            && exe is not null && exe.EndsWith(@"\explorer.exe", StringComparison.OrdinalIgnoreCase))
            aumid = ExplorerAumid;

        return new(aumid, exe);
    }

    static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        path = Environment.ExpandEnvironmentVariables(path);
        // Shell namespace targets ("::{CLSID}") are not files and cannot identify an exe.
        if (!Path.IsPathFullyQualified(path)) return null;
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return path;
        }
    }

    static string? WindowAumid(IntPtr hwnd)
    {
        IPropertyStore? store = null;
        try
        {
            var iid = typeof(IPropertyStore).GUID;
            SHGetPropertyStoreForWindow(hwnd, ref iid, out store);
            var key = PKEY_AppUserModel_ID;
            if (store.GetValue(ref key, out var pv) < 0) return null;
            try
            {
                return pv.vt == VT_LPWSTR ? Marshal.PtrToStringUni(pv.p) : null;
            }
            finally
            {
                PropVariantClear(ref pv);
            }
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (store is not null) Marshal.ReleaseComObject(store);
        }
    }

    static string? ProcessPath(uint pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            uint size = (uint)sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? NormalizePath(sb.ToString()) : null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    static string? PackageAumid(uint pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            uint length = 0;
            GetApplicationUserModelId(h, ref length, null);
            if (length == 0) return null;
            var sb = new StringBuilder((int)length);
            return GetApplicationUserModelId(h, ref length, sb) == 0 ? sb.ToString() : null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    static uint HostedCoreWindowProcess(IntPtr frame, uint framePid)
    {
        uint found = 0;
        EnumChildWindows(frame, (child, _) =>
        {
            if (GetClassName(child) != "Windows.UI.Core.CoreWindow") return true;
            GetWindowThreadProcessId(child, out uint pid);
            if (pid == framePid) return true;
            found = pid;
            return false;
        }, IntPtr.Zero);
        return found;
    }
}
