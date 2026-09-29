using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Gravitone.Core;

namespace Gravitone.UI;

/// <summary>The settings of the dock. Every change is applied and saved as soon as it is made.</summary>
internal sealed class SettingsWindow : Window
{
    readonly DockSettings _settings;
    readonly Action _apply;
    readonly Brush _text, _subtle;
    readonly FontFamily _font = new("Segoe UI Variable Text, Segoe UI");
    bool _loading = true;

    public SettingsWindow(DockSettings settings, Action apply)
    {
        _settings = settings;
        _apply = apply;
        bool dark = Theme.IsDark;
        _text = new SolidColorBrush(dark ? Color.FromRgb(0xF5, 0xF5, 0xF7) : Color.FromRgb(0x1D, 0x1D, 0x1F));
        _subtle = new SolidColorBrush(dark ? Color.FromRgb(0xB4, 0xB4, 0xB8) : Color.FromRgb(0x5C, 0x5C, 0x60));

        Title = Loc.T("{0} Settings", AppInfo.Name);
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(dark ? Color.FromRgb(0x20, 0x20, 0x22) : Color.FromRgb(0xF6, 0xF6, 0xF8));
        FontFamily = _font;
        Foreground = _text;

        var panel = new StackPanel { Margin = new Thickness(24, 8, 24, 20) };
        panel.Children.Add(Heading(Loc.T("Appearance")));
        panel.Children.Add(SliderRow(Loc.T("Icon size"), 24, 96, _settings.IconSize, v => _settings.IconSize = v));
        panel.Children.Add(Check(Loc.T("Magnify icons under the pointer"), _settings.Magnification, v => _settings.Magnification = v));
        panel.Children.Add(SliderRow(Loc.T("Magnified size"), 48, 160, _settings.MagnifiedSize, v => _settings.MagnifiedSize = v));
        panel.Children.Add(Check(Loc.T("Genie effect when minimizing and restoring"), _settings.Genie, v => _settings.Genie = v));

        panel.Children.Add(Heading(Loc.T("Position")));
        panel.Children.Add(EdgeRow());
        panel.Children.Add(Check(Loc.T("Hide automatically"), _settings.AutoHide, v => _settings.AutoHide = v));
        panel.Children.Add(DisplayRow());
        panel.Children.Add(Check(Loc.T("Follow the pointer to the bottom edge of another display"), _settings.FollowPointer, v => _settings.FollowPointer = v));

        panel.Children.Add(Heading(Loc.T("System")));
        panel.Children.Add(Check(Loc.T("Replace the Windows taskbar"), _settings.ReplaceTaskbar, v => _settings.ReplaceTaskbar = v));
        panel.Children.Add(Check(Loc.T("Menu bar on top"), _settings.MenuBar, v => _settings.MenuBar = v));
        panel.Children.Add(Check(Loc.T("Menu bar on every display"), _settings.MenuBarOnAllDisplays, v => _settings.MenuBarOnAllDisplays = v));
        panel.Children.Add(Check(Loc.T("Start with Windows"), _settings.StartWithWindows, v => _settings.StartWithWindows = v));
        panel.Children.Add(Check(Loc.T("Win+1 … Win+9 open the Dock's apps"), _settings.NumberShortcuts, v => _settings.NumberShortcuts = v));
        panel.Children.Add(LanguageRow());
        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("Show or hide the Windows taskbar: {0}", _settings.TaskbarHotkey.Replace("+", " + ")),
            Foreground = _subtle,
            FontSize = 12,
            Margin = new Thickness(0, 10, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        });

