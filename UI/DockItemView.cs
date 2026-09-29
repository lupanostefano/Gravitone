using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Gravitone.Core;

namespace Gravitone.UI;

/// <summary>
/// The visuals of one dock entry: the icon, the pressed shade and the running-app dot,
/// or the thin line of a separator.
/// </summary>
internal sealed class DockItemView
{
    readonly Image? _image;
    readonly Rectangle? _shade;
    readonly Ellipse? _dot;
    readonly Rectangle? _line;

    public DockItemView(DockItem item)
    {
        Item = item;
        if (item.Kind == DockItemKind.Separator)
        {
            _line = new Rectangle { IsHitTestVisible = false };
            Refresh();
            return;
        }

        _image = new Image { Source = item.Icon, Stretch = Stretch.Uniform, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);

        // Darkens only the icon's own pixels while pressed, like the Dock.
        _shade = new Rectangle
        {
            Fill = Brushes.Black,
            Opacity = 0.38,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
        };
        if (item.Icon is not null) _shade.OpacityMask = new ImageBrush(item.Icon) { Stretch = Stretch.Uniform };

        _dot = new Ellipse { IsHitTestVisible = false };
        Refresh();
    }

    public DockItem Item { get; }

    public void AddTo(Panel panel)
    {
        foreach (var e in Elements()) panel.Children.Add(e);
    }

    public void RemoveFrom(Panel panel)
    {
        foreach (var e in Elements()) panel.Children.Remove(e);
    }

    /// <param name="main">The icon rectangle, or the separator line.</param>
    public void Arrange(Rect main, Rect dot, double opacity)
    {
        if (_line is not null)
        {
            Place(_line, main);
            _line.Opacity = opacity;
            return;
        }
        Place(_image!, main);
        Place(_shade!, main);
        Place(_dot!, dot);
        _image!.Opacity = opacity;
        _dot!.Opacity = opacity;
    }

    /// <summary>Draws above the other icons (the one being dragged).</summary>
    public void SetTopmost(bool topmost)
    {
        foreach (var e in Elements()) Panel.SetZIndex(e, topmost ? 500 : 0);
    }

    public void SetPressed(bool pressed)
    {
        if (_shade is not null) _shade.Visibility = pressed ? Visibility.Visible : Visibility.Collapsed;
    }

    // Refresh runs for every icon whenever a window opens, closes or comes to the front: no new brushes each time.
    static readonly Brush LineDark = Frozen(Color.FromArgb(0x4D, 0xFF, 0xFF, 0xFF));
    static readonly Brush LineLight = Frozen(Color.FromArgb(0x38, 0x00, 0x00, 0x00));
    static readonly Brush DotDark = Frozen(Color.FromArgb(0xE0, 0xF2, 0xF2, 0xF2));
    static readonly Brush DotLight = Frozen(Color.FromArgb(0xB8, 0x1E, 0x1E, 0x1E));

    static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public void Refresh()
    {
        if (_line is not null)
        {
            _line.Fill = Theme.IsDark ? LineDark : LineLight;
            return;
        }
        if (!ReferenceEquals(_image!.Source, Item.Icon))
        {
            _image.Source = Item.Icon;
            _shade!.OpacityMask = Item.Icon is null ? null : new ImageBrush(Item.Icon) { Stretch = Stretch.Uniform };
        }
        _dot!.Visibility = Item.IsRunning ? Visibility.Visible : Visibility.Collapsed;
        _dot.Fill = Theme.IsDark ? DotDark : DotLight;
    }

    IEnumerable<UIElement> Elements()
    {
        if (_line is not null)
        {
            yield return _line;
            yield break;
        }
        yield return _image!;
        yield return _shade!;
        yield return _dot!;
    }

    static void Place(FrameworkElement e, Rect r)
    {
        Canvas.SetLeft(e, r.X);
        Canvas.SetTop(e, r.Y);
        e.Width = Math.Max(0, r.Width);
        e.Height = Math.Max(0, r.Height);
    }
}
