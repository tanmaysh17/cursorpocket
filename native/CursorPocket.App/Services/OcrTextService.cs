using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using CursorPocket.Core.Annotations;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace CursorPocket_App.Services;

/// <summary>
/// Reads text out of a screenshot using the OCR engine built into Windows.
/// </summary>
/// <remarks>
/// <para>
/// No sidecar, no model download, no network. The reference tool shells out to Tesseract;
/// Windows has shipped an OCR engine since 1809, it reaches us through the same WinRT
/// projections the camera pipeline already uses, and it needs no new package reference.
/// </para>
/// <para>
/// Construction is <c>TryCreate</c> returning null, following the segmentation model's
/// precedent: OCR is a language pack the user may simply not have installed, and a
/// missing language pack must disable one button rather than take down the editor.
/// </para>
/// </remarks>
internal sealed class OcrTextService
{
    private readonly OcrEngine _engine;

    private OcrTextService(OcrEngine engine) => _engine = engine;

    /// <summary>The language the engine will read in, for display beside the result.</summary>
    internal string LanguageTag => _engine.RecognizerLanguage.LanguageTag;

    /// <summary>
    /// Largest side the engine accepts. Read off the engine rather than hardcoded — it is
    /// a property for a reason and has differed between Windows versions.
    /// </summary>
    internal static int MaximumDimension => (int)OcrEngine.MaxImageDimension;

    /// <summary>
    /// The engine for the user's own languages, falling back to English, then to nothing.
    /// Returns null when no recognizer is installed at all.
    /// </summary>
    internal static OcrTextService? TryCreate()
    {
        try
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine is null)
            {
                // The profile languages may all lack a recognizer even though one is
                // present for English.
                engine = OcrEngine.TryCreateFromLanguage(new Language("en-US"));
            }

            return engine is null ? null : new OcrTextService(engine);
        }
        catch (Exception)
        {
            // A broken or absent projection must disable the feature, not crash the
            // editor. Same insurance the ONNX model path buys.
            return null;
        }
    }

    /// <summary>
    /// Recognises the given region of the screenshot. Returns null when the region cannot
    /// be read at all; an empty result means the engine ran and found no text.
    /// </summary>
    internal async Task<OcrReading?> ReadAsync(Bitmap source, Rectangle region)
    {
        if (region.Width <= 0 || region.Height <= 0
            || OcrScaling.CannotBeRead(region.Width, region.Height, MaximumDimension))
        {
            return null;
        }

        var scale = OcrScaling.ScaleFor(region.Width, region.Height, MaximumDimension);
        var (width, height) = OcrScaling.Scaled(region.Width, region.Height, scale);

        using var resampled = Resample(source, region, width, height);
        using var bitmap = ToSoftwareBitmap(resampled);

        var result = await _engine.RecognizeAsync(bitmap);

        var words = new List<OcrWordBox>();
        foreach (var line in result.Lines)
        {
            foreach (var word in line.Words)
            {
                var box = new AnnRect(
                    word.BoundingRect.X,
                    word.BoundingRect.Y,
                    word.BoundingRect.Width,
                    word.BoundingRect.Height);
                // Back through the same factor the image went through, then shifted by
                // where the region started. Skipping either step puts every highlight in
                // the wrong place while the text itself reads correctly.
                words.Add(new OcrWordBox(
                    word.Text,
                    OcrScaling.ToSource(box, scale, new AnnPoint(region.X, region.Y))));
            }
        }

        // Lines joined rather than result.Text, which returns one space-joined run and
        // loses the line structure that makes a recognised error dialog readable.
        var text = string.Join(Environment.NewLine, result.Lines.Select(line => line.Text));
        return new OcrReading(text, words.Count, LanguageTag, words);
    }

    /// <summary>
    /// Copies a region out of the screenshot at the size the engine wants. Bicubic
    /// because this is the one place resampling should preserve glyph shape rather than
    /// block structure.
    /// </summary>
    private static Bitmap Resample(Bitmap source, Rectangle region, int width, int height)
    {
        var target = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(target);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(source, new Rectangle(0, 0, width, height), region, GraphicsUnit.Pixel);
        return target;
    }

    /// <summary>
    /// Wraps a GDI+ bitmap as the BGRA8 SoftwareBitmap the engine takes. Format32bppArgb
    /// is already B,G,R,A in memory on little-endian, so this is a straight copy.
    /// </summary>
    private static SoftwareBitmap ToSoftwareBitmap(Bitmap bitmap)
    {
        var pixels = new byte[bitmap.Width * bitmap.Height * 4];
        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = bitmap.Width * 4;
            for (var y = 0; y < bitmap.Height; y++)
            {
                Marshal.Copy(data.Scan0 + (y * data.Stride), pixels, y * rowBytes, rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        // Ignore rather than Premultiplied: a screenshot is opaque, and declaring
        // premultiplied alpha over data that is not would darken every glyph edge.
        return SoftwareBitmap.CreateCopyFromBuffer(
            pixels.AsBuffer(),
            BitmapPixelFormat.Bgra8,
            bitmap.Width,
            bitmap.Height,
            BitmapAlphaMode.Ignore);
    }
}

/// <summary>What one OCR pass found.</summary>
internal sealed record OcrReading(string Text, int WordCount, string Language, IReadOnlyList<OcrWordBox> Words);

/// <summary>One recognised word and where it sits, in screenshot pixels.</summary>
internal readonly record struct OcrWordBox(string Text, AnnRect Bounds);
