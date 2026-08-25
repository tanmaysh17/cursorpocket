using CursorPocket.Core.Annotations;

namespace CursorPocket.Core.Media;

/// <summary>
/// Renders the colour wheel shown on the custom-ink swatch until a colour has been
/// sampled, as a packed BGRA buffer (stride = size * 4).
/// </summary>
/// <remarks>
/// Generated rather than drawn with a brush because WinUI has no conic or sweep gradient
/// — it ships LinearGradientBrush and RadialGradientBrush only. Faking one with a fan of
/// wedge-shaped Paths bands visibly at swatch size, and this is about thirty lines of
/// arithmetic that is also directly testable.
/// </remarks>
public static class ConicWheel
{
    /// <summary>
    /// The hues swept around the wheel. These are the wheel's own decoration, not ink the
    /// user can paint with, so unlike <see cref="AnnotationPalette"/> they are free to
    /// include hues that would collide with the app's state colours.
    /// </summary>
    private static readonly AnnColor[] Stops =
    [
        AnnColor.FromHex("#FF3B30"),
        AnnColor.FromHex("#FF9500"),
        AnnColor.FromHex("#FFD60A"),
        AnnColor.FromHex("#34C759"),
        AnnColor.FromHex("#0A84FF"),
        AnnColor.FromHex("#BF5AF2"),
    ];

    public static byte[] Render(int size)
    {
        var pixels = new byte[size * size * 4];
        if (size <= 0)
        {
            return pixels;
        }

        var centre = (size - 1) / 2d;
        var radius = size / 2d;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var dx = x - centre;
                var dy = y - centre;
                var distance = Math.Sqrt((dx * dx) + (dy * dy));
                var index = ((y * size) + x) * 4;

                // One pixel of coverage at the rim, so the disc does not read as a
                // staircase at 26 px.
                var coverage = Math.Clamp(radius - distance, 0, 1);
                if (coverage <= 0)
                {
                    continue;
                }

                var angle = Math.Atan2(dy, dx);
                if (angle < 0)
                {
                    angle += Math.PI * 2;
                }

                var colour = Sweep(angle / (Math.PI * 2));
                pixels[index] = colour.B;
                pixels[index + 1] = colour.G;
                pixels[index + 2] = colour.R;
                pixels[index + 3] = (byte)Math.Round(coverage * 255);
            }
        }

        return pixels;
    }

    /// <summary>Colour at a fraction of the way round the wheel, wrapping at the seam.</summary>
    private static AnnColor Sweep(double position)
    {
        var scaled = position * Stops.Length;
        var first = (int)scaled % Stops.Length;
        var second = (first + 1) % Stops.Length;
        var blend = scaled - Math.Floor(scaled);

        var a = Stops[first];
        var b = Stops[second];
        return new AnnColor(
            255,
            (byte)Math.Round(a.R + ((b.R - a.R) * blend)),
            (byte)Math.Round(a.G + ((b.G - a.G) * blend)),
            (byte)Math.Round(a.B + ((b.B - a.B) * blend)));
    }
}
