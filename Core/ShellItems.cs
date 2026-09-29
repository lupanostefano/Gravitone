using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Gravitone.Interop;
using static Gravitone.Interop.NativeMethods;

namespace Gravitone.Core;

/// <summary>Names and high-resolution icons for anything the shell can parse: files, shortcuts, folders, Store apps.</summary>
internal static class ShellItems
{
    public const string AppsFolder = @"shell:AppsFolder\";
    const int IconPixels = 256;

    public static bool Exists(string parsingName)
    {
        var item = Create(parsingName);
        if (item is null) return false;
        Marshal.ReleaseComObject(item);
        return true;
    }

    public static string? DisplayName(string parsingName)
    {
        var item = Create(parsingName);
        if (item is null) return null;
        try
        {
            item.GetDisplayName(SIGDN_NORMALDISPLAY, out var name);
            return name;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(item);
        }
    }

    /// <summary>Reads a string property (e.g. a shortcut's target or AppUserModelID), or null.</summary>
    public static string? GetString(string parsingName, PROPERTYKEY key)
    {
        var item = Create(parsingName);
        if (item is null) return null;
        try
        {
            if (item is not IShellItem2 item2) return null;
            return item2.GetString(ref key, out var value) >= 0 && !string.IsNullOrWhiteSpace(value) ? value : null;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(item);
        }
    }

    /// <summary>Returns the item's icon at 256 px, trimmed and re-centred so every icon has the same visual weight.</summary>
    public static BitmapSource? Icon(string parsingName)
    {
        var item = Create(parsingName);
        if (item is null) return null;
        try
        {
            if (item is not IShellItemImageFactory factory) return null;
            var size = new SIZE { cx = IconPixels, cy = IconPixels };
            if (factory.GetImage(size, SIIGBF_ICONONLY, out var hbm) < 0 || hbm == IntPtr.Zero)
            {
                if (factory.GetImage(size, SIIGBF_RESIZETOFIT, out hbm) < 0 || hbm == IntPtr.Zero) return null;
            }
            try
            {
                var raw = FromHBitmap(hbm);
                return raw is null ? null : Normalize(raw, IconPixels);
            }
            finally
            {
                DeleteObject(hbm);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Icon of {parsingName}");
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(item);
        }
    }

    const uint SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000;

    [DllImport("shell32.dll")]
    static extern IntPtr ILCombine(IntPtr parent, IntPtr child);
    [DllImport("shell32.dll")]
    static extern void ILFree(IntPtr pidl);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHGetNameFromIDList(IntPtr pidl, uint sigdn, out IntPtr name);

    /// <summary>
    /// The parsing names of the items in a shell drag ("Shell IDList Array", a CIDA): file paths for
    /// files, <c>::{CLSID}</c> names for virtual items such as This PC or Control Panel, which carry no path.
    /// </summary>
    public static List<string> FromIdListArray(byte[] cida)
    {
        var result = new List<string>();
        if (cida.Length < 8) return result;
        var memory = Marshal.AllocHGlobal(cida.Length);
        try
        {
            Marshal.Copy(cida, 0, memory, cida.Length);
            // CIDA: item count, then offsets of the parent folder's ID list and of each item's (relative) one.
            int count = Marshal.ReadInt32(memory);
            if (count <= 0 || 4 + (count + 1) * 4 > cida.Length) return result;
            var parent = memory + Marshal.ReadInt32(memory, 4);
            for (int i = 0; i < count; i++)
            {
                var child = memory + Marshal.ReadInt32(memory, 8 + i * 4);
                var absolute = ILCombine(parent, child);
                if (absolute == IntPtr.Zero) continue;
                try
                {
                    if (SHGetNameFromIDList(absolute, SIGDN_DESKTOPABSOLUTEPARSING, out var name) != 0 || name == IntPtr.Zero) continue;
                    var text = Marshal.PtrToStringUni(name);
                    Marshal.FreeCoTaskMem(name);
                    if (!string.IsNullOrEmpty(text)) result.Add(text);
                }
                finally
                {
                    ILFree(absolute);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Reading the dropped items");
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
        return result;
    }

    /// <summary>A shell item without a file path, such as This PC (<c>::{20D04FE0-…}</c>).</summary>
    public static bool IsVirtual(string target) => target.StartsWith("::", StringComparison.Ordinal);

    static IShellItem? Create(string parsingName)
    {
        try
        {
            SHCreateItemFromParsingName(Environment.ExpandEnvironmentVariables(parsingName), IntPtr.Zero,
                typeof(IShellItem).GUID, out var obj);
            return (IShellItem)obj;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Copies a 32-bit shell HBITMAP into a WPF bitmap, keeping its alpha channel.</summary>
    static BitmapSource? FromHBitmap(IntPtr hbm)
    {
        if (GetObject(hbm, Marshal.SizeOf<BITMAP>(), out var bm) == 0) return null;
        int w = bm.bmWidth, h = bm.bmHeight;
        if (w <= 0 || h <= 0) return null;

        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h, // top-down rows
                biPlanes = 1,
                biBitCount = 32,
            },
        };
        var px = new byte[w * h * 4];
        var dc = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(dc, hbm, 0, (uint)h, px, ref bmi, 0) == 0) return null;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }

        bool hasAlpha = false, premultiplied = true;
        for (int i = 0; i < px.Length; i += 4)
        {
            byte a = px[i + 3];
            if (a != 0) hasAlpha = true;
            if (px[i] > a || px[i + 1] > a || px[i + 2] > a) premultiplied = false;
        }
        if (!hasAlpha)
        {
            for (int i = 3; i < px.Length; i += 4) px[i] = 255;
            premultiplied = true;
        }

        var src = BitmapSource.Create(w, h, 96, 96,
            premultiplied ? PixelFormats.Pbgra32 : PixelFormats.Bgra32, null, px, w * 4);
        src.Freeze();
        return src;
    }

    /// <summary>
    /// Shell icons carry wildly different transparent margins. Crop to the visible pixels and
    /// scale them to a fixed share of the tile, the way every macOS icon follows the same grid.
    /// </summary>
    static BitmapSource Normalize(BitmapSource src, int size, double fill = 0.88)
    {
        int w = src.PixelWidth, h = src.PixelHeight;
        var fmtSrc = src.Format == PixelFormats.Pbgra32 || src.Format == PixelFormats.Bgra32
            ? src
            : new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);
        var px = new byte[w * h * 4];
        fmtSrc.CopyPixels(px, w * 4, 0);

        // Some shell icons are resized by Windows from a much smaller source and pick up a
        // faint halo (alpha in the tens, not zero) along the outer edge of the requested canvas.
        // A low threshold here counts that halo as "content" and the icon never gets cropped
        // and enlarged, so this only counts pixels that are meaningfully opaque.
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (int y = 0; y < h; y++)
        {
            int row = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                if (px[row + x * 4 + 3] <= 127) continue;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }
        if (maxX < 0) return src;

        double boxW = maxX - minX + 1, boxH = maxY - minY + 1;
        double k = size * fill / Math.Max(boxW, boxH);
        double cx = (minX + boxW / 2) * k, cy = (minY + boxH / 2) * k;

        var visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(src, new Rect(size / 2.0 - cx, size / 2.0 - cy, w * k, h * k));
        }
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }
}
