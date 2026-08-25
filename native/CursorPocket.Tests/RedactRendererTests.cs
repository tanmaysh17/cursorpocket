using CursorPocket.Core.Annotations;
using CursorPocket.Core.Media;

namespace CursorPocket.Tests;

public sealed class RedactRendererTests
{
    [Fact]
    public void Solid_replaces_every_pixel_so_nothing_is_recoverable()
    {
        var patch = TextLikePatch(64, 24);

        RedactRenderer.Apply(patch, 64, 24, RedactStyle.Solid, new AnnColor(255, 11, 16, 15));

        // Not "mostly" replaced — every pixel, or the redaction is theatre.
        for (var index = 0; index < patch.Length; index += 4)
        {
            Assert.Equal(15, patch[index]);      // B
            Assert.Equal(16, patch[index + 1]); // G
            Assert.Equal(11, patch[index + 2]); // R
            Assert.Equal(255, patch[index + 3]);
        }
    }

    [Fact]
    public void Solid_is_always_opaque_even_if_asked_for_a_translucent_colour()
    {
        var patch = TextLikePatch(16, 16);

        RedactRenderer.Apply(patch, 16, 16, RedactStyle.Solid, new AnnColor(40, 200, 30, 30));

        // A translucent redaction is not a redaction.
        for (var index = 3; index < patch.Length; index += 4)
        {
            Assert.Equal(255, patch[index]);
        }
    }

    [Theory]
    [InlineData(RedactStyle.Pixelate)]
    [InlineData(RedactStyle.Blur)]
    public void Every_style_destroys_the_local_contrast_that_makes_text_readable(RedactStyle style)
    {
        var patch = TextLikePatch(96, 32);
        var before = LocalContrast(patch, 96, 32);

        RedactRenderer.Apply(patch, 96, 32, style, new AnnColor(255, 0, 0, 0));

        var after = LocalContrast(patch, 96, 32);
        Assert.True(after < before / 3, $"contrast went {before:F1} -> {after:F1}, not enough to obscure text");
    }

    [Theory]
    [InlineData(RedactStyle.Solid)]
    [InlineData(RedactStyle.Pixelate)]
    [InlineData(RedactStyle.Blur)]
    public void Two_renders_of_the_same_patch_are_byte_identical(RedactStyle style)
    {
        var first = TextLikePatch(48, 20);
        var second = TextLikePatch(48, 20);

        RedactRenderer.Apply(first, 48, 20, style, new AnnColor(255, 9, 9, 9));
        RedactRenderer.Apply(second, 48, 20, style, new AnnColor(255, 9, 9, 9));

        // Determinism is what lets the on-screen patch and the exported patch agree, and
        // it is why nothing here uses System.Random — its sequence is not guaranteed
        // stable across .NET versions, so a framework bump would change old output.
        Assert.Equal(first, second);
    }

    [Fact]
    public void A_patch_smaller_than_one_block_is_still_flattened()
    {
        // Two pixels across cannot hold a block grid. The old failure mode here would be
        // to leave the patch untouched, which silently un-redacts a short redaction.
        var patch = TextLikePatch(3, 3);

        RedactRenderer.Apply(patch, 3, 3, RedactStyle.Pixelate, new AnnColor(255, 0, 0, 0));

        var first = (patch[0], patch[1], patch[2]);
        for (var index = 0; index < patch.Length; index += 4)
        {
            Assert.Equal(first, (patch[index], patch[index + 1], patch[index + 2]));
        }
    }

    [Fact]
    public void An_empty_patch_is_ignored_rather_than_throwing()
    {
        var patch = Array.Empty<byte>();

        RedactRenderer.Apply(patch, 0, 0, RedactStyle.Solid, new AnnColor(255, 0, 0, 0));
        RedactRenderer.Apply(patch, -4, 10, RedactStyle.Pixelate, new AnnColor(255, 0, 0, 0));
    }

    [Fact]
    public void Blocks_grow_with_the_patch_so_a_small_redaction_is_not_barely_touched()
    {
        var small = RedactRenderer.BlockSizeFor(40, 40);
        var large = RedactRenderer.BlockSizeFor(800, 400);

        Assert.True(small >= RedactRenderer.MinimumBlockSize);
        Assert.True(large > small);
    }

    /// <summary>
    /// A patch with hard vertical strokes on a light ground — the same high local
    /// contrast that makes text readable, and the thing a redaction has to remove.
    /// </summary>
    private static byte[] TextLikePatch(int width, int height)
    {
        var patch = new byte[Math.Max(0, width * height * 4)];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var ink = (x % 4) < 2 && y > 2 && y < height - 2;
                var value = (byte)(ink ? 20 : 235);
                var index = ((y * width) + x) * 4;
                patch[index] = value;
                patch[index + 1] = value;
                patch[index + 2] = value;
                patch[index + 3] = 255;
            }
        }

        return patch;
    }

    /// <summary>Mean absolute difference between horizontally adjacent pixels.</summary>
    private static double LocalContrast(ReadOnlySpan<byte> patch, int width, int height)
    {
        double total = 0;
        var samples = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 1; x < width; x++)
            {
                var here = patch[(((y * width) + x) * 4) + 1];
                var left = patch[(((y * width) + x - 1) * 4) + 1];
                total += Math.Abs(here - left);
                samples++;
            }
        }

        return samples == 0 ? 0 : total / samples;
    }
}

