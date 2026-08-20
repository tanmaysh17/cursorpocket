namespace CursorPocket.Core.Media;

/// <summary>
/// Zoom-style "touch up my appearance": blend each pixel toward a softened
/// copy, keeping high-contrast detail (eyes, hair edges, glasses) sharp via a
/// luma-difference clamp. When a person mask is available the smoothing is
/// confined to the person; without one it runs globally at reduced strength.
/// </summary>
public static class TouchUpFilter
{
    /// <summary>Blend strength for levels 1 (subtle) and 2 (strong).</summary>
    public static double StrengthFor(int level, bool hasMask)
    {
        var strength = level switch
        {
            <= 0 => 0d,
            1 => 0.45,
            _ => 0.75,
        };
        // Global smoothing also softens the scene behind the person, so cap it lower.
        return hasMask ? strength : strength * 0.6;
    }

    /// <summary>
    /// Blends <paramref name="pixels"/> toward <paramref name="softened"/> (both
    /// packed BGRA, same size) in place. <paramref name="mask"/> is a per-pixel
    /// 0..1 person weight or empty for global smoothing.
    /// </summary>
    public static void Apply(Span<byte> pixels, ReadOnlySpan<byte> softened, ReadOnlySpan<float> mask, int width, int height, double strength)
    {
        if (strength <= 0)
        {
            return;
        }
        const double edgeThreshold = 34d;
        for (var index = 0; index < width * height; index++)
        {
            var offset = index * 4;
            var lumaOriginal = Luma(pixels, offset);
            var lumaSoft = Luma(softened, offset);
            // Where the softened copy differs a lot from the original we are on
            // an edge worth keeping; fade the smoothing out there.
            var edgeKeep = 1 - Math.Clamp(Math.Abs(lumaSoft - lumaOriginal) / edgeThreshold, 0, 1);
            var weight = strength * edgeKeep * (mask.IsEmpty ? 1 : mask[index]);
            if (weight <= 0)
            {
                continue;
            }
            for (var channel = 0; channel < 3; channel++)
            {
                var original = pixels[offset + channel];
                pixels[offset + channel] = (byte)Math.Clamp(original + (softened[offset + channel] - original) * weight, 0, 255);
            }
        }
    }

    private static double Luma(ReadOnlySpan<byte> bgra, int offset) =>
        0.114 * bgra[offset] + 0.587 * bgra[offset + 1] + 0.299 * bgra[offset + 2];
}
