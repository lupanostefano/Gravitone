using Microsoft.Win32;

namespace Gravitone.Core;

internal static class Theme
{
    /// <summary>Follows the Windows "system" colour mode, the one the taskbar uses.</summary>
    public static bool IsDark { get; private set; } = true;

    public static void Refresh()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            IsDark = key?.GetValue("SystemUsesLightTheme") is not int light || light == 0;
        }
        catch (Exception)
        {
            IsDark = true;
        }
    }
}
