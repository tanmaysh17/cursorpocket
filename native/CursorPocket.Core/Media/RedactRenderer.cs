using CursorPocket.Core.Annotations;

namespace CursorPocket.Core.Media;

/// <summary>
/// Obliterates a patch of a screenshot in place, over a packed BGRA buffer
/// (stride = width * 4).
/// </summary>
/// <remarks>
/// <para>
/// Solid is the default, and that is a safety decision rather than an aesthetic one.
/// Pixelation and blur both derive their output from the pixels underneath, so for text
/// they are only partially destructive — the glyph shapes leak through the block
/// averages, and a determined reader (or a solver) can often recover short strings like
/// a password or an account number. Solid replaces the pixels outright and leaves
/// nothing to recover.
/// </para>
/// <para>
/// Every mode is deterministic: a block takes the mean of its source pixels, so two
/// renders of the same document are byte-identical and the on-screen patch and the
/// exported patch cannot disagree. Nothing here reads a clock or a random number
/// generator, which also rules out a framework upgrade silently changing old output.
/// </para>
/// </remarks>
public static class RedactRenderer
{
    /// <summary>Smallest pixelation block, in source pixels.</summary>
    public const int MinimumBlockSize = 6;

    /// <summary>
    /// Block size for a patch. Scaled to the patch rather than fixed, so redacting one
    /// word and redacting a whole panel both read as deliberately obscured instead of
    /// the small one coming out barely touched.
    /// </summary>
    public static int BlockSizeFor(int width, int height)
    {
        var shortEdge = Math.Max(1, Math.Min(width, height));
        return Math.Max(MinimumBlockSize, shortEdge / 8);
    }

    /// <summary>
    /// Blur radius for a patch. Deliberately large: a timid blur over text is the
    /// classic redaction failure.
    /// </summary>
    public static int BlurRadiusFor(int width, int height) =>
        Math.Max(2, Math.Min(width, height) / 10);

    public static void Apply(Span<byte> patch, int width, int height, RedactStyle style, AnnColor solid)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        switch (style)
        {
            case RedactStyle.Pixelate:
                Pixelate(patch, width, height);
                break;
            case RedactStyle.Blur:
                Blur(patch, width, height);
                break;
            default:
                Fill(patch, width, height, solid);
                break;
        }
    }

    private static void Fill(Span<byte> patch, int width, int height, AnnColor colour)
    {
        for (var index = 0; index < width * height * 4; index += 4)
        {
            patch[index] = colour.B;
            patch[index + 1] = colour.G;
            patch[index + 2] = colour.R;
            // Always opaque. A translucent redaction is not a redaction.
            patch[index + 3] = 255;
        }
    }

    private static void Pixelate(Span<byte> patch, int width, int height)
    {
        var block = BlockSizeFor(width, height);

        // Downscale then upscale nearest, which is exactly "average each block and hold
        // it flat". Going through the resizer rather than averaging in place keeps the
        // block grid anchored to the patch origin, so resizing a redaction adds and
        // removes whole blocks instead of shifting every one of them.
        var small = new byte[width * height * 4];
        PixelResizer.Downscale(patch, width, height, block, small, out var smallWidth, out var smallHeight);
        if (smallWidth <= 0 || smallHeight <= 0)
        {
            // The patch is smaller than one block, so there is nothing to average
            // towards. Flatten it to its own mean instead of leaving it untouched.
            Fill(patch, width, height, Mean(patch, width, height));
            return;
        }

        PixelResizer.UpscaleNearest(small.AsSpan(0, smallWidth * smallHeight * 4), smallWidth, smallHeight, patch, width, height);
    }

    private static void Blur(Span<byte> patch, int width, int height)
    {
        var scratch = new byte[width * height * 4];
        // Three passes approximate a Gaussian; one box pass still shows the text.
        BoxBlur.Apply(patch, width, height, BlurRadiusFor(width, height), scratch, iterations: 3);
    }

    private static AnnColor Mean(ReadOnlySpan<byte> patch, int width, int height)
    {
        long b = 0, g = 0, r = 0;
        var count = width * height;
        for (var index = 0; index < count * 4; index += 4)
        {
            b += patch[index];
            g += patch[index + 1];
            r += patch[index + 2];
        }

        return new AnnColor(255, (byte)(r / count), (byte)(g / count), (byte)(b / count));
    }
}
