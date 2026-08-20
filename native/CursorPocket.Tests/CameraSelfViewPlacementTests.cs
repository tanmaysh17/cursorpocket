using CursorPocket.Core.Models;
using CursorPocket.Core.Services;

namespace CursorPocket.Tests;

public sealed class CameraSelfViewPlacementTests
{
    private static readonly CaptureBounds Display = new(0, 0, 1920, 1080);

    [Theory]
    [InlineData("bottom-right", 1528, 846)]
    [InlineData("bottom-left", 32, 846)]
    [InlineData("top-right", 1528, 32)]
    [InlineData("top-left", 32, 32)]
    public void The_self_view_keeps_the_inset_ffmpeg_used_for_its_overlay(string position, int expectedLeft, int expectedTop)
    {
        var rect = CameraSelfViewPlacement.Compute(Display, position, 360);

        Assert.Equal(expectedLeft, rect.Left);
        Assert.Equal(expectedTop, rect.Top);
        Assert.Equal(360, rect.Width);
        Assert.Equal(202, rect.Height);
    }

    [Fact]
    public void An_unknown_position_falls_back_to_the_bottom_right()
    {
        var rect = CameraSelfViewPlacement.Compute(Display, "middle", 360);

        Assert.Equal(1528, rect.Left);
        Assert.Equal(846, rect.Top);
    }

    [Theory]
    [InlineData("bottom-right")]
    [InlineData("bottom-left")]
    [InlineData("top-right")]
    [InlineData("top-left")]
    public void The_self_view_always_lands_inside_the_recorded_area(string position)
    {
        // Anything outside the captured rectangle is missing from the file, so this
        // has to hold for every corner, size, and source geometry.
        foreach (var area in new[]
        {
            Display,
            new CaptureBounds(-1920, 200, 0, 1280),
            new CaptureBounds(120, 90, 520, 330),
            new CaptureBounds(0, 0, 300, 200),
        })
        {
            foreach (var width in new[] { 240, 360, 480 })
            {
                var rect = CameraSelfViewPlacement.Compute(area, position, width);

                Assert.True(rect.Left >= area.Left, $"{position} {width} left {rect.Left} < {area.Left}");
                Assert.True(rect.Top >= area.Top, $"{position} {width} top {rect.Top} < {area.Top}");
                Assert.True(rect.Right <= area.Right, $"{position} {width} right {rect.Right} > {area.Right}");
                Assert.True(rect.Bottom <= area.Bottom, $"{position} {width} bottom {rect.Bottom} > {area.Bottom}");
            }
        }
    }

    [Fact]
    public void A_capture_area_smaller_than_the_camera_clamps_instead_of_overflowing()
    {
        var rect = CameraSelfViewPlacement.Compute(new CaptureBounds(0, 0, 300, 200), "bottom-right", 480);

        Assert.Equal(0, rect.Left);
        Assert.Equal(300, rect.Width);
        Assert.True(rect.Height <= 200);
    }

    [Fact]
    public void Camera_width_is_clamped_to_the_supported_range()
    {
        Assert.Equal(CameraSelfViewPlacement.MinimumWidth, CameraSelfViewPlacement.Compute(Display, "top-left", 10).Width);
        Assert.Equal(CameraSelfViewPlacement.MaximumWidth, CameraSelfViewPlacement.Compute(Display, "top-left", 4000).Width);
    }

    [Fact]
    public void The_self_view_height_stays_even_for_h264()
    {
        foreach (var width in new[] { 240, 300, 360, 420, 480, 639 })
        {
            Assert.Equal(0, CameraSelfViewPlacement.Compute(Display, "top-left", width).Height % 2);
        }
    }

    [Fact]
    public void A_zero_sized_capture_area_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CameraSelfViewPlacement.Compute(new CaptureBounds(10, 10, 11, 11), "top-left", 360));
    }

    [Fact]
    public void The_default_shape_keeps_the_existing_sixteen_by_nine_framing() =>
        Assert.Equal(
            CameraSelfViewPlacement.Compute(Display, "bottom-right", 360),
            CameraSelfViewPlacement.Compute(Display, "bottom-right", 360, "rounded"));

    [Theory]
    [InlineData(240)]
    [InlineData(360)]
    [InlineData(480)]
    public void The_squircle_is_square_so_the_plump_shape_is_not_stretched(int width)
    {
        var rect = CameraSelfViewPlacement.Compute(Display, "bottom-right", width, "squircle");

        Assert.Equal(width, rect.Width);
        Assert.Equal(width, rect.Height);
    }

    [Fact]
    public void An_unknown_shape_falls_back_to_the_rounded_framing() =>
        Assert.Equal(
            CameraSelfViewPlacement.Compute(Display, "top-left", 360, "rounded"),
            CameraSelfViewPlacement.Compute(Display, "top-left", 360, "hexagon"));

    [Theory]
    [InlineData("bottom-right")]
    [InlineData("bottom-left")]
    [InlineData("top-right")]
    [InlineData("top-left")]
    public void The_squircle_also_always_lands_inside_the_recorded_area(string position)
    {
        // The taller 1:1 shape has less room to spare than 16:9, so the inset
        // clamping has to hold for it too or the webcam is cropped out of the file.
        foreach (var area in new[]
        {
            Display,
            new CaptureBounds(-1920, 200, 0, 1280),
            new CaptureBounds(120, 90, 520, 330),
            new CaptureBounds(0, 0, 300, 200),
        })
        {
            foreach (var width in new[] { 240, 360, 480 })
            {
                var rect = CameraSelfViewPlacement.Compute(area, position, width, "squircle");

                Assert.True(rect.Left >= area.Left, $"{position} {width} left {rect.Left} < {area.Left}");
                Assert.True(rect.Top >= area.Top, $"{position} {width} top {rect.Top} < {area.Top}");
                Assert.True(rect.Right <= area.Right, $"{position} {width} right {rect.Right} > {area.Right}");
                Assert.True(rect.Bottom <= area.Bottom, $"{position} {width} bottom {rect.Bottom} > {area.Bottom}");
            }
        }
    }

    [Fact]
    public void The_squircle_height_stays_even_for_h264()
    {
        foreach (var width in new[] { 241, 300, 361, 480, 639 })
        {
            Assert.Equal(0, CameraSelfViewPlacement.Compute(Display, "top-left", width, "squircle").Height % 2);
        }
    }

    [Theory]
    [InlineData(VideoSourceKind.Display, true)]
    [InlineData(VideoSourceKind.Region, true)]
    [InlineData(VideoSourceKind.Window, false)]
    public void Only_screen_area_sources_carry_the_self_view_into_the_file(VideoSourceKind kind, bool expected) =>
        Assert.Equal(expected, CameraSelfViewPlacement.IsRecordedForSource(kind));
}
