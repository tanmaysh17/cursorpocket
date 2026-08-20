using CursorPocket.Core.Models;
using CursorPocket.Core.Services;

namespace CursorPocket.Tests;

public sealed class DrawerAnimationTests
{
    [Fact]
    public void The_drawer_takes_its_full_duration_to_open()
    {
        var progress = 0d;
        var frames = 0;
        // A 16 ms frame is roughly one display refresh.
        while (progress < 1 && frames < 500)
        {
            progress = DrawerAnimation.Advance(progress, 1, 16);
            frames++;
        }

        Assert.Equal(1, progress);
        // Long enough to read as movement, short enough not to feel sluggish.
        Assert.InRange(frames * 16, 150, 260);
    }

    [Fact]
    public void Reversing_mid_travel_closes_from_where_it_had_reached()
    {
        var progress = DrawerAnimation.Advance(0, 1, 95);

        Assert.InRange(progress, 0.4, 0.6);
        Assert.True(DrawerAnimation.Advance(progress, 0, 16) < progress);
    }

    [Fact]
    public void Progress_never_overshoots_either_end()
    {
        Assert.Equal(1, DrawerAnimation.Advance(0.99, 1, 1000));
        Assert.Equal(0, DrawerAnimation.Advance(0.01, 0, 1000));
        // An out-of-range target is clamped, so it still travels toward fully open
        // one step at a time rather than jumping there.
        Assert.InRange(DrawerAnimation.Advance(0, 5, 16), 0, 1);
        Assert.True(DrawerAnimation.Advance(0, 5, 16) > 0);
        Assert.Equal(0, DrawerAnimation.Advance(0, double.NaN, 16));
    }

    [Fact]
    public void A_frame_that_took_no_time_does_not_stall_the_travel()
    {
        // A zero or negative delta would otherwise freeze the drawer part-open.
        Assert.Equal(1, DrawerAnimation.Advance(0.5, 1, 0));
        Assert.Equal(0, DrawerAnimation.Advance(0.5, 0, -20));
    }

    [Fact]
    public void Easing_starts_and_ends_gently()
    {
        Assert.Equal(0, DrawerAnimation.Ease(0));
        Assert.Equal(1, DrawerAnimation.Ease(1));
        Assert.Equal(0.5, DrawerAnimation.Ease(0.5), 6);
        // Slower than linear at the start, faster through the middle.
        Assert.True(DrawerAnimation.Ease(0.2) < 0.2);
        Assert.True(DrawerAnimation.Ease(0.8) > 0.8);
    }

    [Theory]
    [InlineData(0, 178)]
    [InlineData(1, 452)]
    [InlineData(0.5, 315)]
    public void Size_interpolates_between_the_collapsed_and_open_widths(double eased, int expected) =>
        Assert.Equal(expected, DrawerAnimation.Lerp(178, 452, eased));

    [Fact]
    public void The_drawer_opens_before_the_pointer_reaches_it()
    {
        var pill = new CaptureBounds(860, 0, 1038, 30);

        // Approaching from below, still short of the surface.
        Assert.True(DrawerAnimation.IsPointerNear(pill, 950, 90));
        // Well clear of it.
        Assert.False(DrawerAnimation.IsPointerNear(pill, 950, 400));
        Assert.False(DrawerAnimation.IsPointerNear(pill, 200, 15));
    }

    [Fact]
    public void Proximity_includes_the_surface_itself()
    {
        var pill = new CaptureBounds(860, 0, 1038, 30);

        Assert.True(DrawerAnimation.IsPointerNear(pill, 900, 15));
        Assert.True(DrawerAnimation.IsPointerNear(pill, 860, 0));
    }

    [Fact]
    public void A_zero_padding_means_contact_only()
    {
        var pill = new CaptureBounds(100, 100, 200, 140);

        Assert.True(DrawerAnimation.IsPointerNear(pill, 150, 120, 0));
        Assert.False(DrawerAnimation.IsPointerNear(pill, 150, 141, 0));
    }
}
