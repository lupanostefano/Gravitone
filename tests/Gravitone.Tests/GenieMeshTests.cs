using Gravitone.Core;
using Gravitone.Interop;

namespace Gravitone.Tests;

public class GenieMeshTests
{
    static readonly RECT Window = new(600, 150, 1200, 600);
    static readonly RECT Icon = new(900, 1020, 948, 1068);
    static readonly RECT Area = new(600, 150, 1200, 1068);

    static GenieMesh.Vertex[] Mesh(DockEdge edge, double p, RECT? window = null, RECT? icon = null, RECT? area = null)
    {
        var v = new GenieMesh.Vertex[GenieMesh.Rows * 2];
        GenieMesh.Compute(window ?? Window, icon ?? Icon, edge, p, area ?? Area, v);
        return v;
    }

    static (double X, double Y) Pixel(GenieMesh.Vertex v, RECT area) =>
        (area.Left + (v.Position.X + 1) / 2 * area.Width, area.Top + (1 - v.Position.Y) / 2 * area.Height);

    [Fact]
    public void At_the_start_the_mesh_is_exactly_the_window()
    {
        var v = Mesh(DockEdge.Bottom, 0);

        var topLeft = Pixel(v[0], Area);
        var topRight = Pixel(v[1], Area);
        var bottomLeft = Pixel(v[^2], Area);
        var bottomRight = Pixel(v[^1], Area);
        Assert.Equal(600, topLeft.X, 1);
        Assert.Equal(150, topLeft.Y, 1);
        Assert.Equal(1200, topRight.X, 1);
        Assert.Equal(1200, bottomRight.X, 1);
        Assert.Equal(600, bottomLeft.X, 1);
        Assert.Equal(600, bottomLeft.Y, 1);
        Assert.Equal(0, v[0].Uv.X);
        Assert.Equal(0, v[0].Uv.Y);
        Assert.Equal(1, v[^1].Uv.X);
        Assert.Equal(1, v[^1].Uv.Y);
    }

    [Fact]
    public void At_the_end_every_row_is_in_the_icon()
    {
        var v = Mesh(DockEdge.Bottom, 1);
        double iconMidY = (Icon.Top + Icon.Bottom) / 2.0;

        foreach (var vertex in v)
        {
            var (x, y) = Pixel(vertex, Area);
            Assert.Equal(iconMidY, y, 1);
            Assert.InRange(x, Icon.Left, Icon.Right);
        }
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.35)]
    [InlineData(0.6)]
    [InlineData(0.9)]
    public void Rows_never_cross_and_stay_inside_the_drawing_area(double p)
    {
        var v = Mesh(DockEdge.Bottom, p);
        double previous = double.MinValue;
        for (int i = 0; i < GenieMesh.Rows; i++)
        {
            var (xa, ya) = Pixel(v[2 * i], Area);
            var (xb, _) = Pixel(v[2 * i + 1], Area);
            Assert.True(ya >= previous - 0.001, $"row {i} goes back up at p={p}");
            Assert.True(xa <= xb + 0.001);
            Assert.InRange(v[2 * i].Position.X, -1.001f, 1.001f);
            Assert.InRange(v[2 * i].Position.Y, -1.001f, 1.001f);
            previous = ya;
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Side_docks_pull_the_window_sideways_into_the_icon(bool left)
    {
        var edge = left ? DockEdge.Left : DockEdge.Right;
        var window = new RECT(500, 200, 1100, 700);
        var icon = edge == DockEdge.Left ? new RECT(10, 420, 58, 468) : new RECT(1860, 420, 1908, 468);
        var area = new RECT(Math.Min(window.Left, icon.Left), 200, Math.Max(window.Right, icon.Right), 700);

        var v = Mesh(edge, 1, window, icon, area);
        double iconMidX = (icon.Left + icon.Right) / 2.0;
        foreach (var vertex in v)
        {
            var (x, y) = Pixel(vertex, area);
            Assert.Equal(iconMidX, x, 1);
            Assert.InRange(y, icon.Top, icon.Bottom);
        }
    }
}
