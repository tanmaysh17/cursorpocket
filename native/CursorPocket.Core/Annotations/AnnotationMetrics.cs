namespace CursorPocket.Core.Annotations;

/// <summary>
/// Derives mark weight and text size from the image being annotated instead of
/// hardcoding pixels. The first annotation surface drew every label at 32 px, which is
/// readable on a small region capture and nearly invisible on a 4K screenshot.
/// </summary>
public static class AnnotationMetrics
{
    /// <summary>A highlighter nib is this many times a pen stroke of the same step.</summary>
    public const double HighlightWidthFactor = 3.5;

    /// <summary>
    /// Pointer samples closer together than this add nothing to a stroke shape. Scaled
    /// off the image so a small region capture is not over-decimated.
    /// </summary>
    public static double StrokeSampleSpacing(int width, int height) =>
        Math.Max(1.5, ShortEdge(width, height) * 0.0015);

    public static double StrokeWidth(int width, int height, AnnotationSizeStep step)
    {
        var edge = ShortEdge(width, height);
        return step switch
        {
            AnnotationSizeStep.Small => Math.Clamp(edge * 0.0035, 2, 5),
            AnnotationSizeStep.Large => Math.Clamp(edge * 0.0100, 5, 16),
            _ => Math.Clamp(edge * 0.0060, 3, 9),
        };
    }

    public static double HighlightWidth(int width, int height, AnnotationSizeStep step) =>
        StrokeWidth(width, height, step) * HighlightWidthFactor;

    public static double TextSize(int width, int height, AnnotationSizeStep step)
    {
        var edge = ShortEdge(width, height);
        return step switch
        {
            AnnotationSizeStep.Small => Math.Clamp(edge * 0.022, 14, 36),
            AnnotationSizeStep.Large => Math.Clamp(edge * 0.058, 28, 88),
            _ => Math.Clamp(edge * 0.036, 20, 56),
        };
    }

    /// <summary>Steps the size up or down, stopping at the ends rather than wrapping.</summary>
    public static AnnotationSizeStep Step(AnnotationSizeStep current, int direction) =>
        (AnnotationSizeStep)Math.Clamp(
            (int)current + Math.Sign(direction),
            (int)AnnotationSizeStep.Small,
            (int)AnnotationSizeStep.Large);

    /// <summary>
    /// The short edge drives every size: a wide panorama and a tall column deserve the
    /// same weight of ink, and it is the short edge that limits legibility.
    /// </summary>
    private static double ShortEdge(int width, int height) => Math.Max(1, Math.Min(width, height));
}
