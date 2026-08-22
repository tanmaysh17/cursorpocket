using System.Drawing;
using System.Drawing.Imaging;
using CursorPocket.Core.Annotations;
using CursorPocket_App;

namespace CursorPocket.Tests;

/// <summary>
/// Pixel tests over the real export path. The exporter has no WinUI dependency, so it is
/// compiled into this project and driven directly — which is the only way to assert what
/// actually lands in the file rather than what the source code says it will.
/// </summary>
public sealed class AnnotationExportTests
{
    [Fact]
    public void A_solid_redaction_leaves_one_flat_colour_and_nothing_of_the_content()
    {
        using var source = Striped(120, 60);
        var ink = AnnotationPalette.Inks[0].Colour;

        using var result = Export(source, [Redact(source, new AnnRect(20, 10, 60, 30), RedactStyle.Solid, ink)]);

        // Every pixel inside the rect is the ink colour, exactly.
        for (var y = 12; y < 38; y++)
        {
            for (var x = 22; x < 78; x++)
            {
                var pixel = result.GetPixel(x, y);
                Assert.Equal((ink.R, ink.G, ink.B), (pixel.R, pixel.G, pixel.B));
            }
        }

        // ...and the stripes outside it are untouched, so the redaction did not leak.
        Assert.NotEqual((ink.R, ink.G, ink.B), Tuple(result.GetPixel(5, 30)));
    }

    [Fact]
    public void A_pixelated_redaction_destroys_the_content_without_flattening_it()
    {
        using var source = Striped(160, 80);

        using var result = Export(source, [Redact(source, new AnnRect(10, 10, 140, 60), RedactStyle.Pixelate, AnnotationPalette.Inks[0].Colour)]);

        var before = Contrast(source, new Rectangle(14, 14, 130, 50));
        var after = Contrast(result, new Rectangle(14, 14, 130, 50));
        Assert.True(after < before / 3, $"contrast {before:F1} -> {after:F1} is not enough to obscure text");
        // But not a flat block — pixelation keeps the coarse shape, which is the whole
        // reason it is the weaker of the two and not the default.
        Assert.True(after > 0.01, "a pixelated region should not be perfectly uniform");
    }

    [Fact]
    public void A_highlighter_stroke_does_not_darken_itself_where_it_doubles_back()
    {
        using var source = Flat(120, 60, Color.White);

        // One stroke out to x=95 and back to x=55, so x in [55,95] is covered twice by a
        // single stroke and x in [15,55] once. On paper a highlighter does not darken
        // where the same pass crosses over, and neither may this.
        var stroke = new StrokeMark
        {
            Id = 1,
            Colour = AnnotationPalette.Inks[2].Colour.WithAlpha(AnnotationPalette.HighlightAlpha),
            StrokeWidth = 14,
            Highlight = true,
            Points = [new AnnPoint(15, 30), new AnnPoint(95, 30), new AnnPoint(55, 30)],
        };

        using var result = Export(source, [stroke]);

        var single = result.GetPixel(30, 30);
        var doubled = result.GetPixel(75, 30);
        // Allow one unit of rounding, but not the compounding the old path produced.
        Assert.True(
            Math.Abs(single.R - doubled.R) <= 2
            && Math.Abs(single.G - doubled.G) <= 2
            && Math.Abs(single.B - doubled.B) <= 2,
            $"single pass {single.R},{single.G},{single.B} vs doubled {doubled.R},{doubled.G},{doubled.B}");
    }

    [Fact]
    public void A_highlighter_still_lets_the_content_through()
    {
        using var source = Flat(60, 40, Color.White);
        var stroke = new StrokeMark
        {
            Id = 1,
            Colour = AnnotationPalette.Inks[2].Colour.WithAlpha(AnnotationPalette.HighlightAlpha),
            StrokeWidth = 12,
            Highlight = true,
            Points = [new AnnPoint(10, 20), new AnnPoint(50, 20)],
        };

        using var result = Export(source, [stroke]);

        // Translucent, not opaque: the white ground still lifts the ink.
        var inked = result.GetPixel(30, 20);
        var pure = stroke.Colour;
        Assert.NotEqual((pure.R, pure.G, pure.B), (inked.R, inked.G, inked.B));
        Assert.NotEqual((255, 255, 255), (inked.R, inked.G, inked.B));
    }

