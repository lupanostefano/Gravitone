using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Gravitone.Interop;
using static Gravitone.Interop.NativeMethods;

namespace Gravitone.UI;

/// <summary>
/// Context menu for a window that never activates. Such a menu gets no focus and WPF does not
/// dismiss it on outside clicks, so it is given the foreground while open (like a tray menu),
/// watched for clicks elsewhere, and the previous foreground window gets its focus back.
/// </summary>
internal sealed class DockMenu
{
    readonly ContextMenu _menu = new() { Placement = PlacementMode.MousePoint };
    readonly DispatcherTimer _outsideClickWatch = new() { Interval = TimeSpan.FromMilliseconds(40) };
    IntPtr _ownerHwnd, _previousForeground;
    bool _actionTaken;

    public DockMenu()
    {
        _outsideClickWatch.Tick += (_, _) => CloseOnOutsideClick();
        _menu.Closed += (_, _) =>
        {
            _outsideClickWatch.Stop();
            // Hand the focus back unless a command moved it somewhere on purpose.
            if (!_actionTaken && GetForegroundWindow() == _ownerHwnd && _previousForeground != IntPtr.Zero)
                SetForegroundWindow(_previousForeground);
            Closed?.Invoke();
        };
    }

    public event Action? Closed;

    public void Add(string header, Action action, bool isChecked = false, bool enabled = true, string? gesture = null) =>
        Root.Add(header, action, isChecked, enabled, gesture);

    public void AddSeparator() => Root.AddSeparator();

    /// <summary>A greyed-out caption above a group of items.</summary>
    public void AddHeader(string text) => Root.AddHeader(text);

    /// <summary>An item that opens a submenu; fill the returned section.</summary>
    public Section AddSubmenu(string header) => Root.AddSubmenu(header);

    Section Root => _root ??= new Section(this, _menu.Items);
    Section? _root;

    /// <summary>Items of the menu or of one of its submenus.</summary>
    public sealed class Section(DockMenu owner, ItemCollection items)
    {
        public void Add(string header, Action action, bool isChecked = false, bool enabled = true, string? gesture = null)
        {
            // "_" marks an access key in WPF menus; window titles must show it literally.
            var item = new MenuItem
            {
                Header = header.Replace("_", "__"),
                IsChecked = isChecked,
                IsEnabled = enabled,
                InputGestureText = gesture ?? "",
            };
            item.Click += (_, _) =>
            {
                owner._actionTaken = true;
                action();
            };
            items.Add(item);
        }

        public void AddSeparator()
        {
            if (items.Count > 0 && items[^1] is not Separator) items.Add(new Separator());
        }

        public void AddHeader(string text) =>
            items.Add(new MenuItem { Header = text.Replace("_", "__"), IsEnabled = false, FontSize = 11 });

        public Section AddSubmenu(string header)
        {
            var item = new MenuItem { Header = header.Replace("_", "__") };
            items.Add(item);
            return new Section(owner, item.Items);
        }
    }

    public void Show(Window owner, IntPtr ownerHwnd, IntPtr previousForeground)
    {
        if (_menu.Items.Count > 0 && _menu.Items[^1] is Separator) _menu.Items.RemoveAt(_menu.Items.Count - 1);
        _ownerHwnd = ownerHwnd;
        _previousForeground = previousForeground;
        SetForegroundWindow(ownerHwnd); // allowed: the right click was the last input
        _menu.PlacementTarget = owner;
        _menu.IsOpen = true;
        _outsideClickWatch.Start();
    }

    void CloseOnOutsideClick()
    {
        if (!_menu.IsOpen) return;
        bool down = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0
                    || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0
                    || (GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0;
        if (!down || !GetCursorPos(out var p) || IsOverMenu(p)) return;
        _menu.IsOpen = false;
    }

    bool IsOverMenu(POINT p) => IsOver(_menu, _menu.Items, p);

    /// <summary>The menu's own rectangle or that of an open submenu (a separate popup window).</summary>
    static bool IsOver(FrameworkElement popupContent, ItemCollection items, POINT p)
    {
        if (popupContent.IsLoaded && PresentationSource.FromVisual(popupContent) is not null)
        {
            var topLeft = popupContent.PointToScreen(new Point(0, 0));
            var bottomRight = popupContent.PointToScreen(new Point(popupContent.ActualWidth, popupContent.ActualHeight));
            if (p.X >= topLeft.X && p.X <= bottomRight.X && p.Y >= topLeft.Y && p.Y <= bottomRight.Y) return true;
        }
        foreach (var entry in items)
        {
            if (entry is not MenuItem { HasItems: true, IsSubmenuOpen: true } item) continue;
            var popup = item.Template?.FindName("PART_Popup", item) as Popup;
            if (popup?.Child is FrameworkElement child && IsOver(child, item.Items, p)) return true;
        }
        return false;
    }
}
