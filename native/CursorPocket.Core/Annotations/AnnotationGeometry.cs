namespace CursorPocket.Core.Annotations;

/// <summary>
/// The single source of truth for every annotation shape. The live preview and the
/// exported PNG both consume these point lists, which is what makes what-you-see and
/// what-you-save the same thing by construction rather than by discipline.
/// </summary>
/// <remarks>
/// The first annotation surface computed its shapes twice — once as WinUI shapes for
/// the preview and once with GDI+ for the export — and the two had already drifted
/// three ways: the preview drew a triangular line cap where the export drew a
/// pen-width-scaled arrow anchor, the preview filled a rectangle the export left
/// hollow, and a 5 px preview stroke exported at 6 px. Anything a renderer would
/// otherwise have to reinvent belongs in this file.
/// </remarks>
public static class AnnotationGeometry
{
    /// <summary>Arrow head length as a multiple of stroke width.</summary>
    public const double ArrowHeadLength = 3.2;

    /// <summary>Arrow head half-width as a multiple of stroke width.</summary>
    public const double ArrowHeadHalfWidth = 1.9;

    /// <summary>
    /// The closed outline of an arrow: shaft plus head, as one filled polygon. A filled
    /// polygon rather than a stroked line with an end cap, because every cap style
    /// differs between renderers and scales with pen width in renderer-specific ways.
    /// A polygon is the same shape everywhere and at every scale.
    /// </summary>
    public static IReadOnlyList<AnnPoint> ArrowOutline(AnnPoint start, AnnPoint end, double strokeWidth)
    {
        var width = Math.Max(strokeWidth, 0.5);
        var span = end - start;
        var length = span.Length;
        if (length < 1e-6)
        {
            // A zero-length arrow has no direction to point in. Return an empty outline
            // rather than dividing by zero and drawing a NaN polygon.
            return [];
        }

        var direction = new AnnPoint(span.X / length, span.Y / length);
        var normal = new AnnPoint(-direction.Y, direction.X);

        // A short drag must not produce a head longer than the arrow itself: clamp, so
        // the shape degrades into a plain triangle rather than folding inside out.
        var headLength = Math.Min(width * ArrowHeadLength, length);
        var headHalf = Math.Max(width * ArrowHeadHalfWidth, (width * 0.5) + 0.5);
        var shaftHalf = width / 2;
        var headBase = end - (direction * headLength);

        return
        [
            start + (normal * shaftHalf),
            headBase + (normal * shaftHalf),
            headBase + (normal * headHalf),
            end,
            headBase - (normal * headHalf),
            headBase - (normal * shaftHalf),
            start - (normal * shaftHalf),
        ];
    }

    /// <summary>
    /// Drops points closer together than <paramref name="minimumDistance"/>. Pointer
    /// moves arrive far denser than a stroke needs, and smoothing a dense list costs
    /// work without changing the result.
    /// </summary>
    public static IReadOnlyList<AnnPoint> Decimate(IReadOnlyList<AnnPoint> points, double minimumDistance)
    {
        if (points.Count <= 2)
        {
            return points;
        }

        var kept = new List<AnnPoint>(points.Count) { points[0] };
        for (var index = 1; index < points.Count - 1; index++)
        {
            if ((points[index] - kept[^1]).Length >= minimumDistance)
            {
                kept.Add(points[index]);
            }
        }

        // The last point is always kept: a stroke must end where the pointer was
        // released, not at the last sample that happened to clear the threshold.
        kept.Add(points[^1]);
        return kept;
    }

    /// <summary>
    /// Chaikin corner cutting. Turns the polyline a pointer actually produces into
    /// something that reads as a drawn line rather than a jagged sample trail.
    /// Endpoints are preserved, so a stroke still starts and ends where the user did.
    /// </summary>
    public static IReadOnlyList<AnnPoint> Smooth(IReadOnlyList<AnnPoint> points, int iterations)
    {
        if (points.Count < 3 || iterations <= 0)
        {
            return points;
        }

        var current = points;
        for (var pass = 0; pass < iterations; pass++)
        {
            var next = new List<AnnPoint>((current.Count * 2) - 1) { current[0] };
            for (var index = 0; index < current.Count - 1; index++)
            {
                var a = current[index];
                var b = current[index + 1];
                next.Add(new AnnPoint((a.X * 0.75) + (b.X * 0.25), (a.Y * 0.75) + (b.Y * 0.25)));
                next.Add(new AnnPoint((a.X * 0.25) + (b.X * 0.75), (a.Y * 0.25) + (b.Y * 0.75)));
            }

            next.Add(current[^1]);
            current = next;
        }

        return current;
    }

    /// <summary>
    /// Snaps a line or arrow to the nearest 45 degrees while Shift is held, keeping the
    /// length the user dragged so the mark does not jump as it snaps.
    /// </summary>
    public static AnnPoint ConstrainToAngle(AnnPoint anchor, AnnPoint free)
    {
        var span = free - anchor;
        var length = span.Length;
        if (length < 1e-6)
        {
            return free;
        }

        const double step = Math.PI / 4;
        var snapped = Math.Round(Math.Atan2(span.Y, span.X) / step) * step;
        return new AnnPoint(anchor.X + (Math.Cos(snapped) * length), anchor.Y + (Math.Sin(snapped) * length));
    }

    /// <summary>
    /// The rectangle a drag describes, under Shift (square) and Alt (centred on the
    /// press point). The two compose, giving a centred square.
    /// </summary>
    public static AnnRect RectFromDrag(AnnPoint press, AnnPoint current, DrawModifiers modifiers)
    {
        var dx = current.X - press.X;
        var dy = current.Y - press.Y;

        if (modifiers.HasFlag(DrawModifiers.Constrain))
        {
            var side = Math.Max(Math.Abs(dx), Math.Abs(dy));
            dx = side * (dx < 0 ? -1 : 1);
            dy = side * (dy < 0 ? -1 : 1);
        }

        if (modifiers.HasFlag(DrawModifiers.CenterOnPress))
        {
            return new AnnRect(
                press.X - Math.Abs(dx),
                press.Y - Math.Abs(dy),
                Math.Abs(dx) * 2,
                Math.Abs(dy) * 2);
        }

        return AnnRect.FromCorners(press, new AnnPoint(press.X + dx, press.Y + dy));
    }

    /// <summary>
    /// Clamps a rectangle to the image, so a drag that runs off the edge cannot place a
    /// mark in pixels that do not exist.
    /// </summary>
    public static AnnRect ClampToImage(AnnRect rect, int imageWidth, int imageHeight)
    {
        var left = Math.Clamp(rect.X, 0, imageWidth);
        var top = Math.Clamp(rect.Y, 0, imageHeight);
        var right = Math.Clamp(rect.Right, 0, imageWidth);
        var bottom = Math.Clamp(rect.Bottom, 0, imageHeight);
        return new AnnRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }
}