    [Fact]
    public void An_arrow_reaches_the_point_it_was_dragged_to()
    {
        using var source = Flat(100, 100, Color.White);
        var ink = AnnotationPalette.Inks[0].Colour;
        var arrow = new ArrowMark
        {
            Id = 1,
            Colour = ink,
            StrokeWidth = 6,
            Start = new AnnPoint(15, 50),
            End = new AnnPoint(85, 50),
        };

        using var result = Export(source, [arrow]);

        // An arrow that stops short of what it points at is the reason the head is a
        // filled polygon rather than a line cap.
        Assert.True(IsInked(result.GetPixel(84, 50)), "the arrow tip is not inked");
        Assert.True(IsInked(result.GetPixel(20, 50)), "the arrow shaft is not inked");
        // The head is wider than the shaft.
        Assert.True(IsInked(result.GetPixel(70, 42)), "the arrow head is too narrow");
        Assert.False(IsInked(result.GetPixel(20, 42)), "the shaft is as wide as the head");
    }

    [Fact]
    public void A_filled_box_is_filled_and_a_hollow_one_is_not()
    {
        using var source = Flat(100, 100, Color.White);
        var ink = AnnotationPalette.Inks[3].Colour;

        using var filled = Export(source, [Box(ink, filled: true)]);
        using var hollow = Export(source, [Box(ink, filled: false)]);

        // The old exporter previewed a fill on every box and wrote none of it.
        Assert.False(IsWhite(filled.GetPixel(50, 50)), "a filled box has no fill in the file");
        Assert.True(IsWhite(hollow.GetPixel(50, 50)), "a hollow box was filled anyway");
        // Both still have an edge. Probed on the left edge itself (x=15), not inside it:
        // a 5 px stroke centred there covers roughly x 13 to 18.
        Assert.False(IsWhite(filled.GetPixel(15, 50)));
        Assert.False(IsWhite(hollow.GetPixel(15, 50)));
    }

    [Fact]
    public void A_step_marker_carries_a_readable_digit()
    {
        using var source = Flat(80, 80, Color.White);
        var ink = AnnotationPalette.Inks[2].Colour;
        var marker = new MarkerMark
        {
            Id = 1,
            Colour = ink,
            StrokeWidth = 4,
            Center = new AnnPoint(40, 40),
            Number = 3,
            Radius = 24,
        };

        using var result = Export(source, [marker]);

        var expected = AnnotationPalette.OnInk(ink);
        var digitPixels = 0;
        for (var y = 24; y < 56; y++)
        {
            for (var x = 24; x < 56; x++)
            {
                var pixel = result.GetPixel(x, y);
                if (Math.Abs(pixel.R - expected.R) < 40
                    && Math.Abs(pixel.G - expected.G) < 40
                    && Math.Abs(pixel.B - expected.B) < 40)
                {
                    digitPixels++;
                }
            }
        }

        Assert.True(digitPixels > 20, $"only {digitPixels} pixels of digit — the number is not legible");
        // The disc itself is the ink colour.
        Assert.True(Close(result.GetPixel(40, 20), ink), "the marker disc is not the ink colour");
    }

    [Fact]
    public void A_focus_region_dims_the_outside_and_leaves_the_inside_alone()
    {
        using var source = Flat(120, 120, Color.White);
        var focus = new FocusMark
        {
            Id = 1,
            Colour = AnnotationPalette.Inks[0].Colour,
            StrokeWidth = 3,
            Rect = new AnnRect(40, 40, 40, 40),
            Mode = FocusMode.Dim,
            Shape = FocusShape.Rectangle,
        };

        using var result = Export(source, [focus]);

        Assert.True(IsWhite(result.GetPixel(60, 60)), "the inside of a focus region was dimmed");
        Assert.False(IsWhite(result.GetPixel(10, 10)), "the outside was not dimmed");
        Assert.True(result.GetPixel(10, 10).R < 150, "the dim is too weak to draw the eye");
    }

    [Fact]
    public void An_elliptical_focus_region_dims_its_own_corners()
    {
        using var source = Flat(120, 120, Color.White);
        var focus = new FocusMark
        {
            Id = 1,
            Colour = AnnotationPalette.Inks[0].Colour,
            StrokeWidth = 3,
            Rect = new AnnRect(20, 20, 80, 80),
            Mode = FocusMode.Dim,
            Shape = FocusShape.Ellipse,
        };

        using var result = Export(source, [focus]);

        // The centre stays clear...
        Assert.True(IsWhite(result.GetPixel(60, 60)));
        // ...but the corner of the bounding box is outside the ellipse, so it dims too.
        // Four surrounding bands would have left this bright.
        Assert.False(IsWhite(result.GetPixel(24, 24)), "the corner outside the ellipse was left bright");
    }

