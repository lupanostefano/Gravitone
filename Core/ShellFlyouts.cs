using static Gravitone.Interop.NativeMethods;

namespace Gravitone.Core;

/// <summary>The Windows 11 flyouts the menu bar opens, through their own keyboard shortcuts.</summary>
internal static class ShellFlyouts
{
    const byte VK_A = 0x41, VK_N = 0x4E;

    /// <summary>Quick Settings: Wi-Fi, volume, battery (Win+A).</summary>
    public static void QuickSettings() => PressKeys(VK_LWIN, VK_A);

    /// <summary>Notifications and calendar (Win+N).</summary>
    public static void NotificationCenter() => PressKeys(VK_LWIN, VK_N);

    static void PressKeys(params byte[] keys)
    {
        foreach (var key in keys) keybd_event(key, 0, 0, UIntPtr.Zero);
        for (int i = keys.Length - 1; i >= 0; i--) keybd_event(keys[i], 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
