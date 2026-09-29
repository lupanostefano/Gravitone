using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static Gravitone.Interop.NativeMethods;

namespace Gravitone.Core;

/// <summary>The dock's Start item: opens the Start menu (click) or the Win+X menu (right click).</summary>
internal static class StartMenu
{
    public const string Target = "gravitone:start";
    const byte VK_X = 0x58;

    static readonly HashSet<string> StartProcesses =
        new(StringComparer.OrdinalIgnoreCase) { "StartMenuExperienceHost", "SearchHost", "SearchApp" };

    static BitmapSource? _icon;

    // "windock:start" is what configurations written before the product got its name contain.
    public static bool IsTarget(string target) =>
        string.Equals(target, Target, StringComparison.OrdinalIgnoreCase) || string.Equals(target, "windock:start", StringComparison.OrdinalIgnoreCase);

    public static DockItemConfig CreateConfig() => new() { Name = "Start", Target = Target };

    public static BitmapSource Icon => _icon ??= CreateIcon();

    /// <summary>Opens Start, or closes it when open, exactly like the Windows key.</summary>
    public static void Toggle() => PressKeys(VK_LWIN);

    /// <summary>The menu Windows shows on a right click of its own Start button.</summary>
    public static void ShowQuickLinkMenu() => PressKeys(VK_LWIN, VK_X);

    /// <summary>True when Start (or search, which opens in its place) is the window in front.</summary>
    public static bool IsOpen(IntPtr foreground)
    {
        if (foreground == IntPtr.Zero) return false;
        GetWindowThreadProcessId(foreground, out uint pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return StartProcesses.Contains(process.ProcessName);
        }
        catch (Exception)
        {
            return false;
        }
    }

    static void PressKeys(params byte[] keys)
    {
        foreach (var key in keys) keybd_event(key, 0, 0, UIntPtr.Zero);
        for (int i = keys.Length - 1; i >= 0; i--) keybd_event(keys[i], 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    /// <summary>A blue rounded tile with four white panes, on the same grid as the normalized app icons.</summary>
    static BitmapSource CreateIcon()
    {
        const int size = 256;
        double tile = size * 0.88, origin = (size - tile) / 2, radius = tile * 0.225;
        double pane = tile * 0.2, gap = tile * 0.035, first = size / 2.0 - pane - gap / 2;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var background = new LinearGradientBrush(Color.FromRgb(0x3B, 0xB0, 0xFF), Color.FromRgb(0x00, 0x5F, 0xD4), 90);
            dc.DrawRoundedRectangle(background, null, new Rect(origin, origin, tile, tile), radius, radius);
            for (int row = 0; row < 2; row++)
            {
                for (int col = 0; col < 2; col++)
                {
                    var r = new Rect(first + col * (pane + gap), first + row * (pane + gap), pane, pane);
                    dc.DrawRoundedRectangle(Brushes.White, null, r, pane * 0.06, pane * 0.06);
                }
            }
        }
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
