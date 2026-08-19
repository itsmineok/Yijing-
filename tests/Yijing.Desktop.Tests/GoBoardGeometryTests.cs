using System.Windows;
using Yijing.Desktop.Controls;
using Yijing.Domain.Board;

namespace Yijing.Desktop.Tests;

public sealed class GoBoardGeometryTests
{
    [Theory]
    [InlineData(0, 0, 28, 28)]
    [InlineData(18, 18, 388, 388)]
    public void PointToPixel_maps_board_corners(int row, int column, double x, double y)
    {
        var pixel = GoBoardControl.PointToPixel(new BoardPoint(row, column), 19, new Rect(28, 28, 360, 360));

        Assert.Equal(new Point(x, y), pixel);
    }

    [Fact]
    public void PointToPixel_maps_board_center()
    {
        var pixel = GoBoardControl.PointToPixel(new BoardPoint(6, 6), 13, new Rect(20, 30, 300, 300));

        Assert.Equal(new Point(170, 180), pixel);
    }

    [Theory]
    [InlineData(0, 0, 12, 24)]
    [InlineData(0, 8, 92, 24)]
    [InlineData(8, 0, 12, 104)]
    [InlineData(8, 8, 92, 104)]
    public void PointToPixel_maps_all_nine_by_nine_corners(int row, int column, double x, double y)
    {
        var pixel = GoBoardControl.PointToPixel(new BoardPoint(row, column), 9, new Rect(12, 24, 80, 80));

        Assert.Equal(new Point(x, y), pixel);
    }

    [Fact]
    public void Geometry_supports_rectangular_bounds_with_independent_axis_spacing()
    {
        var bounds = new Rect(10, 20, 160, 80);
        var pixel = GoBoardControl.PointToPixel(new BoardPoint(4, 4), 9, bounds);

        Assert.Equal(new Point(90, 60), pixel);
        Assert.Equal(new BoardPoint(4, 4), GoBoardControl.PixelToPoint(pixel, 9, bounds, 1));
    }

    [Fact]
    public void Geometry_remains_correct_with_125_percent_scaled_bounds()
    {
        var bounds = new Rect(35, 35, 450, 450);
        var pixel = GoBoardControl.PointToPixel(new BoardPoint(9, 9), 19, bounds);

        Assert.Equal(new Point(260, 260), pixel);
        Assert.Equal(new BoardPoint(9, 9), GoBoardControl.PixelToPoint(pixel, 19, bounds, 11.25));
    }

    [Fact]
    public void PixelToPoint_maps_a_click_inside_tolerance()
    {
        var point = GoBoardControl.PixelToPoint(new Point(168, 181), 13, new Rect(20, 30, 300, 300), 12);

        Assert.Equal(new BoardPoint(6, 6), point);
    }

    [Fact]
    public void PixelToPoint_rejects_a_click_outside_tolerance()
    {
        var point = GoBoardControl.PixelToPoint(new Point(182.5, 180), 13, new Rect(20, 30, 300, 300), 12);

        Assert.Null(point);
    }

    [Fact]
    public void PixelToPoint_honors_precise_forty_five_percent_spacing_tolerance()
    {
        var bounds = new Rect(0, 0, 80, 80);
        const double tolerance = 10 * 0.45;

        Assert.Equal(new BoardPoint(4, 4),
            GoBoardControl.PixelToPoint(new Point(44.49, 40), 9, bounds, tolerance));
        Assert.Null(GoBoardControl.PixelToPoint(new Point(44.51, 40), 9, bounds, tolerance));
    }
}
