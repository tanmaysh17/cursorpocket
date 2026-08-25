using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using CursorPocket_App.Services;

namespace CursorPocket.Tests;

/// <summary>
/// Drives the real Windows OCR engine against text rendered here, so the assertion is
/// about what actually comes back rather than about what the source code says it will.
/// </summary>
/// <remarks>
/// Every test skips itself when the machine has no OCR recognizer installed. That is the
/// same condition the editor degrades on, and a build agent without a language pack must
/// not fail the suite for it.
/// </remarks>
public sealed class OcrTextServiceTests
{
    [Fact]
    public async Task Windows_reads_back_the_words_that_were_rendered()
    {
        var service = OcrTextService.TryCreate();
        if (service is null)
        {
            return;
        }

        using var image = Render("Invoice total 4213", 720, 160, 56);

        var reading = await service.ReadAsync(image, new Rectangle(0, 0, image.Width, image.Height));

        Assert.NotNull(reading);
        var text = reading!.Text.Replace(" ", string.Empty);
        Assert.Contains("Invoice", reading.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("4213", text, StringComparison.Ordinal);
        Assert.True(reading.WordCount >= 3, $"expected at least 3 words, got {reading.WordCount}");
        Assert.False(string.IsNullOrWhiteSpace(reading.Language));
    }

    [Fact]
    public async Task A_word_box_lands_on_the_word_it_describes()
    {
        var service = OcrTextService.TryCreate();
        if (service is null)
        {
            return;
        }

        using var image = Render("Alpha", 600, 200, 72);

        var reading = await service.ReadAsync(image, new Rectangle(0, 0, image.Width, image.Height));

        Assert.NotNull(reading);
        var word = reading!.Words.FirstOrDefault(w => w.Text.Contains("Alpha", StringComparison.OrdinalIgnoreCase));
        Assert.NotEqual(default, word);

        // The text is drawn from (20, 40). A box that had skipped the coordinate mapping
        // would still be plausible-looking, so this checks it actually covers the ink.
        Assert.True(word.Bounds.Width > 10 && word.Bounds.Height > 10, "the box has no area");
        Assert.True(InkFraction(image, word.Bounds) > 0.02, "the reported box does not sit over any ink");
    }

    [Fact]
    public async Task A_region_reads_only_that_region()
    {
        var service = OcrTextService.TryCreate();
        if (service is null)
        {
            return;
        }

        // "Alpha" on the top half, "Omega" on the bottom.
        using var image = new Bitmap(640, 320, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(image))
        {
            Prepare(graphics);
            graphics.Clear(Color.White);
            using var font = new Font("Segoe UI", 60, FontStyle.Regular, GraphicsUnit.Pixel);
            using var ink = new SolidBrush(Color.Black);
            graphics.DrawString("Alpha", font, ink, new PointF(30, 30));
            graphics.DrawString("Omega", font, ink, new PointF(30, 200));
        }

        var top = await service.ReadAsync(image, new Rectangle(0, 0, 640, 140));

        Assert.NotNull(top);
        Assert.Contains("Alpha", top!.Text, StringComparison.OrdinalIgnoreCase);
        // Reading a region must not quietly read the whole image.
        Assert.DoesNotContain("Omega", top.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_word_box_from_a_region_is_offset_by_where_that_region_started()
    {
        var service = OcrTextService.TryCreate();
        if (service is null)
        {
            return;
        }

        using var image = new Bitmap(640, 400, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(image))
        {
            Prepare(graphics);
            graphics.Clear(Color.White);
            using var font = new Font("Segoe UI", 60, FontStyle.Regular, GraphicsUnit.Pixel);
            using var ink = new SolidBrush(Color.Black);
            graphics.DrawString("Delta", font, ink, new PointF(40, 250));
        }

        var region = new Rectangle(0, 220, 640, 160);
        var reading = await service.ReadAsync(image, region);

        Assert.NotNull(reading);
        var word = reading!.Words.FirstOrDefault();
        Assert.NotEqual(default, word);
        // Reported in screenshot coordinates, not region coordinates. Without the origin
        // shift this would come back near y=30 and the highlight would sit at the top of
        // the image while the text read correctly — a units bug that looks like a
        // rendering bug.
        Assert.True(word.Bounds.Y > 200, $"box y {word.Bounds.Y:F0} was not shifted into source coordinates");
    }

    [Fact]
    public async Task A_blank_image_reads_as_no_text_rather_than_failing()
    {
        var service = OcrTextService.TryCreate();
        if (service is null)
        {
            return;
        }

        using var image = new Bitmap(300, 200, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(image))
        {
            graphics.Clear(Color.White);
        }

        var reading = await service.ReadAsync(image, new Rectangle(0, 0, 300, 200));

        // An empty result means the engine ran and found nothing, which is different from
        // the engine refusing the region.
        Assert.NotNull(reading);
        Assert.Equal(0, reading!.WordCount);
    }

    [Fact]
    public async Task A_region_that_cannot_be_read_is_refused_rather_than_throwing()
    {
        var service = OcrTextService.TryCreate();
        if (service is null)
        {
            return;
        }

        using var image = Render("x", 200, 200, 40);

        Assert.Null(await service.ReadAsync(image, new Rectangle(0, 0, 0, 0)));
        Assert.Null(await service.ReadAsync(image, new Rectangle(0, 0, -5, 20)));
    }

    [Fact]
    public async Task A_short_wide_strip_reports_nothing_found_rather_than_failing()
    {
        var service = OcrTextService.TryCreate();
        if (service is null)
        {
            return;
        }

        // Windows OCR wants document-like input and will not read a very short, very wide
        // single line. Measured, not assumed: an 1882x160 strip of 103 px text comes back
        // empty whether it was upscaled from 400x34 or drawn at that size to begin with,
        // so this is the engine's limit and no resampling on our side works around it.
        // What matters is that the editor treats it as "nothing recognised here" rather
        // than as an error, so the caller must still get a result object.
        using var image = Render("Total", 400, 34, 22);

        var reading = await service.ReadAsync(image, new Rectangle(0, 0, 400, 34));

        Assert.NotNull(reading);
        Assert.NotNull(reading!.Text);
    }

    [Fact]
    public void The_engine_reports_a_sane_maximum_dimension()
    {
        if (OcrTextService.TryCreate() is null)
        {
            return;
        }

        // Read off the engine rather than hardcoded, and large enough for a real capture.
        Assert.True(OcrTextService.MaximumDimension >= 1024, $"maximum of {OcrTextService.MaximumDimension} is implausible");
    }

    private static Bitmap Render(string text, int width, int height, float fontSize)
    {
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        Prepare(graphics);
        graphics.Clear(Color.White);
        using var font = new Font("Segoe UI", fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        using var ink = new SolidBrush(Color.Black);
        graphics.DrawString(text, font, ink, new PointF(20, Math.Max(2, (height - (fontSize * 1.4f)) / 2)));
        return bitmap;
    }

    private static void Prepare(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }

    /// <summary>How much of a reported box is actually covered by dark pixels.</summary>
    private static double InkFraction(Bitmap image, CursorPocket.Core.Annotations.AnnRect box)
    {
        var left = Math.Max(0, (int)box.X);
        var top = Math.Max(0, (int)box.Y);
        var right = Math.Min(image.Width, (int)Math.Ceiling(box.Right));
        var bottom = Math.Min(image.Height, (int)Math.Ceiling(box.Bottom));
        if (right <= left || bottom <= top)
        {
            return 0;
        }

        var dark = 0;
        var total = 0;
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                if (image.GetPixel(x, y).R < 128)
                {
                    dark++;
                }

                total++;
            }
        }

        return total == 0 ? 0 : (double)dark / total;
    }
}
