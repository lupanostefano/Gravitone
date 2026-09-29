using System.Runtime.InteropServices;
using Gravitone.Interop;
using static Gravitone.Interop.NativeMethods;

namespace Gravitone.Core;

/// <summary>A window's pixels, taken just before it minimizes (premultiplied BGRA, top-down) and where it was.</summary>
internal sealed class GenieCapture
{
    public int Width, Height;
    public required uint[] Pixels;
    /// <summary>The visible window in screen pixels (without the invisible resize borders).</summary>
    public RECT Bounds;

    /// <summary>Why the last <see cref="From"/> returned null (for the log).</summary>
    public static string LastFailure { get; private set; } = "";

    public static unsafe GenieCapture? From(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var outer)) return Fail("invalid window");
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT frame, Marshal.SizeOf<RECT>()) != 0) frame = outer;
        int ow = outer.Width, oh = outer.Height;
        if (ow < 60 || oh < 40 || ow > 10000 || oh > 10000) return Fail($"size {ow}x{oh}");

        var screen = GetDC(IntPtr.Zero);
        var dc = CreateCompatibleDC(screen);
        var bmi = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = ow, biHeight = -oh, biPlanes = 1, biBitCount = 32 },
        };
        var dib = CreateDIBSection(screen, ref bmi, DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
        ReleaseDC(IntPtr.Zero, screen);
        if (dib == IntPtr.Zero)
        {
            DeleteDC(dc);
            return Fail("out of memory for the capture");
        }
        var old = SelectObject(dc, dib);
        try
        {
            // Zero the surface first: a failed or partial capture then shows as empty and is refused below.
            new Span<byte>((void*)bits, ow * oh * 4).Clear();
            if (!PrintWindow(hwnd, dc, PW_RENDERFULLCONTENT)) return Fail($"PrintWindow failed with error {Marshal.GetLastWin32Error()}");

            int fx = Math.Clamp(frame.Left - outer.Left, 0, ow - 1), fy = Math.Clamp(frame.Top - outer.Top, 0, oh - 1);
            int fw = Math.Min(frame.Width, ow - fx), fh = Math.Min(frame.Height, oh - fy);
            if (fw < 40 || fh < 30) return Fail($"visible area {fw}x{fh}");

            var pixels = new uint[fw * fh];
            var src = (uint*)bits;
            bool any = false;
            for (int y = 0; y < fh; y++)
            {
                var row = src + (fy + y) * ow + fx;
                for (int x = 0; x < fw; x++)
                {
                    uint p = row[x] & 0x00FFFFFF;
                    any |= p != 0;
                    pixels[y * fw + x] = p | 0xFF000000;
                }
            }
            if (!any) return Fail("empty capture (protected or not-yet-painted window)");

            if (!IsZoomed(hwnd)) RoundCorners(pixels, fw, fh, 8 * Math.Max(1.0, GetDpiForWindow(hwnd) / 96.0));
            return new GenieCapture
            {
                Width = fw,
                Height = fh,
                Pixels = pixels,
                Bounds = new RECT(frame.Left, frame.Top, frame.Left + fw, frame.Top + fh),
            };
        }
        finally
        {
            SelectObject(dc, old);
            DeleteObject(dib);
            DeleteDC(dc);
        }
    }

    static GenieCapture? Fail(string why)
    {
        LastFailure = why;
        return null;
    }

    /// <summary>Windows 11 rounds the corners of its windows; the capture has square ones.</summary>
    static void RoundCorners(uint[] px, int w, int h, double radius)
    {
        int r = (int)Math.Ceiling(radius);
        for (int y = 0; y < r; y++)
        {
            for (int x = 0; x < r; x++)
            {
                double dx = r - x - 0.5, dy = r - y - 0.5;
                double coverage = Math.Clamp(radius + 0.5 - Math.Sqrt(dx * dx + dy * dy), 0, 1);
                if (coverage >= 1) continue;
                Fade(px, y * w + x, coverage);
                Fade(px, y * w + (w - 1 - x), coverage);
                Fade(px, (h - 1 - y) * w + x, coverage);
                Fade(px, (h - 1 - y) * w + (w - 1 - x), coverage);
            }
        }
    }

    static void Fade(uint[] px, int i, double k)
    {
        uint p = px[i];
        uint a = (uint)(255 * k);
        uint r = (uint)(((p >> 16) & 0xFF) * k), g = (uint)(((p >> 8) & 0xFF) * k), b = (uint)((p & 0xFF) * k);
        px[i] = (a << 24) | (r << 16) | (g << 8) | b;
    }
}


