namespace CursorPocket.Core.Media;

/// <summary>
/// Chooses which part of the camera frame to keep when the self-view's shape is a
/// different aspect than the camera.
/// <para>
/// A 1:1 squircle showing a 4:3 webcam has to drop a quarter of the width. Cropping
/// down the middle of the *frame* is not the same as cropping around the *person* —
/// sit slightly off to one side and a centred crop cuts you in half. When a person
/// mask is available the crop follows them instead; without one it falls back to
/// the centre, which is the old behaviour.
/// </para>
/// </summary>
public static class AutoFrameCrop
{
    /// <summary>
    /// Horizontal centre of mass of the mask, as a 0..1 fraction of the width, or
    /// null when the mask is too empty to be meaningful (nobody in frame).
    /// </summary>
    public static double? HorizontalCentroid(ReadOnlySpan<float> mask, int width, int height)
    {
        double weighted = 0;
        double total = 0;
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var weight = mask[row + x];
                if (weight <= 0.15f)
                {
                    // Ignore the faint halo around the subject; it drags the centre
                    // toward the middle and undoes the whole point.
                    continue;
                }
                weighted += weight * x;
                total += weight;
            }
        }
        // Below roughly 1% coverage there is no subject to centre on.
        return total < width * height * 0.01 ? null : weighted / total / Math.Max(1, width - 1);
    }

    /// <summary>
    /// The rectangle to keep from a <paramref name="frameWidth"/>×<paramref name="frameHeight"/>
    /// frame so it fills <paramref name="targetAspect"/> (width ÷ height), positioned
    /// around <paramref name="focusX"/> (0..1 across the width, 0.5 = centred).
    /// Always returns a rectangle fully inside the frame.
    /// </summary>
    public static (int X, int Y, int Width, int Height) Compute(
        int frameWidth,
        int frameHeight,
        double targetAspect,
        double focusX = 0.5)
    {
        if (frameWidth < 2 || frameHeight < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(frameWidth), "The frame must have a visible size.");
        }
        if (!double.IsFinite(targetAspect) || targetAspect <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetAspect), "The target aspect must be positive.");
        }
        var frameAspect = frameWidth / (double)frameHeight;
        int width, height;
        if (frameAspect > targetAspect)
        {
            // Frame is wider than the shape: keep full height, trim the sides.
            height = frameHeight;
            width = (int)Math.Round(frameHeight * targetAspect);
        }
        else
        {
            // Frame is taller: keep full width, trim top and bottom.
            width = frameWidth;
            height = (int)Math.Round(frameWidth / targetAspect);
        }
        width = Math.Clamp(width, 2, frameWidth);
        height = Math.Clamp(height, 2, frameHeight);
        // Even dimensions keep the downstream 2x downscale exact.
        width -= width % 2;
        height -= height % 2;

        var focus = double.IsFinite(focusX) ? Math.Clamp(focusX, 0, 1) : 0.5;
        var x = (int)Math.Round(focus * frameWidth - width / 2d);
        x = Math.Clamp(x, 0, frameWidth - width);
        // Vertical stays centred: faces sit near the top of a webcam frame already,
        // and a mask-driven vertical crop bobs distractingly as someone moves.
        var y = Math.Clamp((frameHeight - height) / 2, 0, frameHeight - height);
        return (x, y, width, height);
    }

    /// <summary>Copies a crop rectangle out of a packed BGRA frame into a packed destination.</summary>
    public static void CopyCrop(
        ReadOnlySpan<byte> source,
        int sourceWidth,
        (int X, int Y, int Width, int Height) crop,
        Span<byte> destination)
    {
        for (var row = 0; row < crop.Height; row++)
        {
            var from = ((crop.Y + row) * sourceWidth + crop.X) * 4;
            source.Slice(from, crop.Width * 4).CopyTo(destination.Slice(row * crop.Width * 4, crop.Width * 4));
        }
    }
}
