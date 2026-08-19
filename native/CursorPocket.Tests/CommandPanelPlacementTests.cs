using CursorPocket.Core.Models;
using CursorPocket.Core.Services;
using CursorPocket.Core.Storage;

namespace CursorPocket.Tests;

public sealed class CommandPanelPlacementTests
{
    private static readonly CaptureBounds WorkArea = new(0, 0, 1920, 1040);
    private const int Width = 296;
    private const int Height = 340;
    private const int Margin = 22;

    [Theory]
    [InlineData(1, 0, 1602, 22)]      // default: top right
    [InlineData(0, 0, 22, 22)]        // top left
    [InlineData(0, 1, 22, 678)]       // bottom left
    [InlineData(1, 1, 1602, 678)]     // bottom right
    [InlineData(0.5, 0.5, 812, 350)]  // centred
    public void An_anchor_resolves_to_a_position_inside_the_work_area(
        double anchorX,
        double anchorY,
        int expectedLeft,
        int expectedTop)
    {
        var rect = CommandPanelPlacement.Resolve(WorkArea, Width, Height, anchorX, anchorY, Margin);

        Assert.Equal(expectedLeft, rect.Left);
        Assert.Equal(expectedTop, rect.Top);
        Assert.Equal(Width, rect.Width);
        Assert.Equal(Height, rect.Height);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(0.5, 0.5)]
    [InlineData(0.33, 0.67)]
    public void Dragging_and_reopening_lands_in_the_same_place(double anchorX, double anchorY)
    {
        var rect = CommandPanelPlacement.Resolve(WorkArea, Width, Height, anchorX, anchorY, Margin);

        var (roundTrippedX, roundTrippedY) = CommandPanelPlacement.AnchorFor(
            WorkArea, Width, Height, rect.Left, rect.Top, Margin);
        var again = CommandPanelPlacement.Resolve(WorkArea, Width, Height, roundTrippedX, roundTrippedY, Margin);

        Assert.Equal(rect.Left, again.Left);
        Assert.Equal(rect.Top, again.Top);
    }

    [Fact]
    public void A_position_remembered_on_one_display_stays_on_screen_on_another()
    {
        // Dragged to the bottom right of a 4K display...
        var wide = new CaptureBounds(0, 0, 3840, 2120);
        var dropped = CommandPanelPlacement.Resolve(wide, Width, Height, 1, 1, Margin);
        var (anchorX, anchorY) = CommandPanelPlacement.AnchorFor(wide, Width, Height, dropped.Left, dropped.Top, Margin);

        // ...still fully visible on a small secondary display at a negative origin.
        var small = new CaptureBounds(-1280, -200, 0, 520);
        var rect = CommandPanelPlacement.Resolve(small, Width, Height, anchorX, anchorY, Margin);

        Assert.True(rect.Left >= small.Left && rect.Right <= small.Right);
        Assert.True(rect.Top >= small.Top && rect.Bottom <= small.Bottom);
    }

    [Theory]
    [InlineData(-4, -9)]
    [InlineData(7, 12)]
    [InlineData(double.NaN, double.NaN)]
    [InlineData(double.PositiveInfinity, double.NegativeInfinity)]
    public void An_out_of_range_anchor_never_puts_the_panel_off_screen(double anchorX, double anchorY)
    {
        var rect = CommandPanelPlacement.Resolve(WorkArea, Width, Height, anchorX, anchorY, Margin);

        Assert.True(rect.Left >= WorkArea.Left && rect.Right <= WorkArea.Right);
        Assert.True(rect.Top >= WorkArea.Top && rect.Bottom <= WorkArea.Bottom);
    }

    [Fact]
    public void A_work_area_smaller_than_the_panel_gives_up_the_margin_first()
    {
        var cramped = new CaptureBounds(0, 0, 300, 300);

        var rect = CommandPanelPlacement.Resolve(cramped, Width, Height, 1, 1, Margin);

        Assert.True(rect.Left >= 0 && rect.Right <= 300);
        Assert.True(rect.Top >= 0 && rect.Bottom <= 300);
    }

    [Fact]
    public void A_drag_beyond_the_margin_is_clamped_rather_than_discarded()
    {
        var (anchorX, anchorY) = CommandPanelPlacement.AnchorFor(WorkArea, Width, Height, -500, 5000, Margin);

        Assert.Equal(0, anchorX);
        Assert.Equal(1, anchorY);
    }

    [Fact]
    public void Settings_default_to_the_top_right_and_repair_a_corrupt_anchor()
    {
        Assert.Equal(CommandPanelPlacement.DefaultAnchorX, new AppSettings().CommandPanelAnchorX);
        Assert.Equal(CommandPanelPlacement.DefaultAnchorY, new AppSettings().CommandPanelAnchorY);

        var repaired = SettingsStore.Normalize(new AppSettings
        {
            CommandPanelAnchorX = 4.5,
            CommandPanelAnchorY = double.NaN,
        });

        Assert.Equal(1, repaired.CommandPanelAnchorX);
        Assert.Equal(CommandPanelPlacement.DefaultAnchorY, repaired.CommandPanelAnchorY);
    }

    [Fact]
    public void A_dragged_anchor_survives_a_settings_round_trip()
    {
        var saved = SettingsStore.Normalize(new AppSettings { CommandPanelAnchorX = 0.25, CommandPanelAnchorY = 0.75 });

        Assert.Equal(0.25, saved.CommandPanelAnchorX);
        Assert.Equal(0.75, saved.CommandPanelAnchorY);
    }
}