    [Fact]
    public void Exporting_the_same_document_twice_produces_the_same_bytes()
    {
        using var source = Striped(90, 50);
        var ink = AnnotationPalette.Inks[0].Colour;
        AnnotationMark[] marks =
        [
            Redact(source, new AnnRect(10, 10, 40, 20), RedactStyle.Pixelate, ink),
            Box(ink, filled: true),
        ];

        var first = ExportBytes(source, marks);
        var second = ExportBytes(source, marks);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Exporting_does_not_disturb_the_source_bitmap()
    {
        using var source = Striped(80, 40);
        var before = Snapshot(source);

        using var _ = Export(source, [Redact(source, new AnnRect(5, 5, 60, 30), RedactStyle.Solid, AnnotationPalette.Inks[0].Colour)]);

        // The source is the session's one copy of the original pixels. A redaction that
        // wrote through it would make every later render compound.
        Assert.Equal(before, Snapshot(source));
    }

    // ------------------------------------------------------------------------ helpers

    private static RedactMark Redact(Bitmap source, AnnRect rect, RedactStyle style, AnnColor ink) => new()
    {
        Id = 1,
        Colour = ink,
        StrokeWidth = 4,
        Rect = rect,
        Style = style,
    };

    private static BoxMark Box(AnnColor ink, bool filled) => new()
    {
        Id = 2,
        Colour = ink,
        StrokeWidth = 5,
        Rect = new AnnRect(15, 30, 70, 40),
        Filled = filled,
    };

    private static Bitmap Export(Bitmap source, IReadOnlyList<AnnotationMark> marks)
    {
        var path = Path.Combine(Path.GetTempPath(), $"cp-export-{Guid.NewGuid():N}.png");
        try
        {
            AnnotationExport.Flatten(source, marks, path);
            // Loaded through a copy so the file handle is released and the temp file can
            // be deleted on every platform.
            using var loaded = new Bitmap(path);
            return new Bitmap(loaded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] ExportBytes(Bitmap source, IReadOnlyList<AnnotationMark> marks)
    {
        var path = Path.Combine(Path.GetTempPath(), $"cp-export-{Guid.NewGuid():N}.png");
        try
        {
            AnnotationExport.Flatten(source, marks, path);
            return File.ReadAllBytes(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Hard vertical stripes: the high local contrast that makes text readable.</summary>
    private static Bitmap Striped(int width, int height)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var ink = (x % 4) < 2;
                bitmap.SetPixel(x, y, ink ? Color.FromArgb(255, 20, 20, 20) : Color.FromArgb(255, 235, 235, 235));
            }
        }

        return bitmap;
    }

    private static Bitmap Flat(int width, int height, Color colour)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(colour);
        return bitmap;
    }

    private static double Contrast(Bitmap bitmap, Rectangle area)
    {
        double total = 0;
        var samples = 0;
        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left + 1; x < area.Right; x++)
            {
                total += Math.Abs(bitmap.GetPixel(x, y).G - bitmap.GetPixel(x - 1, y).G);
                samples++;
            }
        }

        return samples == 0 ? 0 : total / samples;
    }

    private static string Snapshot(Bitmap bitmap)
    {
        var builder = new System.Text.StringBuilder();
        for (var y = 0; y < bitmap.Height; y += 3)
        {
            for (var x = 0; x < bitmap.Width; x += 3)
            {
                builder.Append(bitmap.GetPixel(x, y).ToArgb()).Append(';');
            }
        }

        return builder.ToString();
    }

    private static bool IsWhite(Color pixel) => pixel.R > 245 && pixel.G > 245 && pixel.B > 245;

    private static bool IsInked(Color pixel) => !IsWhite(pixel);

    private static bool Close(Color pixel, AnnColor ink) =>
        Math.Abs(pixel.R - ink.R) < 24 && Math.Abs(pixel.G - ink.G) < 24 && Math.Abs(pixel.B - ink.B) < 24;

    private static (byte, byte, byte) Tuple(Color pixel) => (pixel.R, pixel.G, pixel.B);
}
