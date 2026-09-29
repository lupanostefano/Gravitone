using Gravitone.Core;

namespace Gravitone.Tests;

public class DockLayoutTests
{
    static readonly DockMetrics Metrics = new(48, 80, DockEdge.Bottom);

    static List<DockSlot> Icons(int n) => Enumerable.Repeat(new DockSlot(true, 1), n).ToList();

    [Fact]
    public void At_rest_the_dock_is_centred_and_every_icon_has_its_resting_size()
    {
        var layout = new DockLayout();
        layout.Compute(Metrics, Icons(8), 1920, pointer: null, amount: 0);

        Assert.All(layout.Sizes, s => Assert.Equal(48, s, 3));
        double left = layout.PlateStart, right = 1920 - layout.PlateEnd;
        Assert.Equal(left, right, 3);
    }

    [Fact]
    public void The_icon_under_the_pointer_grows_to_the_magnified_size_and_its_neighbours_less()
    {
        var layout = new DockLayout();
        var slots = Icons(9);
        layout.Compute(Metrics, slots, 1920, pointer: null, amount: 0);
        double centre = layout.Starts[4] + layout.Sizes[4] / 2;

        layout.Compute(Metrics, slots, 1920, pointer: centre, amount: 1);

        Assert.Equal(80, layout.Sizes[4], 1);
        Assert.True(layout.Sizes[3] < layout.Sizes[4] && layout.Sizes[3] > 48);
        Assert.True(layout.Sizes[0] < layout.Sizes[3]);
        Assert.Equal(4, layout.ItemAt(centre));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1920)]
    public void Magnified_near_a_screen_edge_the_plate_never_leaves_the_screen(double pointer)
    {
        var layout = new DockLayout();
        layout.Compute(Metrics, Icons(30), 1920, pointer, amount: 1);

        Assert.True(layout.PlateStart >= 0);
        Assert.True(layout.PlateEnd <= 1920 + 0.001);
    }

    [Fact]
    public void An_entry_fading_out_takes_no_room_once_gone()
    {
        var layout = new DockLayout();
        var slots = Icons(5);
        layout.Compute(Metrics, slots, 1920, null, 0);
        double full = layout.PlateEnd - layout.PlateStart;

        slots[2] = new DockSlot(true, 0);
        layout.Compute(Metrics, slots, 1920, null, 0);

        Assert.Equal(full - 48 - Metrics.Gap, layout.PlateEnd - layout.PlateStart, 3);
        Assert.Equal(0, layout.Sizes[2], 3);
    }

    [Fact]
    public void Separators_keep_their_width_when_magnified()
    {
        var layout = new DockLayout();
        var slots = new List<DockSlot> { new(true, 1), new(false, 1), new(true, 1) };
        layout.Compute(Metrics, slots, 1920, null, 0);
        double pointer = layout.Starts[1] + layout.Sizes[1] / 2;

        layout.Compute(Metrics, slots, 1920, pointer, 1);

        Assert.Equal(Metrics.SeparatorWidth, layout.Sizes[1], 3);
    }
}
