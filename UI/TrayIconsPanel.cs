using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Gravitone.Core;
using Gravitone.Interop;
using ManagedShell.WindowsTray;
using static Gravitone.Interop.NativeMethods;
using MsNative = ManagedShell.Interop.NativeMethods;

namespace Gravitone.UI;

/// <summary>
/// Third-party notification area icons, received through ManagedShell's tray service (which
/// forwards everything to Explorer as well, so its own tray stays complete). Clicks, double
/// clicks and hovers go to the owning app exactly as Explorer would send them.
/// </summary>
internal sealed class TrayIconsPanel : StackPanel, IDisposable
{
    // Explorer's own icons: the menu bar draws Wi-Fi, volume and battery itself.
    static readonly HashSet<Guid> SystemIcons =
    [
        new(NotificationArea.NETWORK_GUID), new(NotificationArea.POWER_GUID), new(NotificationArea.VOLUME_GUID),
    ];

    readonly TrayService _trayService = new();
    readonly NotificationArea _area;
    // By reference: NotifyIcon equality compares its (changing) window handle, id and GUID.
    readonly Dictionary<NotifyIcon, Border> _views = new(ReferenceEqualityComparer.Instance);
    readonly HashSet<NotifyIcon> _subscribed = new(ReferenceEqualityComparer.Instance);
    bool _disposed;

    public TrayIconsPanel()
    {
        Orientation = Orientation.Horizontal;
        _area = new NotificationArea(NotificationArea.DEFAULT_PINNED, _trayService, new ExplorerTrayService())
        {
            // The default is one collection shared by every NotificationArea: after the menu bar is
            // turned off and on it would still hold the icons of apps closed in between.
            TrayIcons = [],
        };
        _area.TrayIcons.CollectionChanged += OnCollectionChanged;
        _area.SetAppBarMessageCallback(AppBarRelay.Handle);
        // Broadcasts TaskbarCreated: every app re-adds its icon, now to us first.
        _area.Initialize();
        if (_area.IsFailed) Log.Info("Notification area: failed to start");
        Sync();
    }

    /// <summary>Tells the tray where it lives, so apps place their menus and flyouts next to it.</summary>
    public void SetHostRect(RECT bar)
    {
        _area.SetTrayHostSizeData(new TrayHostSizeData
        {
            edge = MsNative.ABEdge.ABE_TOP,
            rc = new MsNative.Rect { Left = bar.Left, Top = bar.Top, Right = bar.Right, Bottom = bar.Bottom },
        });
    }

    public Brush HoverBrush { get; set; } = Brushes.Transparent;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _area.TrayIcons.CollectionChanged -= OnCollectionChanged;
        foreach (var icon in _subscribed) icon.PropertyChanged -= OnIconChanged;
        _subscribed.Clear();
        _views.Clear();
        _area.Dispose();
    }

    void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Sync();

    void OnIconChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not NotifyIcon icon) return;
        switch (e.PropertyName)
        {
            case nameof(NotifyIcon.Icon) when _views.TryGetValue(icon, out var view):
                ((Image)view.Child).Source = icon.Icon;
                break;
            case nameof(NotifyIcon.Title) when _views.TryGetValue(icon, out var view):
                view.ToolTip = Tip(icon);
                break;
            case nameof(NotifyIcon.IsHidden):
                Sync();
                break;
        }
    }

    /// <summary>Brings the row of views in line with the tray's icons, keeping existing views (and their hover state).</summary>
    void Sync()
    {
        var wanted = _area.TrayIcons.Where(i => !i.IsHidden && !SystemIcons.Contains(i.GUID)).ToList();

        // Hidden icons stay subscribed: they can be shown again (NIS_HIDDEN cleared).
        foreach (var icon in _area.TrayIcons)
            if (_subscribed.Add(icon)) icon.PropertyChanged += OnIconChanged;
        foreach (var gone in _subscribed.Where(i => !_area.TrayIcons.Contains(i)).ToList())
        {
            gone.PropertyChanged -= OnIconChanged;
            _subscribed.Remove(gone);
        }
        foreach (var gone in _views.Keys.Where(i => !wanted.Contains(i)).ToList()) _views.Remove(gone);

        Children.Clear();
        foreach (var icon in wanted)
        {
            if (!_views.TryGetValue(icon, out var view)) _views[icon] = view = CreateView(icon);
            Children.Add(view);
        }
    }

    Border CreateView(NotifyIcon icon)
    {
        var image = new Image { Source = icon.Icon, Width = 16, Height = 16, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var view = new Border
        {
            Child = image,
            Padding = new Thickness(5, 0, 5, 0),
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            VerticalAlignment = VerticalAlignment.Stretch,
            ToolTip = Tip(icon),
        };
        ToolTipService.SetInitialShowDelay(view, 600);

        view.MouseEnter += (_, _) =>
        {
            view.Background = HoverBrush;
            SetPlacement(icon, view);
            icon.IconMouseEnter(MouseParam());
        };
        view.MouseLeave += (_, _) =>
        {
            view.Background = Brushes.Transparent;
            icon.IconMouseLeave(MouseParam());
        };
        view.MouseMove += (_, _) => icon.IconMouseMove(MouseParam());
        view.MouseDown += (_, e) =>
        {
            SetPlacement(icon, view);
            icon.IconMouseDown(e.ChangedButton, MouseParam(), (int)GetDoubleClickTime());
            e.Handled = true;
        };
        view.MouseUp += (_, e) =>
        {
            icon.IconMouseUp(e.ChangedButton, MouseParam(), (int)GetDoubleClickTime());
            e.Handled = true;
        };
        return view;
    }

    static string? Tip(NotifyIcon icon) => string.IsNullOrWhiteSpace(icon.Title) ? null : icon.Title;

    /// <summary>The icon's rectangle on screen, in physical pixels (Shell_NotifyIconGetRect answers with it).</summary>
    void SetPlacement(NotifyIcon icon, FrameworkElement view)
    {
        if (PresentationSource.FromVisual(view) is null) return;
        var topLeft = view.PointToScreen(new Point(0, 0));
        var bottomRight = view.PointToScreen(new Point(view.ActualWidth, view.ActualHeight));
        icon.Placement = new MsNative.Rect
        {
            Left = (int)topLeft.X,
            Top = (int)topLeft.Y,
            Right = (int)bottomRight.X,
            Bottom = (int)bottomRight.Y,
        };
    }

    /// <summary>The cursor position packed as the tray callbacks expect it (x low word, y high word).</summary>
    static uint MouseParam()
    {
        GetCursorPos(out var p);
        return (uint)(((p.Y & 0xFFFF) << 16) | (p.X & 0xFFFF));
    }
}
