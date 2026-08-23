namespace CursorPocket.Core.Annotations;

/// <summary>
/// Decides what number the next step marker gets.
/// </summary>
/// <remarks>
/// One above the highest marker currently on the image, with gaps allowed. Deriving it
/// rather than storing a counter is what makes undo behave: undo marker 3 and the next
/// marker is 3 again, with no separate counter to rewind. Gaps are deliberate too —
/// renumbering after a delete would silently invalidate a text mark that says "see 2".
/// </remarks>
public static class MarkerNumbering
{
    public static int Next(IReadOnlyList<AnnotationMark> marks)
    {
        var highest = 0;
        foreach (var mark in marks)
        {
            if (mark is MarkerMark marker && marker.Number > highest)
            {
                highest = marker.Number;
            }
        }

        return highest + 1;
    }

    /// <summary>
    /// Radius for a marker on this image. Sized off the short edge for the same reason
    /// every other weight is: a fixed radius is a dot on a 4K shot and a blot on a small
    /// region capture.
    /// </summary>
    public static double RadiusFor(int width, int height, AnnotationSizeStep step)
    {
        var shortEdge = Math.Max(1, Math.Min(width, height));
        var scale = step switch
        {
            AnnotationSizeStep.Small => 0.020,
            AnnotationSizeStep.Large => 0.048,
            _ => 0.032,
        };

        return Math.Clamp(shortEdge * scale, 12, 72);
    }
}
