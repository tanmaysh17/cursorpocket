using CursorPocket.Core.Annotations;

namespace CursorPocket.Tests;

public sealed class AnnotationGeometryTests
{
    [Fact]
    public void An_arrow_outline_is_a_closed_shape_that_reaches_its_target()
    {
        var outline = AnnotationGeometry.ArrowOutline(new AnnPoint(10, 10), new AnnPoint(110, 10), 6);

        // Seven points: two tail corners, four head corners, and the tip.
        Assert.Equal(7, outline.Count);
        // The tip is the point the user dragged to, exactly. An arrow that stops short
        // of what it is pointing at is the whole reason this is a polygon.
        Assert.Contains(new AnnPoint(110, 10), outline);
    }

    [Fact]
    public void An_arrow_head_is_wider_than_its_shaft()
    {
        var outline = AnnotationGeometry.ArrowOutline(new AnnPoint(0, 50), new AnnPoint(200, 50), 8);

        var widest = outline.Max(point => Math.Abs(point.Y - 50));
        var shaftHalf = 8 / 2d;
        Assert.True(widest > shaftHalf, $"The head half-width {widest} must exceed the shaft's {shaftHalf}.");
        Assert.Equal(8 * AnnotationGeometry.ArrowHeadHalfWidth, widest, 6);
    }

    [Fact]
    public void A_zero_length_arrow_draws_nothing_instead_of_NaN()
    {
        var outline = AnnotationGeometry.ArrowOutline(new AnnPoint(40, 40), new AnnPoint(40, 40), 6);

        Assert.Empty(outline);
    }

    [Fact]
    public void A_very_short_arrow_degrades_instead_of_folding_inside_out()
    {
        // The head would normally be 3.2 * 10 = 32 px long, far longer than this 5 px
        // drag. Clamping keeps every point between the two ends.
        var outline = AnnotationGeometry.ArrowOutline(new AnnPoint(0, 0), new AnnPoint(5, 0), 10);

        Assert.NotEmpty(outline);
        Assert.All(outline, point => Assert.InRange(point.X, 0, 5));
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(100, 10)]
    [InlineData(-35, 90)]
    public void Constraining_an_angle_keeps_the_dragged_length(double dx, double dy)
    {
        var anchor = new AnnPoint(20, 20);
        var free = new AnnPoint(anchor.X + dx, anchor.Y + dy);

        var snapped = AnnotationGeometry.ConstrainToAngle(anchor, free);

        // Snapping rotates the mark, it never rescales it — otherwise a line would jump
        // longer or shorter the moment Shift went down.
        Assert.Equal((free - anchor).Length, (snapped - anchor).Length, 6);
    }

    [Theory]
    [InlineData(80, 10, 0)]
    [InlineData(10, 80, 90)]
    [InlineData(60, 55, 45)]
    [InlineData(-60, 55, 135)]
    [InlineData(-70, -70, -135)]
    public void Constraining_an_angle_snaps_to_the_nearest_45_degrees(double dx, double dy, double expectedDegrees)
    {
        var anchor = new AnnPoint(0, 0);

        var snapped = AnnotationGeometry.ConstrainToAngle(anchor, new AnnPoint(dx, dy));

        var degrees = Math.Atan2(snapped.Y, snapped.X) * 180 / Math.PI;
        Assert.Equal(expectedDegrees, degrees, 6);
    }

    [Fact]
    public void A_plain_drag_becomes_the_rectangle_between_the_two_corners()
    {
        var rect = AnnotationGeometry.RectFromDrag(new AnnPoint(100, 80), new AnnPoint(40, 200), DrawModifiers.None);

        Assert.Equal(new AnnRect(40, 80, 60, 120), rect);
    }

    [Fact]
    public void Shift_makes_a_square_that_still_follows_the_drag_direction()
    {
        var rect = AnnotationGeometry.RectFromDrag(
            new AnnPoint(100, 100),
            new AnnPoint(60, 10),
            DrawModifiers.Constrain);

        // The longer axis wins, and the square grows up and to the left because that is
        // where the pointer went.
        Assert.Equal(90, rect.Width, 6);
        Assert.Equal(90, rect.Height, 6);
        Assert.Equal(10, rect.X, 6);
        Assert.Equal(10, rect.Y, 6);
    }

    [Fact]
    public void Alt_centres_the_shape_on_the_press_point()
    {
        var rect = AnnotationGeometry.RectFromDrag(
            new AnnPoint(100, 100),
            new AnnPoint(130, 120),
            DrawModifiers.CenterOnPress);

        Assert.Equal(new AnnPoint(100, 100), rect.Center);
        Assert.Equal(60, rect.Width, 6);
        Assert.Equal(40, rect.Height, 6);
    }

    [Fact]
    public void Shift_and_Alt_together_give_a_centred_square()
    {
        var rect = AnnotationGeometry.RectFromDrag(
            new AnnPoint(200, 200),
            new AnnPoint(150, 180),
            DrawModifiers.Constrain | DrawModifiers.CenterOnPress);

        Assert.Equal(new AnnPoint(200, 200), rect.Center);
        Assert.Equal(rect.Width, rect.Height, 6);
        Assert.Equal(100, rect.Width, 6);
    }

    [Fact]
    public void Smoothing_keeps_both_endpoints_where_the_user_put_them()
    {
        List<AnnPoint> points = [new(0, 0), new(10, 40), new(20, 0), new(30, 40)];

        var smoothed = AnnotationGeometry.Smooth(points, 2);

        Assert.Equal(points[0], smoothed[0]);
        Assert.Equal(points[^1], smoothed[^1]);
        Assert.True(smoothed.Count > points.Count);
    }

    [Fact]
    public void Smoothing_pulls_a_spike_in_towards_its_neighbours()
    {
        List<AnnPoint> points = [new(0, 0), new(10, 100), new(20, 0)];

        var smoothed = AnnotationGeometry.Smooth(points, 2);

        // The whole point of corner cutting: nothing in the result reaches as far as the
        // raw spike did.
        Assert.True(smoothed.Max(point => point.Y) < 100);
    }

    [Fact]
    public void Smoothing_leaves_a_two_point_line_exactly_straight()
    {
        List<AnnPoint> points = [new(0, 0), new(50, 50)];

        var smoothed = AnnotationGeometry.Smooth(points, 3);

        Assert.Equal(points, smoothed);
    }

    [Fact]
    public void Decimating_drops_crowded_samples_but_never_the_last_one()
    {
        List<AnnPoint> points = [new(0, 0), new(1, 0), new(2, 0), new(3, 0), new(40, 0), new(41, 0)];

        var thinned = AnnotationGeometry.Decimate(points, 10);

        // A stroke has to end where the pointer was released, even though that final
        // sample is only 1 px from the one before it.
        Assert.Equal(new AnnPoint(0, 0), thinned[0]);
        Assert.Equal(new AnnPoint(41, 0), thinned[^1]);
        Assert.True(thinned.Count < points.Count);
    }

    [Fact]
    public void Clamping_keeps_a_rectangle_inside_the_image()
    {
        var rect = AnnotationGeometry.ClampToImage(new AnnRect(-30, -10, 200, 150), 100, 100);

        Assert.Equal(new AnnRect(0, 0, 100, 100), rect);
    }

    [Fact]
    public void Clamping_a_rectangle_that_misses_the_image_gives_no_area()
    {
        var rect = AnnotationGeometry.ClampToImage(new AnnRect(400, 400, 50, 50), 100, 100);

        Assert.Equal(0, rect.Width);
        Assert.Equal(0, rect.Height);
    }
}
