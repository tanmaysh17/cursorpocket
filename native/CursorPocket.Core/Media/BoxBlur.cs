namespace CursorPocket.Core.Media;

/// <summary>
/// Separable box blur over packed BGRA buffers. Three iterations approximate a
/// Gaussian; the pipeline always runs it on a downscaled copy, so the cost is
/// a few thousand pixels rather than the full frame.
/// </summary>
public static class BoxBlur
{
    /// <summary>Blurs in place. <paramref name="scratch"/> must be at least as large as <paramref name="pixels"/>.</summary>
    public static void Apply(Span<byte> pixels, int width, int height, int radius, Span<byte> scratch, int iterations = 1)
    {
        if (radius < 1 || width < 2 || height < 2)
        {
            return;
        }
        for (var pass = 0; pass < iterations; pass++)
        {
            BlurHorizontal(pixels, width, height, radius, scratch);
            BlurVertical(scratch, width, height, radius, pixels);
        }
    }

    private static void BlurHorizontal(ReadOnlySpan<byte> source, int width, int height, int radius, Span<byte> destination)
    {
        var window = radius * 2 + 1;
        for (var y = 0; y < height; y++)
        {
            var row = y * width * 4;
            int b = 0, g = 0, r = 0;
            for (var x = -radius; x <= radius; x++)
            {
                var index = row + Math.Clamp(x, 0, width - 1) * 4;
                b += source[index];
                g += source[index + 1];
                r += source[index + 2];
            }
            for (var x = 0; x < width; x++)
            {
                var destIndex = row + x * 4;
                destination[destIndex] = (byte)(b / window);
                destination[destIndex + 1] = (byte)(g / window);
                destination[destIndex + 2] = (byte)(r / window);
                destination[destIndex + 3] = 255;
                var leaving = row + Math.Clamp(x - radius, 0, width - 1) * 4;
                var entering = row + Math.Clamp(x + radius + 1, 0, width - 1) * 4;
                b += source[entering] - source[leaving];
                g += source[entering + 1] - source[leaving + 1];
                r += source[entering + 2] - source[leaving + 2];
            }
        }
    }

    private static void BlurVertical(ReadOnlySpan<byte> source, int width, int height, int radius, Span<byte> destination)
    {
        var window = radius * 2 + 1;
        var strideBytes = width * 4;
        for (var x = 0; x < width; x++)
        {
            var column = x * 4;
            int b = 0, g = 0, r = 0;
            for (var y = -radius; y <= radius; y++)
            {
                var index = Math.Clamp(y, 0, height - 1) * strideBytes + column;
                b += source[index];
                g += source[index + 1];
                r += source[index + 2];
            }
            for (var y = 0; y < height; y++)
            {
                var destIndex = y * strideBytes + column;
                destination[destIndex] = (byte)(b / window);
                destination[destIndex + 1] = (byte)(g / window);
                destination[destIndex + 2] = (byte)(r / window);
                destination[destIndex + 3] = 255;
                var leaving = Math.Clamp(y - radius, 0, height - 1) * strideBytes + column;
                var entering = Math.Clamp(y + radius + 1, 0, height - 1) * strideBytes + column;
                b += source[entering] - source[leaving];
                g += source[entering + 1] - source[leaving + 1];
                r += source[entering + 2] - source[leaving + 2];
            }
        }
    }

    /// <summary>Small box blur over a single-channel float plane (mask feathering), in place.</summary>
    public static void FeatherPlane(Span<float> plane, int width, int height, int radius, Span<float> scratch)
    {
        if (radius < 1 || width < 2 || height < 2)
        {
            return;
        }
        var window = radius * 2 + 1;
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            var sum = 0f;
            for (var x = -radius; x <= radius; x++)
            {
                sum += plane[row + Math.Clamp(x, 0, width - 1)];
            }
            for (var x = 0; x < width; x++)
            {
                scratch[row + x] = sum / window;
                sum += plane[row + Math.Clamp(x + radius + 1, 0, width - 1)] - plane[row + Math.Clamp(x - radius, 0, width - 1)];
            }
        }
        for (var x = 0; x < width; x++)
        {
            var sum = 0f;
            for (var y = -radius; y <= radius; y++)
            {
                sum += scratch[Math.Clamp(y, 0, height - 1) * width + x];
            }
            for (var y = 0; y < height; y++)
            {
                plane[y * width + x] = sum / window;
                sum += scratch[Math.Clamp(y + radius + 1, 0, height - 1) * width + x] - scratch[Math.Clamp(y - radius, 0, height - 1) * width + x];
            }
        }
    }
}
