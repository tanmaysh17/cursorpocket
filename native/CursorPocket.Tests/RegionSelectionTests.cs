using CursorPocket.Core.Services;

namespace CursorPocket.Tests;

public sealed class RegionSelectionTests
{
    [Theory]
    // Dragged in each direction; the rectangle is the same either way.
    [InlineData(100, 80, 700, 480)]
    [InlineData(700, 480, 100, 80)]
    [InlineData(700, 80, 100, 480)]
    [InlineData(100, 480, 700, 80)]
    public void A_drag_in_any_direction_yields_the_same_rectangle(int startX, int startY, int endX, int endY)
    {
        var bounds = RegionSelection.FromCorners(startX, startY, endX, endY);

        Assert.Equal(100, bounds.Left);
        Assert.Equal(80, bounds.Top);
        Assert.Equal(700, bounds.Right);
        Assert.Equal(480, bounds.Bottom);
        Assert.Equal(600, bounds.Width);
        Assert.Equal(400, bounds.Height);
    }

    [Fact]
    public void The_full_dragged_size_is_kept_rather_than_scaled_down()
    {
        // The regression: a 600x400 drag on a 150% display used to arrive as 400x267
        // because device-independent coordinates were passed to a pixel capture,
        // cutting off the right and bottom of every region.
        var bounds = RegionSelection.FromCorners(0, 0, 600, 400);

        Assert.Equal(600, bounds.Width);
        Assert.Equal(400, bounds.Height);
    }

    [Fact]
    public void A_region_on_a_monitor_left_of_the_primary_keeps_its_negative_origin()
    {
        var bounds = RegionSelection.FromCorners(-1600, -200, -900, 300);

        Assert.Equal(-1600, bounds.Left);
        Assert.Equal(-200, bounds.Top);
        Assert.Equal(700, bounds.Width);
        Assert.Equal(500, bounds.Height);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(500, 500, 502, 520)]
    [InlineData(500, 500, 520, 502)]
    public void A_stray_click_or_sliver_is_not_a_usable_region(int startX, int startY, int endX, int endY) =>
        Assert.False(RegionSelection.IsUsable(RegionSelection.FromCorners(startX, startY, endX, endY)));

    [Fact]
    public void A_region_at_the_minimum_size_is_usable()
    {
        var bounds = RegionSelection.FromCorners(10, 10, 10 + RegionSelection.MinimumSide, 10 + RegionSelection.MinimumSide);

        Assert.True(RegionSelection.IsUsable(bounds));
    }
}
