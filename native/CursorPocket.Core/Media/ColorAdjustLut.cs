namespace CursorPocket.Core.Media;

/// <summary>
/// Brightness / contrast / warmth as three 256-entry lookup tables applied in
/// one pass over BGRA pixels. Built once per settings change, never per frame.
/// </summary>
public sealed class ColorAdjustLut
{
    private readonly byte[] _blue = new byte[256];
    private readonly byte[] _green = new byte[256];
    private readonly byte[] _red = new byte[256];

    public ColorAdjustLut(int brightness, int contrast, int warmth)
    {
        var b = Math.Clamp(brightness, -100, 100) / 100d;
        var c = Math.Clamp(contrast, -100, 100) / 100d;
        var w = Math.Clamp(warmth, -100, 100) / 100d;
        for (var value = 0; value < 256; value++)
        {
            // Contrast pivots on mid gray; brightness is a plain offset. Both are
            // scaled down so the ±100 ends stay usable rather than blowing out.
            var adjusted = ((value / 255d - 0.5) * (1 + c * 0.8) + 0.5 + b * 0.4) * 255d;
            _red[value] = ClampToByte(adjusted + w * 22);
            _green[value] = ClampToByte(adjusted + w * 6);
            _blue[value] = ClampToByte(adjusted - w * 22);
        }
    }

    /// <summary>Applies the tables to a BGRA buffer in place. Alpha is untouched.</summary>
    public void Apply(Span<byte> bgra, int width, int height, int stride)
    {
        for (var row = 0; row < height; row++)
        {
            var line = bgra.Slice(row * stride, width * 4);
            for (var offset = 0; offset < line.Length; offset += 4)
            {
                line[offset] = _blue[line[offset]];
                line[offset + 1] = _green[line[offset + 1]];
                line[offset + 2] = _red[line[offset + 2]];
            }
        }
    }

    private static byte ClampToByte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}