        Content = panel;
        _loading = false;
    }

    TextBlock Heading(string text) => new()
    {
        Text = text,
        FontSize = 15,
        FontWeight = FontWeights.SemiBold,
        Foreground = _text,
        Margin = new Thickness(0, 14, 0, 6),
    };

    CheckBox Check(string text, bool value, Action<bool> set)
    {
        var box = new CheckBox { Content = text, IsChecked = value, Foreground = _text, Margin = new Thickness(0, 5, 0, 5) };
        void Changed(object? sender, RoutedEventArgs e)
        {
            if (_loading) return;
            set(box.IsChecked == true);
            _apply();
        }
        box.Checked += Changed;
        box.Unchecked += Changed;
        return box;
    }

    UIElement SliderRow(string label, double min, double max, double value, Action<double> set)
    {
        var grid = new Grid { Margin = new Thickness(0, 5, 0, 5) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        var name = new TextBlock { Text = label, Foreground = _text, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), SmallChange = 1, LargeChange = 8, VerticalAlignment = VerticalAlignment.Center };
        var number = new TextBlock { Text = ((int)slider.Value).ToString(), Foreground = _subtle, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        slider.ValueChanged += (_, e) =>
        {
            number.Text = ((int)Math.Round(e.NewValue)).ToString();
            if (_loading) return;
            set(Math.Round(e.NewValue));
            _apply();
        };
        Grid.SetColumn(slider, 1);
        Grid.SetColumn(number, 2);
        grid.Children.Add(name);
        grid.Children.Add(slider);
        grid.Children.Add(number);
        return grid;
    }

    /// <summary>The interface language. Menus change at once, this window when it is opened again.</summary>
    UIElement LanguageRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 5) };
        row.Children.Add(new TextBlock { Text = Loc.T("Language"), Foreground = _text, Width = 190, VerticalAlignment = VerticalAlignment.Center });
        var names = new[] { Loc.T("Same as Windows"), "English", "Italiano" };
        var combo = new ComboBox { Width = 210, VerticalAlignment = VerticalAlignment.Center };
        foreach (var name in names) combo.Items.Add(name);
        combo.SelectedIndex = Math.Max(0, Array.IndexOf(Loc.Languages, _settings.Language ?? ""));
        combo.SelectionChanged += (_, _) =>
        {
            if (_loading || combo.SelectedIndex < 0) return;
            _settings.Language = Loc.Languages[combo.SelectedIndex];
            _apply();
        };
        row.Children.Add(combo);
        return row;
    }

    /// <summary>The monitor the dock lives on: the primary (whichever it is) or a given one.</summary>
    UIElement DisplayRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
        row.Children.Add(new TextBlock { Text = Loc.T("Dock display"), Foreground = _text, Width = 190, VerticalAlignment = VerticalAlignment.Center });
        var choices = new List<(string Label, string Device)> { (Loc.T("Primary"), "") };
        var displays = Displays.All();
        for (int i = 0; i < displays.Count; i++) choices.Add((Displays.FriendlyName(displays[i], i), displays[i].DeviceName));
        if (_settings.Display.Length > 0 && !choices.Any(c => string.Equals(c.Device, _settings.Display, StringComparison.OrdinalIgnoreCase)))
            choices.Add((Loc.T("{0} (disconnected)", _settings.Display), _settings.Display));

        var combo = new ComboBox { Width = 210, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (label, _) in choices) combo.Items.Add(label);
        combo.SelectedIndex = Math.Max(0, choices.FindIndex(c => string.Equals(c.Device, _settings.Display, StringComparison.OrdinalIgnoreCase)));
        combo.SelectionChanged += (_, _) =>
        {
            if (_loading || combo.SelectedIndex < 0) return;
            _settings.Display = choices[combo.SelectedIndex].Device;
            _apply();
        };
        row.Children.Add(combo);
        return row;
    }

    UIElement EdgeRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 5) };
        row.Children.Add(new TextBlock { Text = Loc.T("Position on screen"), Foreground = _text, Width = 190, VerticalAlignment = VerticalAlignment.Center });
        var choices = new[] { (Loc.T("Bottom"), DockEdge.Bottom), (Loc.T("Left"), DockEdge.Left), (Loc.T("Right"), DockEdge.Right) };
        foreach (var (label, edge) in choices)
        {
            var radio = new RadioButton
            {
                Content = label,
                GroupName = "edge",
                IsChecked = _settings.Edge == edge,
                Foreground = _text,
                Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            radio.Checked += (_, _) =>
            {
                if (_loading) return;
                _settings.Edge = edge;
                _apply();
            };
            row.Children.Add(radio);
        }
        return row;
    }
}
