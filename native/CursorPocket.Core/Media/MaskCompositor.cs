namespace CursorPocket.Core.Media;

/// <summary>Blends the person over a replacement background using a 0..1 alpha plane.</summary>
public static class MaskCompositor
{
    /// <summary>
    /// In place: <c>pixels = pixels·mask + background·(1−mask)</c>. Both buffers
    /// are packed BGRA of the same dimensions.
    /// </summary>
    public static void Composite(Span<byte> pixels, ReadOnlySpan<byte> background, ReadOnlySpan<float> mask, int width, int height)
    {
        for (var index = 0; index < width * height; index++)
        {
            var alpha = Math.Clamp(mask[index], 0f, 1f);
            if (alpha >= 1f)
            {
                continue;
            }
            var offset = index * 4;
            var inverse = 1f - alpha;
            pixels[offset] = (byte)(pixels[offset] * alpha + background[offset] * inverse);
            pixels[offset + 1] = (byte)(pixels[offset + 1] * alpha + background[offset + 1] * inverse);
            pixels[offset + 2] = (byte)(pixels[offset + 2] * alpha + background[offset + 2] * inverse);
            pixels[offset + 3] = 255;
        }
    }
}
