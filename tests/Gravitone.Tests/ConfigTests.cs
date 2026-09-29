using Gravitone.Core;

namespace Gravitone.Tests;

public class ConfigTests
{
    [Theory]
    [InlineData(4, 16)]
    [InlineData(500, 128)]
    [InlineData(48, 48)]
    public void Icon_sizes_are_kept_in_a_usable_range(double given, double expected)
    {
        var s = new DockSettings { IconSize = given, MagnifiedSize = 10 };
        s.Clamp();
        Assert.Equal(expected, s.IconSize);
        Assert.True(s.MagnifiedSize >= s.IconSize, "the magnified size is never smaller than the resting one");
    }

    [Fact]
    public void Defaults_replace_the_taskbar_and_keep_the_safety_nets_on()
    {
        var s = new DockSettings();
        Assert.True(s.ReplaceTaskbar);
        Assert.Equal("Ctrl+Alt+Shift+B", s.TaskbarHotkey);
        Assert.Equal(DockEdge.Bottom, s.Edge);
        Assert.Equal("", s.Language);
        Assert.Equal("", s.Display);
    }

    [Theory]
    [InlineData("gravitone:start", true)]
    [InlineData("windock:start", true)] // configurations from before the rename
    [InlineData("GRAVITONE:START", true)]
    [InlineData(@"C:\Windows\explorer.exe", false)]
    public void The_start_item_is_recognised_under_its_old_and_new_name(string target, bool expected)
    {
        Assert.Equal(expected, StartMenu.IsTarget(target));
    }
}
