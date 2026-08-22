using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using CursorPocket.Core.Annotations;

namespace CursorPocket_App;

/// <summary>
/// Flattens marks onto the source pixels and writes the PNG.
/// </summary>
/// <remarks>
/// This is the second consumer of <see cref="AnnotationGeometry"/>; the annotation
/// surface is the first. Neither computes a shape of its own, which is the whole point:
/// the previous exporter derived its own geometry and had already drifted from the
/// preview three ways — a pen-width-scaled arrow anchor where the preview drew a
/// triangular cap, no fill where the preview showed one, and 6 px where the preview
/// stroked 5. Every shape below is either a point list from Core or a rectangle the
/// preview also draws from.
/// </remarks>
internal static class AnnotationExport
{
    /// <summary>
    /// The family used for annotation text, on both sides. GDI+ has no SemiBold weight
    /// of its own — FontStyle carries only Regular, Bold, and Italic — so matching the
    /// preview means naming the semibold family outright rather than casting a weight.
    /// </summary>
    internal const string TextFamily = "Segoe UI Semibold";

    /// <summary>Alpha behind a filled box or ellipse. Matches the preview exactly.</summary>
    internal const byte FillAlpha = 64;

    internal static void Flatten(Bitmap source, IReadOnlyList<AnnotationMark> marks, string destination)
    {
        using var bitmap = new Bitmap(source);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        // Pixel units on the surface as well as on each font, so a scratch surface's DPI
        // cannot change a measurement.
        graphics.PageUnit = GraphicsUnit.Pixel;
        // AntiAlias, never AntiAliasGridFit: grid fitting snaps glyph advances to the
        // pixel grid, which makes the same string a different width at a different
        // scale and would break the preview-matches-export property where it shows most.
        graphics.TextRenderingHint = TextRenderingHint.AntiAlias;

        foreach (var mark in marks)
        {
            Draw(graphics, mark);
        }

        bitmap.Save(destination, ImageFormat.Png);
    }

    private static void Draw(Graphics graphics, AnnotationMark mark)
    {
        var colour = ToGdi(mark.Colour);

        switch (mark)
        {
            case ArrowMark arrow:
            {
                var outline = AnnotationGeometry.ArrowOutline(arrow.Start, arrow.End, arrow.StrokeWidth);
                if (outline.Count < 3)
                {
                    return;
                }

                using var brush = new SolidBrush(colour);
                graphics.FillPolygon(brush, ToPoints(outline));
                break;
            }

            case LineMark line:
            {
                using var pen = RoundPen(colour, line.StrokeWidth);
                graphics.DrawLine(pen, (float)line.Start.X, (float)line.Start.Y, (float)line.End.X, (float)line.End.Y);
                break;
            }

            case StrokeMark stroke:
            {
                if (stroke.Points.Count < 2)
                {
                    return;
                }

                using var pen = RoundPen(colour, stroke.StrokeWidth);
                graphics.DrawLines(pen, ToPoints(stroke.Points));
                break;
            }

            case BoxMark box:
            {
                var rect = ToRect(box.Rect);
                if (box.Filled)
                {
                    using var fill = new SolidBrush(ToGdi(box.Colour.WithAlpha(FillAlpha)));
                    graphics.FillRectangle(fill, rect);
                }

                using var pen = RoundPen(colour, box.StrokeWidth);
                graphics.DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);
                break;
            }

            case EllipseMark ellipse:
            {
                var rect = ToRect(ellipse.Rect);
                if (ellipse.Filled)
                {
                    using var fill = new SolidBrush(ToGdi(ellipse.Colour.WithAlpha(FillAlpha)));
                    graphics.FillEllipse(fill, rect);
                }

                using var pen = RoundPen(colour, ellipse.StrokeWidth);
                graphics.DrawEllipse(pen, rect);
                break;
            }

            case TextMark text:
            {
                using var font = new Font(TextFamily, (float)text.FontSize, FontStyle.Regular, GraphicsUnit.Pixel);
                using var brush = new SolidBrush(colour);
                // GenericTypographic for drawing as well as measuring: the default format
                // adds invisible padding around the string, which shows up as text that
                // sits a few pixels off from where the preview put it.
                graphics.DrawString(
                    text.Text,
                    font,
                    brush,
                    new PointF((float)text.Anchor.X, (float)text.Anchor.Y),
                    StringFormat.GenericTypographic);
                break;
            }
        }
    }

    private static Pen RoundPen(Color colour, double width) => new(colour, (float)width)
    {
        StartCap = LineCap.Round,
        EndCap = LineCap.Round,
        LineJoin = LineJoin.Round,
    };

    private static PointF[] ToPoints(IReadOnlyList<AnnPoint> points)
    {
        var result = new PointF[points.Count];
        for (var index = 0; index < points.Count; index++)
        {
            result[index] = new PointF((float)points[index].X, (float)points[index].Y);
        }

        return result;
    }

    private static RectangleF ToRect(AnnRect rect) =>
        new((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);

    private static Color ToGdi(AnnColor colour) => Color.FromArgb(colour.A, colour.R, colour.G, colour.B);
}
