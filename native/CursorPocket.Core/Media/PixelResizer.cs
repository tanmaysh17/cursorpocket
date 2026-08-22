namespace CursorPocket.Core.Media;

/// <summary>
/// Integer-factor box downscale and bilinear upscale for tightly packed BGRA
/// buffers (stride = width * 4). The effect pipeline works on packed copies so
/// only the entry and exit points deal with the camera frame's stride.
/// </summary>
public static class PixelResizer
{
    /// <summary>Averages each factor×factor block. Output is width/factor × height/factor (floor, min 1).</summary>
    public static void Downscale(ReadOnlySpan<byte> source, int width, int height, int factor, Span<byte> destination, out int outWidth, out int outHeight)
    {
        if (factor < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(factor));
        }
        outWidth = Math.Max(1, width / factor);
        outHeight = Math.Max(1, height / factor);
        var samples = factor * factor;
        for (var y = 0; y < outHeight; y++)
        {
            for (var x = 0; x < outWidth; x++)
            {
                int b = 0, g = 0, r = 0;
                for (var sy = 0; sy < factor; sy++)
                {
                    // Clamped, not assumed in range: a frame narrower or shorter
                    // than the factor would otherwise read past the buffer.
                    var sourceY = Math.Min(y * factor + sy, height - 1);
                    for (var sx = 0; sx < factor; sx++)
                    {
                        var sourceX = Math.Min(x * factor + sx, width - 1);
                        var index = (sourceY * width + sourceX) * 4;
                        b += source[index];
                        g += source[index + 1];
                        r += source[index + 2];
                    }
                }
                var destIndex = (y * outWidth + x) * 4;
                destination[destIndex] = (byte)(b / samples);
                destination[destIndex + 1] = (byte)(g / samples);
                destination[destIndex + 2] = (byte)(r / samples);
                destination[destIndex + 3] = 255;
            }
        }
    }

    /// <summary>
    /// Nearest-neighbour upscale from a packed BGRA buffer to a packed destination of any
    /// larger size. Pixelation needs this rather than the bilinear path: interpolating
    /// between block averages smears the block edges back into a soft blur, which is both
    /// less legible as a deliberate redaction and easier to partially undo.
    /// </summary>
    public static void UpscaleNearest(ReadOnlySpan<byte> source, int sourceWidth, int sourceHeight, Span<byte> destination, int destWidth, int destHeight)
    {
        for (var y = 0; y < destHeight; y++)
        {
            var sy = Math.Min(sourceHeight - 1, y * sourceHeight / Math.Max(1, destHeight));
            for (var x = 0; x < destWidth; x++)
            {
                var sx = Math.Min(sourceWidth - 1, x * sourceWidth / Math.Max(1, destWidth));
                var sourceIndex = ((sy * sourceWidth) + sx) * 4;
                var destIndex = ((y * destWidth) + x) * 4;
                destination[destIndex] = source[sourceIndex];
                destination[destIndex + 1] = source[sourceIndex + 1];
                destination[destIndex + 2] = source[sourceIndex + 2];
                destination[destIndex + 3] = 255;
            }
        }
    }

    /// <summary>Bilinear upscale from a packed BGRA buffer to a packed destination of any larger size.</summary>
    public static void UpscaleBilinear(ReadOnlySpan<byte> source, int sourceWidth, int sourceHeight, Span<byte> destination, int destWidth, int destHeight)
    {
        var xRatio = sourceWidth > 1 ? (sourceWidth - 1d) / Math.Max(1, destWidth - 1) : 0d;
        var yRatio = sourceHeight > 1 ? (sourceHeight - 1d) / Math.Max(1, destHeight - 1) : 0d;
        for (var y = 0; y < destHeight; y++)
        {
            var sy = y * yRatio;
            var y0 = (int)sy;
            var y1 = Math.Min(y0 + 1, sourceHeight - 1);
            var fy = sy - y0;
            for (var x = 0; x < destWidth; x++)
            {
                var sx = x * xRatio;
                var x0 = (int)sx;
                var x1 = Math.Min(x0 + 1, sourceWidth - 1);
                var fx = sx - x0;
                var i00 = (y0 * sourceWidth + x0) * 4;
                var i10 = (y0 * sourceWidth + x1) * 4;
                var i01 = (y1 * sourceWidth + x0) * 4;
                var i11 = (y1 * sourceWidth + x1) * 4;
                var destIndex = (y * destWidth + x) * 4;
                for (var channel = 0; channel < 3; channel++)
                {
                    var top = source[i00 + channel] + (source[i10 + channel] - source[i00 + channel]) * fx;
                    var bottom = source[i01 + channel] + (source[i11 + channel] - source[i01 + channel]) * fx;
                    destination[destIndex + channel] = (byte)Math.Clamp(top + (bottom - top) * fy, 0, 255);
                }
                destination[destIndex + 3] = 255;
            }
        }
    }

    /// <summary>
    /// Bilinear resample of a single-channel float plane (used for the person
    /// mask) to an arbitrary destination size.
    /// </summary>
    public static void ResamplePlane(ReadOnlySpan<float> source, int sourceWidth, int sourceHeight, Span<float> destination, int destWidth, int destHeight)
    {
        var xRatio = sourceWidth > 1 ? (sourceWidth - 1d) / Math.Max(1, destWidth - 1) : 0d;
        var yRatio = sourceHeight > 1 ? (sourceHeight - 1d) / Math.Max(1, destHeight - 1) : 0d;
        for (var y = 0; y < destHeight; y++)
        {
            var sy = y * yRatio;
            var y0 = (int)sy;
            var y1 = Math.Min(y0 + 1, sourceHeight - 1);
            var fy = (float)(sy - y0);
            for (var x = 0; x < destWidth; x++)
            {
                var sx = x * xRatio;
                var x0 = (int)sx;
                var x1 = Math.Min(x0 + 1, sourceWidth - 1);
                var fx = (float)(sx - x0);
                var top = source[y0 * sourceWidth + x0] + (source[y0 * sourceWidth + x1] - source[y0 * sourceWidth + x0]) * fx;
                var bottom = source[y1 * sourceWidth + x0] + (source[y1 * sourceWidth + x1] - source[y1 * sourceWidth + x0]) * fx;
                destination[y * destWidth + x] = top + (bottom - top) * fy;
            }
        }
    }

    /// <summary>
    /// Center-crop-and-scale a packed BGRA image to fill a destination size,
    /// preserving aspect ratio (like Stretch=UniformToFill). Used to prepare a
    /// replacement background once per size change.
    /// </summary>
    public static byte[] CropToFill(ReadOnlySpan<byte> source, int sourceWidth, int sourceHeight, int destWidth, int destHeight)
    {
        var destination = new byte[destWidth * destHeight * 4];
        var scale = Math.Max(destWidth / (double)sourceWidth, destHeight / (double)sourceHeight);
        var cropWidth = destWidth / scale;
        var cropHeight = destHeight / scale;
        var originX = (sourceWidth - cropWidth) / 2;
        var originY = (sourceHeight - cropHeight) / 2;
        for (var y = 0; y < destHeight; y++)
        {
            var sy = Math.Clamp(originY + (y + 0.5) * cropHeight / destHeight, 0, sourceHeight - 1);
            var y0 = (int)sy;
            for (var x = 0; x < destWidth; x++)
            {
                var sx = Math.Clamp(originX + (x + 0.5) * cropWidth / destWidth, 0, sourceWidth - 1);
                var x0 = (int)sx;
                var sourceIndex = (y0 * sourceWidth + x0) * 4;
                var destIndex = (y * destWidth + x) * 4;
                destination[destIndex] = source[sourceIndex];
                destination[destIndex + 1] = source[sourceIndex + 1];
                destination[destIndex + 2] = source[sourceIndex + 2];
                destination[destIndex + 3] = 255;
            }
        }
        return destination;
    }
}
