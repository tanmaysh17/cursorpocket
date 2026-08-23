using System.Drawing;
using System.Drawing.Imaging;
using CursorPocket.Core.Annotations;
using CursorPocket.Core.Media;

namespace CursorPocket_App;

/// <summary>
/// Produces the pixel patches for the marks that read the screenshot rather than draw
/// over it — redaction and the loupe.
/// </summary>
/// <remarks>
/// <para>
/// Both the live preview and the exporter call these, so the patch on screen and the
/// patch in the file are the same bytes. That is the same discipline
/// <see cref="AnnotationGeometry"/> enforces for shapes, applied to pixels.
/// </para>
/// <para>
/// Patches are always sampled from the untouched source at full resolution, never from
/// whatever has already been composited. Two consequences worth keeping: a redaction
/// cannot be weakened by a mark drawn underneath it, and re-rendering is idempotent, so
/// pixelating twice does not pixelate the pixelation.
/// </para>
/// </remarks>
internal static class AnnotationPatches
{
    internal readonly record struct Patch(byte[] Pixels, int Width, int Height)
    {
        internal bool IsEmpty => Width <= 0 || Height <= 0;
    }

    /// <summary>The source rect, obliterated according to the mark's style.</summary>
    internal static Patch Redact(Bitmap source, RedactMark mark)
    {
        var rect = Snap(mark.Rect, source.Width, source.Height);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return default;
        }

        var pixels = Read(source, rect);
        RedactRenderer.Apply(pixels, rect.Width, rect.Height, mark.Style, mark.Colour);
        return new Patch(pixels, rect.Width, rect.Height);
    }

    /// <summary>
    /// The region under a loupe, magnified. A loupe shows the middle of its own frame
    /// enlarged, so the sample is the frame deflated by the magnification about its
    /// centre — the same pixels a magnifier held over that spot would show.
    /// </summary>
    internal static Patch Loupe(Bitmap source, FocusMark mark)
    {
        var frame = Snap(mark.Rect, source.Width, source.Height);
        if (frame.Width <= 0 || frame.Height <= 0)
        {
            return default;
        }

        var magnification = Math.Max(1.1, mark.Magnification);
        var sampleWidth = Math.Max(1, (int)Math.Round(frame.Width / magnification));
        var sampleHeight = Math.Max(1, (int)Math.Round(frame.Height / magnification));
        var sample = Snap(
            new AnnRect(
                frame.X + ((frame.Width - sampleWidth) / 2d),
                frame.Y + ((frame.Height - sampleHeight) / 2d),
                sampleWidth,
                sampleHeight),
            source.Width,
            source.Height);

        if (sample.Width <= 0 || sample.Height <= 0)
        {
            return default;
        }

        var small = Read(source, sample);
        var enlarged = new byte[frame.Width * frame.Height * 4];
        // Bilinear here, unlike pixelation: a loupe exists to make detail readable, and
        // nearest-neighbour would show the reader square blocks instead of detail.
        PixelResizer.UpscaleBilinear(small, sample.Width, sample.Height, enlarged, frame.Width, frame.Height);
        return new Patch(enlarged, frame.Width, frame.Height);
    }

    /// <summary>
    /// Rounds a rect to whole source pixels and clips it to the image. Rounding once,
    /// here, is what keeps a patch's block grid anchored: if the preview and the export
    /// rounded differently the two would disagree by a pixel at the edges.
    /// </summary>
    internal static Rectangle Snap(AnnRect rect, int imageWidth, int imageHeight)
    {
        var left = (int)Math.Round(rect.X);
        var top = (int)Math.Round(rect.Y);
        var right = (int)Math.Round(rect.Right);
        var bottom = (int)Math.Round(rect.Bottom);

        left = Math.Clamp(left, 0, imageWidth);
        top = Math.Clamp(top, 0, imageHeight);
        right = Math.Clamp(right, 0, imageWidth);
        bottom = Math.Clamp(bottom, 0, imageHeight);

        return new Rectangle(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    /// <summary>Copies a rect out of a bitmap as tightly packed BGRA.</summary>
    private static byte[] Read(Bitmap source, Rectangle rect)
    {
        var pixels = new byte[rect.Width * rect.Height * 4];
        // Format32bppArgb is B,G,R,A in memory on little-endian, which is exactly the
        // packed layout CursorPocket.Core.Media works in.
        var data = source.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = rect.Width * 4;
            for (var y = 0; y < rect.Height; y++)
            {
                var row = data.Scan0 + (y * data.Stride);
                System.Runtime.InteropServices.Marshal.Copy(row, pixels, y * rowBytes, rowBytes);
            }
        }
        finally
        {
            source.UnlockBits(data);
        }

        return pixels;
    }

    /// <summary>Wraps a packed BGRA patch as a bitmap the exporter can blit.</summary>
    internal static Bitmap ToBitmap(Patch patch)
    {
        var bitmap = new Bitmap(patch.Width, patch.Height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, patch.Width, patch.Height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = patch.Width * 4;
            for (var y = 0; y < patch.Height; y++)
            {
                var row = data.Scan0 + (y * data.Stride);
                System.Runtime.InteropServices.Marshal.Copy(patch.Pixels, y * rowBytes, row, rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }
}
