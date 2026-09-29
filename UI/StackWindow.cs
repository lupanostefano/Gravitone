using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Gravitone.Core;
using Gravitone.Interop;
using static Gravitone.Interop.NativeMethods;

namespace Gravitone.UI;

/// <summary>
/// The stack of a folder: its newest items in a grid, opened above the dock icon (Downloads, for
/// instance). A window that never activates, closed by a click anywhere else, like the dock's menus.
/// </summary>
internal sealed class StackWindow : Window
{
    const int MaxItems = 12;
    const double TileWidth = 96;

    readonly DockItem _item;
    readonly DispatcherTimer _outsideClickWatch = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly FrameworkElement _root;
    IntPtr _hwnd;
    bool _closed;

    public StackWindow(DockItem item, RECT anchor, double scale, DockEdge edge, Action<bool> closed)
    {
        _item = item;
        Closed += (_, _) => closed(_byOutsideClick);

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _root = BuildContent(item);
        Content = new Border { Padding = new Thickness(12), Child = _root }; // room for the shadow

        // Place it next to the icon, in physical pixels: WPF's Left/Top are in the units of the monitor the
        // window starts on, wrong when the dock's monitor has another scale. Moved there before it is
        // shown, the window takes that monitor's scale and SizeToContent gives the size computed here.
        var measured = (Content as FrameworkElement)!;
        measured.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = measured.DesiredSize.Width * scale, h = measured.DesiredSize.Height * scale;
        var monitor = MonitorFromRect(anchor);
        double cx = (anchor.Left + anchor.Right) / 2.0, cy = (anchor.Top + anchor.Bottom) / 2.0;
        double gap = 2 * scale, x, y;
        switch (edge)
        {
            case DockEdge.Left:
                x = anchor.Right + gap;
                y = cy - h / 2;
                break;
            case DockEdge.Right:
                x = anchor.Left - w - gap;
                y = cy - h / 2;
                break;
            default:
                x = cx - w / 2;
                y = anchor.Top - h - gap;
                break;
        }
        x = Math.Clamp(x, monitor.Left, Math.Max(monitor.Left, monitor.Right - w));
        y = Math.Clamp(y, monitor.Top, Math.Max(monitor.Top, monitor.Bottom - h));
        Left = -32000; // off screen until SourceInitialized moves it
        Top = -32000;

        _outsideClickWatch.Tick += (_, _) => CloseOnOutsideClick();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            MakeToolWindow(_hwnd);
            SetWindowPos(_hwnd, IntPtr.Zero, (int)Math.Round(x), (int)Math.Round(y), 0, 0,
                SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        };
        Loaded += (_, _) => _outsideClickWatch.Start();
    }

    bool _byOutsideClick;

    public DockItem Item => _item;

    public new void Close()
    {
        if (_closed) return;
        _closed = true;
        _outsideClickWatch.Stop();
        base.Close();
    }

    static RECT MonitorFromRect(RECT r)
    {
        var monitor = MonitorFromPoint(new POINT { X = (r.Left + r.Right) / 2, Y = (r.Top + r.Bottom) / 2 }, MONITOR_DEFAULTTOPRIMARY);
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        return GetMonitorInfo(monitor, ref info) ? info.rcWork : r;
    }

    void CloseOnOutsideClick()
    {
        if (_closed) return;
        bool down = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0
                    || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0
                    || (GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0;
        if (!down || !GetCursorPos(out var p) || !GetWindowRect(_hwnd, out var r)) return;
        if (p.X >= r.Left && p.X <= r.Right && p.Y >= r.Top && p.Y <= r.Bottom) return;
        _byOutsideClick = true;
        Close();
    }

    FrameworkElement BuildContent(DockItem item)
    {
        bool dark = Theme.IsDark;
        var text = new SolidColorBrush(dark ? Color.FromRgb(0xF5, 0xF5, 0xF7) : Color.FromRgb(0x1D, 0x1D, 0x1F));
        var subtle = new SolidColorBrush(dark ? Color.FromRgb(0xB4, 0xB4, 0xB8) : Color.FromRgb(0x5C, 0x5C, 0x60));
        var hover = new SolidColorBrush(dark ? Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1F, 0x00, 0x00, 0x00));
        var font = new FontFamily("Segoe UI Variable Text, Segoe UI");

        var entries = ReadEntries(Environment.ExpandEnvironmentVariables(item.Target));
        var panel = new StackPanel { Orientation = Orientation.Vertical };
        if (entries.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = Loc.T("This folder is empty"),
                Foreground = subtle,
                FontFamily = font,
                FontSize = 13,
                Margin = new Thickness(24, 20, 24, 20),
            });
        }
        else
        {
            var grid = new UniformGrid { Columns = Math.Min(4, entries.Count), Margin = new Thickness(8, 8, 8, 2) };
            foreach (var path in entries) grid.Children.Add(Tile(path, text, hover, font));
            panel.Children.Add(grid);
        }

        var open = new TextBlock
        {
            Text = Loc.T("Open \"{0}\" in File Explorer", item.Name),
            Foreground = subtle,
            FontFamily = font,
            FontSize = 12,
            Margin = new Thickness(0, 6, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            Cursor = Cursors.Hand,
        };
        open.MouseEnter += (_, _) => open.Foreground = text;
        open.MouseLeave += (_, _) => open.Foreground = subtle;
        open.MouseLeftButtonUp += (_, _) => Open(Environment.ExpandEnvironmentVariables(item.Target));
        panel.Children.Add(open);

        return new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(dark ? Color.FromArgb(0xEE, 0x26, 0x26, 0x28) : Color.FromArgb(0xF0, 0xF3, 0xF3, 0xF5)),
            BorderBrush = new SolidColorBrush(dark ? Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x26, 0x00, 0x00, 0x00)),
            BorderThickness = new Thickness(1),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.32, Direction = 270 },
            Child = panel,
        };
    }

    FrameworkElement Tile(string path, Brush text, Brush hover, FontFamily font)
    {
        var image = new Image { Source = ShellItems.Icon(path), Width = 56, Height = 56, Margin = new Thickness(0, 4, 0, 4) };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var name = new TextBlock
        {
            Text = Path.GetFileName(path),
            Foreground = text,
            FontFamily = font,
            FontSize = 11.5,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 32,
        };
        var stack = new StackPanel { Width = TileWidth - 12, Margin = new Thickness(6, 6, 6, 6) };
        stack.Children.Add(image);
        stack.Children.Add(name);
        var tile = new Border { CornerRadius = new CornerRadius(8), Background = Brushes.Transparent, Child = stack, Cursor = Cursors.Hand, ToolTip = path };
        tile.MouseEnter += (_, _) => tile.Background = hover;
        tile.MouseLeave += (_, _) => tile.Background = Brushes.Transparent;
        tile.MouseLeftButtonUp += (_, _) =>
        {
            Open(path);
            Close();
        };
        return tile;
    }

    /// <summary>The newest files and folders of a folder, skipping hidden and system ones.</summary>
    static List<string> ReadEntries(string folder)
    {
        try
        {
            return new DirectoryInfo(folder).EnumerateFileSystemInfos()
                .Where(e => (e.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                .OrderByDescending(e => e.LastWriteTimeUtc)
                .Take(MaxItems)
                .Select(e => e.FullName)
                .ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Reading {folder}");
            return [];
        }
    }

    static void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Opening {path}");
        }
    }
}
