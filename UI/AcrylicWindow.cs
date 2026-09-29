using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Gravitone.Core;
using Gravitone.Interop;
using static Gravitone.Interop.NativeMethods;

namespace Gravitone.UI;

/// <summary>
/// A non-layered, never-activated window with the real Windows 11 acrylic backdrop from DWM
/// (layered windows cannot have it). WPF content drawn on it sits on top of the glass.
/// </summary>
internal abstract class AcrylicWindow : Window
{
    readonly bool _rounded;

    protected AcrylicWindow(bool rounded)
    {
        _rounded = rounded;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
        Width = 10;
        Height = 10;
        Background = Brushes.Transparent;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            GlassFrameThickness = new Thickness(-1),
            ResizeBorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
    }

    public IntPtr Handle { get; private set; }

    RECT _bounds;

    /// <summary>Moves the glass, in physical screen pixels.</summary>
    public void SetBounds(RECT r)
    {
        if (Handle == IntPtr.Zero || r.Equals(_bounds)) return;
        _bounds = r;
        SetWindowPos(Handle, IntPtr.Zero, r.Left, r.Top, Math.Max(1, r.Width), Math.Max(1, r.Height),
            SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOOWNERZORDER);
    }

    /// <summary>After sleep, unlock or a display change DWM may draw the glass as inactive (a flat colour).</summary>
    public void Reactivate()
    {
        if (Handle != IntPtr.Zero) SendMessage(Handle, WM_NCACTIVATE, 1, IntPtr.Zero);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Handle = new WindowInteropHelper(this).Handle;
        // Moved onto a monitor with another scale, WPF resizes the window to what it thinks it should be.
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            var wanted = _bounds;
            _bounds = default;
            if (wanted.Width > 0) SetBounds(wanted);
        });
        MakeToolWindow(Handle);
        // Without a system menu DWM draws no caption buttons on the glass.
        long style = (long)GetWindowLongPtr(Handle, GWL_STYLE);
        SetWindowLongPtr(Handle, GWL_STYLE, (IntPtr)(style & ~(WS_SYSMENU | WS_MINIMIZEBOX | WS_MAXIMIZEBOX)));

        var source = HwndSource.FromHwnd(Handle)!;
        source.CompositionTarget.BackgroundColor = Colors.Transparent;
        source.AddHook(AcrylicWndProc);

        var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        DwmExtendFrameIntoClientArea(Handle, ref margins);
        int corner = _rounded ? DWMWCP_ROUND : DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
        int backdrop = DWMSBT_TRANSIENTWINDOW;
        DwmSetWindowAttribute(Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        ApplyTheme();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        // DWM swaps acrylic for a flat colour on inactive windows, and this one is never activated.
        SendMessage(Handle, WM_NCACTIVATE, 1, IntPtr.Zero);
    }

    /// <summary>Dark or light glass; <paramref name="border"/> is a COLORREF (0x00BBGGRR) or DWMWA_COLOR_NONE.</summary>
    protected void ApplyGlassTheme(int border)
    {
        if (Handle == IntPtr.Zero) return;
        int dark = Theme.IsDark ? 1 : 0;
        DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        DwmSetWindowAttribute(Handle, DWMWA_BORDER_COLOR, ref border, sizeof(int));
    }

    public abstract void ApplyTheme();

    IntPtr AcrylicWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_MOUSEACTIVATE:
                handled = true;
                return MA_NOACTIVATE;
            case WM_NCACTIVATE when wParam == IntPtr.Zero:
                // Keep the frame (and so the acrylic) in its active look.
                handled = true;
                return DefWindowProc(hwnd, msg, 1, lParam);
            case WM_WINDOWPOSCHANGING:
                // Every resize is clamped to the minimum size of a captioned window (39 px high at
                // 100%; WPF records it before any hook can lower it): the glass must be free to be
                // thinner (menu bar) or tiny (dock plate). WPF's layout then no longer matches the
                // window, so these windows hold nothing but a tint; content goes in a window above.
                handled = true;
                return IntPtr.Zero;
            case WM_GETMINMAXINFO:
                // The same limit when the system resizes the window.
                var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                info.ptMinTrackSize = new POINT { X = 1, Y = 1 };
                Marshal.StructureToPtr(info, lParam, false);
                handled = true;
                return IntPtr.Zero;
        }
        return IntPtr.Zero;
    }
}
