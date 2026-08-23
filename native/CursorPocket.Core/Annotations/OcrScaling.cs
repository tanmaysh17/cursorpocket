namespace CursorPocket.Core.Annotations;

/// <summary>
/// Fits an image to what the OCR engine will accept, and maps the word boxes it returns
/// back onto the screenshot.
/// </summary>
/// <remarks>
/// This is the physical-pixels-versus-reported-coordinates trap in a new place. The
/// engine is handed a resampled copy and answers in that copy's coordinate space, so
/// every box has to come back through the same factor plus the region's own origin. Get
/// it wrong and the recognised text is right while every highlight sits in the wrong
/// place — which looks like a rendering bug rather than a units bug.
/// </remarks>
public static class OcrScaling
{
    /// <summary>
    /// The engine rejects an image with a side shorter than this outright.
    /// </summary>
    public const int MinimumSide = 40;

    /// <summary>
    /// Factor to multiply the region by before handing it over. Greater than one for a
    /// region too small to be accepted, less than one for one too large.
    /// </summary>
    /// <remarks>
    /// Scaling up past <see cref="MinimumSide"/> is deliberately not attempted. Measuring
    /// showed it does not help and can hurt: the engine wants document-like input, and
    /// enlarging a short wide strip only stretches its aspect ratio further from that.
    /// A 1882x160 strip of 103 px text comes back empty whether it was upscaled from
    /// 400x34 or drawn at that size to begin with, so the limit is the engine's and no
    /// amount of resampling here works around it.
    /// </remarks>
    public static double ScaleFor(int width, int height, int maximumDimension)
    {
        if (width <= 0 || height <= 0 || maximumDimension <= 0)
        {
            return 1;
        }

        var scale = 1d;
        var shortest = Math.Min(width, height);
        if (shortest < MinimumSide)
        {
            scale = (double)MinimumSide / shortest;
        }

        // The ceiling wins over the floor: exceeding the engine's maximum is a hard
        // rejection, whereas falling short of the minimum only costs accuracy.
        var longest = Math.Max(width, height) * scale;
        if (longest > maximumDimension)
        {
            scale *= maximumDimension / longest;
        }

        return scale;
    }

    /// <summary>The size actually handed to the engine, never smaller than one pixel.</summary>
    public static (int Width, int Height) Scaled(int width, int height, double scale) =>
        (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));

    /// <summary>
    /// Maps a box the engine reported back into screenshot pixels: undo the resample,
    /// then shift by where the region started.
    /// </summary>
    public static AnnRect ToSource(AnnRect box, double scale, AnnPoint origin)
    {
        var factor = scale <= 0 ? 1 : scale;
        return new AnnRect(
            origin.X + (box.X / factor),
            origin.Y + (box.Y / factor),
            box.Width / factor,
            box.Height / factor);
    }

    /// <summary>
    /// True when a region cannot be made acceptable at all — one side is far too short
    /// while the other is already at the ceiling, so no single factor satisfies both.
    /// </summary>
    public static bool CannotBeRead(int width, int height, int maximumDimension)
    {
        if (width <= 0 || height <= 0)
        {
            return true;
        }

        var scale = ScaleFor(width, height, maximumDimension);
        var (scaledWidth, scaledHeight) = Scaled(width, height, scale);
        return Math.Min(scaledWidth, scaledHeight) < MinimumSide;
    }
}
