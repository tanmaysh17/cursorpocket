using CursorPocket.Core.Models;
using CursorPocket.Core.Services;

namespace CursorPocket.Tests;

public sealed class PinnedCapturePlacementTests
{
    private static readonly CaptureBounds Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void A_small_image_is_pinned_at_its_natural_size()
    {
        var (width, height) = PinnedCapturePlacement.Size(Screen, 400, 300, 1.0);

        Assert.Equal(400, width);
        Assert.Equal(300, height);
    }

    [Fact]
    public void A_large_image_is_capped_to_a_share_of_the_screen()
    {
        var (width, height) = PinnedCapturePlacement.Size(Screen, 1920, 1080, 1.0);

        Assert.True(width <= 1920 * PinnedCapturePlacement.MaximumWidthShare + 1);
        Assert.True(height <= 1080 * PinnedCapturePlacement.MaximumHeightShare + 1);
    }

    [Fact]
    public void Capping_preserves_the_aspect_ratio()
    {
        var (width, height) = PinnedCapturePlacement.Size(Screen, 3000, 1000, 1.0);

        // One factor for both axes, or a wide screenshot would come out squashed.
        Assert.Equal(3.0, (double)width / height, 2);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0.2)]
    [InlineData(1.0)]
    public void Scaling_stays_proportional_at_every_step(double scale)
    {
        var (width, height) = PinnedCapturePlacement.Size(Screen, 600, 400, scale);

        Assert.Equal(1.5, (double)width / height, 1);
    }

    [Fact]
    public void Scale_is_clamped_rather_than_allowed_to_vanish_or_balloon()
    {
        var tiny = PinnedCapturePlacement.Size(Screen, 600, 400, 0.001);
        var huge = PinnedCapturePlacement.Size(Screen, 600, 400, 12);

        Assert.True(tiny.Width >= 600 * PinnedCapturePlacement.MinimumScale - 1);
        Assert.True(huge.Width <= 600);
    }

    [Fact]
    public void A_degenerate_image_never_produces_a_zero_sized_pin()
    {
        var (width, height) = PinnedCapturePlacement.Size(Screen, 0, 0, 1.0);

        Assert.True(width >= 1 && height >= 1);
    }

    [Fact]
    public void The_first_pin_sits_in_the_top_right_corner()
    {
        var bounds = PinnedCapturePlacement.Place(Screen, 400, 300, 0);

        Assert.Equal(1920 - PinnedCapturePlacement.Margin - 400, bounds.Left);
        Assert.Equal(PinnedCapturePlacement.Margin, bounds.Top);
    }

    [Fact]
    public void Later_pins_cascade_off_the_first()
    {
        var first = PinnedCapturePlacement.Place(Screen, 400, 300, 0);
        var second = PinnedCapturePlacement.Place(Screen, 400, 300, 1);
        var third = PinnedCapturePlacement.Place(Screen, 400, 300, 2);

        Assert.NotEqual(first.Left, second.Left);
        Assert.Equal(PinnedCapturePlacement.CascadeStep, first.Left - second.Left);
        Assert.Equal(PinnedCapturePlacement.CascadeStep, second.Top - first.Top);
        Assert.NotEqual(second.Left, third.Left);
    }

    [Fact]
    public void A_long_cascade_stays_on_screen()
    {
        for (var index = 0; index < 60; index++)
        {
            var bounds = PinnedCapturePlacement.Place(Screen, 400, 300, index);

            Assert.InRange(bounds.Left, Screen.Left, Screen.Right - 400);
            Assert.InRange(bounds.Top, Screen.Top, Screen.Bottom - 300);
        }
    }

    [Fact]
    public void A_pin_on_a_secondary_screen_is_placed_relative_to_that_screen()
    {
        var right = new CaptureBounds(1920, 0, 3840, 1080);

        var bounds = PinnedCapturePlacement.Place(right, 400, 300, 0);

        Assert.InRange(bounds.Left, 1920, 3840 - 400);
        Assert.Equal(3840 - PinnedCapturePlacement.Margin - 400, bounds.Left);
    }

    [Fact]
    public void The_wheel_steps_the_scale_and_stops_at_the_ends()
    {
        Assert.Equal(0.6, PinnedCapturePlacement.StepScale(0.5, 1), 6);
        Assert.Equal(0.4, PinnedCapturePlacement.StepScale(0.5, -1), 6);
        Assert.Equal(PinnedCapturePlacement.MaximumScale, PinnedCapturePlacement.StepScale(1.0, 1), 6);
        Assert.Equal(PinnedCapturePlacement.MinimumScale, PinnedCapturePlacement.StepScale(0.2, -1), 6);
    }
}
