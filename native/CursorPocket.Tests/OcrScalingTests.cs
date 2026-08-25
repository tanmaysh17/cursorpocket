using CursorPocket.Core.Annotations;

namespace CursorPocket.Tests;

public sealed class OcrScalingTests
{
    [Fact]
    public void An_ordinary_region_is_handed_over_untouched()
    {
        Assert.Equal(1, OcrScaling.ScaleFor(800, 300, 4096), 6);
    }

    [Fact]
    public void A_region_too_small_to_be_accepted_is_scaled_up()
    {
        // The engine rejects a side under 40 px, and a one-word crop is easily that thin.
        var scale = OcrScaling.ScaleFor(200, 12, 4096);

        Assert.True(scale > 1);
        var (_, height) = OcrScaling.Scaled(200, 12, scale);
        Assert.True(height >= OcrScaling.MinimumSide, $"scaled height {height} is still under the minimum");
    }

    [Fact]
    public void A_region_too_large_is_scaled_down_to_the_ceiling()
    {
        var scale = OcrScaling.ScaleFor(9000, 3000, 4096);

        var (width, height) = OcrScaling.Scaled(9000, 3000, scale);
        Assert.True(Math.Max(width, height) <= 4096, $"{width}x{height} still exceeds the engine maximum");
        Assert.True(scale < 1);
    }

    [Fact]
    public void The_ceiling_wins_over_the_floor_when_they_conflict()
    {
        // A 5000x8 sliver: it is both too thin to accept and too long to accept. Blowing
        // it up to clear the 40 px floor would put it far past the ceiling, and exceeding
        // the ceiling is a hard rejection while falling short of the floor only costs
        // accuracy. So the result must respect the ceiling.
        var scale = OcrScaling.ScaleFor(5000, 8, 4096);
        var (width, height) = OcrScaling.Scaled(5000, 8, scale);

        Assert.True(Math.Max(width, height) <= 4096, $"{width}x{height} exceeds the ceiling");
        Assert.True(OcrScaling.CannotBeRead(5000, 8, 4096), "this region should be reported as unreadable");
    }

    [Fact]
    public void A_readable_region_is_not_reported_as_unreadable()
    {
        Assert.False(OcrScaling.CannotBeRead(1920, 1080, 4096));
        Assert.False(OcrScaling.CannotBeRead(300, 20, 4096));
        Assert.True(OcrScaling.CannotBeRead(0, 100, 4096));
    }

    [Fact]
    public void A_box_maps_back_through_the_same_factor_it_was_scaled_by()
    {
        // The engine saw a copy scaled 2x, starting from source pixel (100, 40).
        var box = new AnnRect(20, 10, 60, 16);

        var mapped = OcrScaling.ToSource(box, 2, new AnnPoint(100, 40));

        Assert.Equal(110, mapped.X, 6);
        Assert.Equal(45, mapped.Y, 6);
        Assert.Equal(30, mapped.Width, 6);
        Assert.Equal(8, mapped.Height, 6);
    }

    [Fact]
    public void A_box_from_an_unscaled_whole_image_maps_to_itself()
    {
        var box = new AnnRect(12, 34, 56, 7);

        Assert.Equal(box, OcrScaling.ToSource(box, 1, new AnnPoint(0, 0)));
    }

    [Theory]
    [InlineData(400, 300)]
    [InlineData(1920, 1080)]
    [InlineData(200, 14)]
    [InlineData(7000, 4000)]
    public void Scaling_a_box_and_mapping_it_back_returns_where_it_started(int width, int height)
    {
        var scale = OcrScaling.ScaleFor(width, height, 4096);
        var origin = new AnnPoint(37, 91);
        // A box covering the whole region, expressed in the engine's coordinate space.
        var inEngineSpace = new AnnRect(0, 0, width * scale, height * scale);

        var mapped = OcrScaling.ToSource(inEngineSpace, scale, origin);

        Assert.Equal(origin.X, mapped.X, 4);
        Assert.Equal(origin.Y, mapped.Y, 4);
        Assert.Equal(width, mapped.Width, 4);
        Assert.Equal(height, mapped.Height, 4);
    }

    [Fact]
    public void A_zero_scale_is_treated_as_one_rather_than_dividing_by_zero()
    {
        var mapped = OcrScaling.ToSource(new AnnRect(5, 5, 10, 10), 0, new AnnPoint(0, 0));

        Assert.Equal(new AnnRect(5, 5, 10, 10), mapped);
    }

    [Fact]
    public void A_degenerate_region_never_produces_a_zero_sized_image()
    {
        var (width, height) = OcrScaling.Scaled(1, 1, 0.01);

        Assert.True(width >= 1 && height >= 1);
    }
}
