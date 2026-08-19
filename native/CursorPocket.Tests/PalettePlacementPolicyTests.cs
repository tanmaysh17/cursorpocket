using CursorPocket.Core.Services;

namespace CursorPocket.Tests;

public sealed class PalettePlacementPolicyTests
{
    private static readonly PaletteRect WorkArea = PaletteRect.FromEdges(0, 0, 1920, 1040);
    private const int Width = 372;
    private const int Height = 468;
    private const int Margin = 24;
    private const int Padding = 64;

    [Theory]
    [InlineData(PaletteCorner.TopRight, 1524, 24)]
    [InlineData(PaletteCorner.BottomRight, 1524, 548)]
    [InlineData(PaletteCorner.BottomLeft, 24, 548)]
    [InlineData(PaletteCorner.TopLeft, 24, 24)]
    public void Each_corner_sits_inside_the_work_area_with_the_requested_margin(
        PaletteCorner corner,
        int expectedLeft,
        int expectedTop)
    {
        var rect = PalettePlacementPolicy.RectFor(corner, WorkArea, Width, Height, Margin);

        Assert.Equal(expectedLeft, rect.Left);
        Assert.Equal(expectedTop, rect.Top);
        Assert.Equal(Width, rect.Width);
        Assert.Equal(Height, rect.Height);
        Assert.True(rect.Left >= WorkArea.Left && rect.Right <= WorkArea.Right);
        Assert.True(rect.Top >= WorkArea.Top && rect.Bottom <= WorkArea.Bottom);
    }

    [Fact]
    public void A_panel_larger_than_the_work_area_is_clamped_rather_than_pushed_off_screen()
    {
        var cramped = PaletteRect.FromEdges(0, 0, 300, 320);

        var rect = PalettePlacementPolicy.RectFor(PaletteCorner.BottomRight, cramped, Width, Height, Margin);

        Assert.Equal(0, rect.Left);
        Assert.Equal(0, rect.Top);
        Assert.Equal(300, rect.Width);
        Assert.Equal(320, rect.Height);
    }

    [Fact]
    public void The_work_area_offset_is_preserved_on_a_secondary_display()
    {
        var secondary = PaletteRect.FromEdges(-1920, 200, 0, 1280);

        var rect = PalettePlacementPolicy.RectFor(PaletteCorner.BottomLeft, secondary, Width, Height, Margin);

        Assert.Equal(-1896, rect.Left);
        Assert.Equal(1280 - Height - Margin, rect.Top);
    }

    [Theory]
    [InlineData(1900, 20, PaletteCorner.BottomLeft)]
    [InlineData(20, 20, PaletteCorner.BottomRight)]
    [InlineData(20, 1020, PaletteCorner.TopRight)]
    [InlineData(1900, 1020, PaletteCorner.TopLeft)]
    public void Command_mode_opens_in_the_corner_farthest_from_the_pointer(
        int pointerX,
        int pointerY,
        PaletteCorner expected)
    {
        var corner = PalettePlacementPolicy.ChooseCorner(
            WorkArea, Width, Height, Margin, pointerX, pointerY, Padding);

        Assert.Equal(expected, corner);
    }

    [Fact]
    public void Stepping_away_never_reuses_the_corner_the_panel_already_occupies()
    {
        // A pointer in the middle leaves every corner equally viable, so only the
        // exclusion can force the panel to visibly move.
        foreach (var occupied in PalettePlacementPolicy.Corners)
        {
            var corner = PalettePlacementPolicy.ChooseCorner(
                WorkArea, Width, Height, Margin, 960, 520, Padding, avoid: occupied);

            Assert.NotEqual(occupied, corner);
        }
    }

    [Fact]
    public void A_pointer_closing_in_on_the_top_right_panel_sends_it_to_the_bottom_left()
    {
        var panel = PalettePlacementPolicy.RectFor(PaletteCorner.TopRight, WorkArea, Width, Height, Margin);
        var pointerX = panel.Left - 20;
        var pointerY = panel.Top + 40;

        Assert.True(PalettePlacementPolicy.IsPointerEncroaching(panel, pointerX, pointerY, Padding));
        Assert.Equal(
            PaletteCorner.BottomLeft,
            PalettePlacementPolicy.ChooseCorner(
                WorkArea, Width, Height, Margin, pointerX, pointerY, Padding, avoid: PaletteCorner.TopRight));
    }

    [Fact]
    public void Encroachment_covers_the_keep_away_band_but_not_the_screen_beyond_it()
    {
        var panel = new PaletteRect(1000, 400, 372, 468);

        Assert.True(PalettePlacementPolicy.IsPointerEncroaching(panel, 1100, 500, Padding));
        Assert.True(PalettePlacementPolicy.IsPointerEncroaching(panel, 1000 - Padding, 500, Padding));
        Assert.False(PalettePlacementPolicy.IsPointerEncroaching(panel, 1000 - Padding - 1, 500, Padding));
        Assert.False(PalettePlacementPolicy.IsPointerEncroaching(panel, 1100, 400 - Padding - 1, Padding));
    }

    [Fact]
    public void Contains_is_true_only_inside_the_panel_itself()
    {
        var panel = new PaletteRect(1000, 400, 372, 468);

        Assert.True(panel.Contains(1000, 400));
        Assert.True(panel.Contains(1371, 867));
        Assert.False(panel.Contains(1372, 868));
        Assert.False(panel.Contains(999, 400));
    }

    [Fact]
    public void Distance_is_measured_to_the_nearest_edge_and_is_zero_inside()
    {
        var panel = new PaletteRect(100, 100, 200, 200);

        Assert.Equal(0d, PalettePlacementPolicy.DistanceToRect(panel, 150, 150));
        Assert.Equal(50d, PalettePlacementPolicy.DistanceToRect(panel, 50, 150));
        Assert.Equal(5d, PalettePlacementPolicy.DistanceToRect(panel, 96, 97), 6);
    }

    [Fact]
    public void A_display_too_small_for_any_clear_corner_still_returns_the_farthest_one()
    {
        var tiny = PaletteRect.FromEdges(0, 0, 500, 600);

        var corner = PalettePlacementPolicy.ChooseCorner(tiny, Width, Height, Margin, 10, 10, Padding);

        Assert.Equal(PaletteCorner.BottomRight, corner);
    }
}