public sealed class MarkerNumberingTests
{
    [Fact]
    public void The_first_marker_is_one()
    {
        Assert.Equal(1, MarkerNumbering.Next([]));
    }

    [Fact]
    public void Numbering_counts_only_markers_and_ignores_other_marks()
    {
        List<AnnotationMark> marks = [Line(1), Marker(2, 1), Line(3), Marker(4, 2)];

        Assert.Equal(3, MarkerNumbering.Next(marks));
    }

    [Fact]
    public void A_number_is_reused_after_the_marker_holding_it_is_undone()
    {
        List<AnnotationMark> marks = [Marker(1, 1), Marker(2, 2)];
        Assert.Equal(3, MarkerNumbering.Next(marks));

        // Undo removes the mark from view, so the next marker is 2 again. Deriving the
        // number rather than keeping a counter is what makes this work with no rewind.
        marks.RemoveAt(1);
        Assert.Equal(2, MarkerNumbering.Next(marks));
    }

    [Fact]
    public void Deleting_a_middle_marker_leaves_a_gap_rather_than_renumbering()
    {
        List<AnnotationMark> marks = [Marker(1, 1), Marker(2, 2), Marker(3, 3)];
        marks.RemoveAt(1);

        // Renumbering would silently invalidate a text mark that says "see 2".
        Assert.Equal(4, MarkerNumbering.Next(marks));
        Assert.Equal([1, 3], marks.OfType<MarkerMark>().Select(m => m.Number));
    }

    [Fact]
    public void Marker_radius_grows_with_the_image_and_stays_within_bounds()
    {
        var tiny = MarkerNumbering.RadiusFor(120, 90, AnnotationSizeStep.Medium);
        var large = MarkerNumbering.RadiusFor(3840, 2160, AnnotationSizeStep.Medium);

        Assert.True(tiny >= 12, "a marker on a small capture still has to be legible");
        Assert.True(large <= 72, "a marker on a 4K capture must not become a blot");
        Assert.True(large > tiny);
    }

    private static LineMark Line(int id) => new()
    {
        Id = id,
        Colour = AnnotationPalette.Default.Colour,
        StrokeWidth = 4,
        Start = new AnnPoint(0, 0),
        End = new AnnPoint(1, 1),
    };

    private static MarkerMark Marker(int id, int number) => new()
    {
        Id = id,
        Colour = AnnotationPalette.Default.Colour,
        StrokeWidth = 4,
        Center = new AnnPoint(number * 10, 10),
        Number = number,
        Radius = 20,
    };
}

public sealed class ConicWheelTests
{
    [Fact]
    public void The_wheel_is_a_disc_with_transparent_corners()
    {
        const int size = 32;
        var pixels = ConicWheel.Render(size);

        Assert.Equal(size * size * 4, pixels.Length);
        // A corner is outside the disc.
        Assert.Equal(0, pixels[3]);
        // The centre is inside it.
        var middle = (((size / 2) * size) + (size / 2)) * 4;
        Assert.Equal(255, pixels[middle + 3]);
    }

    [Fact]
    public void Opposite_sides_of_the_wheel_are_different_hues()
    {
        const int size = 48;
        var pixels = ConicWheel.Render(size);
        var y = size / 2;
        var left = (((y * size) + 3) * 4);
        var right = (((y * size) + size - 4) * 4);

        Assert.NotEqual(
            (pixels[left], pixels[left + 1], pixels[left + 2]),
            (pixels[right], pixels[right + 1], pixels[right + 2]));
    }

    [Fact]
    public void The_wheel_renders_at_swatch_size_without_throwing()
    {
        Assert.Equal(26 * 26 * 4, ConicWheel.Render(26).Length);
        Assert.Empty(ConicWheel.Render(0));
    }
}

public sealed class UpscaleNearestTests
{
    [Fact]
    public void Nearest_upscale_holds_each_block_flat_instead_of_smearing_it()
    {
        // Two source pixels, black then white, blown up to eight across.
        var source = new byte[] { 0, 0, 0, 255, 255, 255, 255, 255 };
        var destination = new byte[8 * 4];

        PixelResizer.UpscaleNearest(source, 2, 1, destination, 8, 1);

        // Bilinear would produce a ramp through the middle. Nearest must not: a soft edge
        // is a blur, and a blur is not what pixelation is for.
        for (var x = 0; x < 4; x++)
        {
            Assert.Equal(0, destination[(x * 4) + 1]);
        }

        for (var x = 4; x < 8; x++)
        {
            Assert.Equal(255, destination[(x * 4) + 1]);
        }
    }

    [Fact]
    public void Nearest_upscale_always_writes_opaque_pixels()
    {
        var source = new byte[] { 10, 20, 30, 0 };
        var destination = new byte[4 * 4];

        PixelResizer.UpscaleNearest(source, 1, 1, destination, 2, 2);

        for (var index = 3; index < destination.Length; index += 4)
        {
            Assert.Equal(255, destination[index]);
        }
    }
}
