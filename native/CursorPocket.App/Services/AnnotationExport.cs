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
/// This is the second consumer of <see cref="AnnotationGeometry"/> and
/// <see cref="AnnotationPatches"/>; the annotation surface is the first. Neither computes
/// a shape or samples a pixel of its own, which is the whole point: the previous exporter
/// derived its own geometry and had already drifted from the preview three ways — a
/// pen-width-scaled arrow anchor where the preview drew a triangular cap, no fill where
/// the preview showed one, and 6 px where the preview stroked 5.
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

    /// <summary>How dark the world outside a focus region goes.</summary>
    internal const byte DimAlpha = 150;

    /// <summary>Pill padding as a fraction of the text size, on each side.</summary>
    internal const double PillPaddingFactor = 0.34;

    internal static void Flatten(Bitmap source, IReadOnlyList<AnnotationMark> marks, string destination) =>
        Flatten(source, marks, null, destination);

    /// <summary>
    /// Composites the marks over the screenshot and writes the PNG, applying the crop,
    /// cuts, and backdrop if there are any.
    /// </summary>
    /// <remarks>
    /// Marks are composited first, in source coordinates, and the transform is then a pure
    /// blit of the result. That is deliberately simpler than mapping each mark's geometry
    /// through the transform, and it is also the more defensible behaviour: cutting a strip
    /// out cuts the annotated image, exactly like cutting a printed page. A box straddling
    /// a seam loses its middle and closes up, rather than staying tall and spanning a join.
    /// </remarks>
    internal static void Flatten(
        Bitmap source,
        IReadOnlyList<AnnotationMark> marks,
        DocumentTransform? transform,
        string destination)
    {
        using var composited = new Bitmap(source);
        using (var graphics = Graphics.FromImage(composited))
        {
            Configure(graphics);
            foreach (var mark in marks)
            {
                Draw(graphics, source, mark);
            }
        }

        if (transform is null || transform.IsIdentity)
        {
            composited.Save(destination, ImageFormat.Png);
            return;
        }

        using var output = new Bitmap(transform.OutputWidth, transform.OutputHeight, PixelFormat.Format32bppArgb);
        using var target = Graphics.FromImage(output);
        Configure(target);

        if (transform.Backdrop.IsEnabled)
        {
            DrawBackdrop(target, transform);
        }

        var content = new RectangleF(
            transform.Backdrop.IsEnabled ? (float)transform.Backdrop.Padding : 0,
            transform.Backdrop.IsEnabled ? (float)transform.Backdrop.Padding : 0,
            transform.ContentWidth,
            transform.ContentHeight);

        // Rounding the screenshot's own corners is what makes a backdrop read as a
        // presentation frame rather than as an accidental margin.
        var saved = target.Save();
        if (transform.Backdrop is { IsEnabled: true, CornerRadius: > 0 })
        {
            using var rounded = new GraphicsPath();
            AddRoundedRect(rounded, content, (float)transform.Backdrop.CornerRadius);
            target.SetClip(rounded);
        }

        foreach (var slab in transform.Slabs())
        {
            target.DrawImage(
                composited,
                new RectangleF((float)slab.Output.X, (float)slab.Output.Y, (float)slab.Output.Width, (float)slab.Output.Height),
                new RectangleF((float)slab.Source.X, (float)slab.Source.Y, (float)slab.Source.Width, (float)slab.Source.Height),
                GraphicsUnit.Pixel);
        }

        target.Restore(saved);
        output.Save(destination, ImageFormat.Png);
    }

    /// <summary>
    /// Fills the backdrop and lays a soft shadow under the image. The shadow is a stack of
    /// rounded rectangles rather than a real blur, which is cheap and, at this size,
    /// indistinguishable.
    /// </summary>
    private static void DrawBackdrop(Graphics graphics, DocumentTransform transform)
    {
        var backdrop = transform.Backdrop;
        var whole = new RectangleF(0, 0, transform.OutputWidth, transform.OutputHeight);

        using (var fill = new SolidBrush(ToGdi(backdrop.Fill)))
        {
            graphics.FillRectangle(fill, whole);
        }

        if (backdrop.ShadowBlur <= 0 || backdrop.ShadowOpacity <= 0)
        {
            return;
        }

        var content = new RectangleF(
            (float)backdrop.Padding,
            (float)backdrop.Padding,
            transform.ContentWidth,
            transform.ContentHeight);

        const int layers = 8;
        for (var layer = layers; layer >= 1; layer--)
        {
            var spread = (float)(backdrop.ShadowBlur * layer / layers);
            var alpha = (int)Math.Round(backdrop.ShadowOpacity * 255 / layers / 1.6);
            if (alpha <= 0)
            {
                continue;
            }

            var rect = RectangleF.Inflate(content, spread, spread);
            // Offset downward, so the image reads as lifted off the backdrop rather than
            // floating in the middle of a halo.
            rect.Offset(0, spread * 0.35f);
            using var path = new GraphicsPath();
            AddRoundedRect(path, rect, (float)(backdrop.CornerRadius + spread));
            using var brush = new SolidBrush(Color.FromArgb(Math.Clamp(alpha, 1, 255), 0, 0, 0));
            graphics.FillPath(brush, path);
        }
    }

    private static void Configure(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        // Pixel units on the surface as well as on each font, so a scratch surface's DPI
        // cannot change a measurement.
        graphics.PageUnit = GraphicsUnit.Pixel;
        // AntiAlias, never AntiAliasGridFit: grid fitting snaps glyph advances to the
        // pixel grid, which makes the same string a different width at a different
        // scale and would break the preview-matches-export property where it shows most.
        graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
    }

    /// <summary>
    /// Measures annotation text. The preview sizes its readability pill from this too, so
    /// the pill is the same rectangle on screen and in the file rather than two
    /// independent guesses from two text stacks that measure differently.
    /// </summary>
    internal static SizeF MeasureText(string text, double fontSize)
    {
        using var scratch = new Bitmap(1, 1, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(scratch);
        Configure(graphics);
        using var font = new Font(TextFamily, (float)fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        return graphics.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
    }

    /// <summary>Corner radius for a rounded focus region or a text pill.</summary>
    internal static double CornerRadiusFor(double width, double height) =>
        Math.Clamp(Math.Min(width, height) * 0.16, 4, 48);

    private static void Draw(Graphics graphics, Bitmap source, AnnotationMark mark)
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

            case StrokeMark { Highlight: true } highlight:
                DrawHighlight(graphics, highlight);
                break;

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
                // One path for both fill and stroke, so a rounded box rounds identically
                // whichever it is drawn with — and so Alt+wheel's radius reaches the file.
                using var outline = new GraphicsPath();
                if (box.CornerRadius > 0)
                {
                    AddRoundedRect(outline, rect, (float)box.CornerRadius);
                }
                else
                {
                    outline.AddRectangle(rect);
                }

                if (box.Filled)
                {
                    using var fill = new SolidBrush(ToGdi(box.Colour.WithAlpha(FillAlpha)));
                    graphics.FillPath(fill, outline);
                }

                using var pen = RoundPen(colour, box.StrokeWidth);
                graphics.DrawPath(pen, outline);
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

            case MarkerMark marker:
                DrawMarker(graphics, marker);
                break;

            case RedactMark redact:
            {
                var patch = AnnotationPatches.Redact(source, redact);
                if (patch.IsEmpty)
                {
                    return;
                }

                var target = AnnotationPatches.Snap(redact.Rect, source.Width, source.Height);
                using var image = AnnotationPatches.ToBitmap(patch);
                // NearestNeighbor and no smoothing: the patch is already exactly the size
                // of its target, and any resampling here would soften the very block
                // edges the redaction depends on.
                var previous = graphics.InterpolationMode;
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.DrawImage(image, target);
                graphics.InterpolationMode = previous;
                break;
            }

            case FocusMark focus:
                DrawFocus(graphics, source, focus);
                break;

            case TextMark text:
                DrawText(graphics, text);
                break;
        }
    }

    /// <summary>
    /// A highlighter stroke is drawn opaque into its own layer and composited once at its
    /// alpha. Stroking a translucent pen directly makes a single stroke darken itself
    /// everywhere it crosses over, which is what a highlighter never does on paper.
    /// </summary>
    private static void DrawHighlight(Graphics graphics, StrokeMark stroke)
    {
        if (stroke.Points.Count < 2)
        {
            return;
        }

        var points = ToPoints(stroke.Points);
        var pad = (float)stroke.StrokeWidth + 2;
        var minX = points.Min(p => p.X) - pad;
        var minY = points.Min(p => p.Y) - pad;
        var maxX = points.Max(p => p.X) + pad;
        var maxY = points.Max(p => p.Y) + pad;

        // Only the stroke's own bounds, not the whole image: a full-size scratch per
        // highlighter stroke would be 33 MB on a 4K capture.
        var width = (int)Math.Ceiling(maxX - minX);
        var height = (int)Math.Ceiling(maxY - minY);
        if (width <= 0 || height <= 0)
        {
            return;
        }

        using var layer = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var layerGraphics = Graphics.FromImage(layer))
        {
            layerGraphics.SmoothingMode = SmoothingMode.AntiAlias;
            layerGraphics.TranslateTransform(-minX, -minY);
            using var pen = RoundPen(ToGdi(stroke.Colour.WithAlpha(255)), stroke.StrokeWidth);
            layerGraphics.DrawLines(pen, points);
        }

        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = stroke.Colour.A / 255f });
        graphics.DrawImage(
            layer,
            new Rectangle((int)Math.Floor(minX), (int)Math.Floor(minY), width, height),
            0,
            0,
            width,
            height,
            GraphicsUnit.Pixel,
            attributes);
    }

    private static void DrawMarker(Graphics graphics, MarkerMark marker)
    {
        var radius = (float)marker.Radius;
        var bounds = new RectangleF(
            (float)(marker.Center.X - radius),
            (float)(marker.Center.Y - radius),
            radius * 2,
            radius * 2);

        using var fill = new SolidBrush(ToGdi(marker.Colour));
        graphics.FillEllipse(fill, bounds);

        var label = marker.Number.ToString();
        // Sized so a two-digit marker still fits inside its disc.
        var fontSize = radius * (label.Length > 1 ? 1.05f : 1.3f);
        using var font = new Font(TextFamily, fontSize, FontStyle.Regular, GraphicsUnit.Pixel);
        using var text = new SolidBrush(ToGdi(AnnotationPalette.OnInk(marker.Colour)));
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        graphics.DrawString(label, font, text, bounds, format);
    }

    private static void DrawFocus(Graphics graphics, Bitmap source, FocusMark focus)
    {
        var rect = ToRect(focus.Rect);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        if (focus.Mode == FocusMode.Loupe)
        {
            var patch = AnnotationPatches.Loupe(source, focus);
            if (patch.IsEmpty)
            {
                return;
            }

            var target = AnnotationPatches.Snap(focus.Rect, source.Width, source.Height);
            using var image = AnnotationPatches.ToBitmap(patch);
            using var clip = ShapePath(rect, focus.Shape);
            var saved = graphics.Save();
            graphics.SetClip(clip);
            graphics.DrawImage(image, target);
            graphics.Restore(saved);

            using var ring = RoundPen(ToGdi(focus.Colour), focus.StrokeWidth);
            graphics.DrawPath(ring, clip);
            return;
        }

        // Dim everything outside the shape: one path holding the whole image and the
        // shape, filled with the alternate rule so the shape punches a hole.
        using var mask = new GraphicsPath { FillMode = FillMode.Alternate };
        mask.AddRectangle(new RectangleF(0, 0, source.Width, source.Height));
        using (var inner = ShapePath(rect, focus.Shape))
        {
            mask.AddPath(inner, connect: false);
        }

        using var dim = new SolidBrush(Color.FromArgb(DimAlpha, 0, 0, 0));
        graphics.FillPath(dim, mask);
    }

    private static void DrawText(Graphics graphics, TextMark text)
    {
        var size = MeasureText(text.Text, text.FontSize);
        var pad = (float)(text.FontSize * PillPaddingFactor);
        var origin = new PointF((float)text.Anchor.X, (float)text.Anchor.Y);

        using var font = new Font(TextFamily, (float)text.FontSize, FontStyle.Regular, GraphicsUnit.Pixel);

        if (text.Pill)
        {
            var pill = new RectangleF(origin.X, origin.Y, size.Width + (pad * 2), size.Height + pad);
            using var background = new GraphicsPath();
            AddRoundedRect(background, pill, (float)CornerRadiusFor(pill.Width, pill.Height));
            using var fill = new SolidBrush(Color.FromArgb(240, 242, 247, 244));
            graphics.FillPath(fill, background);
            using var ink = new SolidBrush(Color.FromArgb(255, 11, 16, 15));
            graphics.DrawString(
                text.Text,
                font,
                ink,
                new PointF(origin.X + pad, origin.Y + (pad / 2)),
                StringFormat.GenericTypographic);
            return;
        }

        // No pill, so the glyphs need their own separation from whatever is underneath.
        using var outline = new GraphicsPath();
        outline.AddString(
            text.Text,
            new FontFamily(TextFamily),
            (int)FontStyle.Regular,
            (float)text.FontSize,
            new PointF(origin.X + pad, origin.Y + (pad / 2)),
            StringFormat.GenericTypographic);
        using var halo = new Pen(Color.FromArgb(140, 0, 0, 0), (float)Math.Max(2, text.FontSize * 0.09))
        {
            LineJoin = LineJoin.Round,
        };
        graphics.DrawPath(halo, outline);
        using var glyphs = new SolidBrush(ToGdi(text.Colour));
        graphics.FillPath(glyphs, outline);
    }

    private static GraphicsPath ShapePath(RectangleF rect, FocusShape shape)
    {
        var path = new GraphicsPath();
        switch (shape)
        {
            case FocusShape.Ellipse:
                path.AddEllipse(rect);
                break;
            case FocusShape.Rounded:
                AddRoundedRect(path, rect, (float)CornerRadiusFor(rect.Width, rect.Height));
                break;
            default:
                path.AddRectangle(rect);
                break;
        }

        return path;
    }

    private static void AddRoundedRect(GraphicsPath path, RectangleF rect, float radius)
    {
        var r = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2);
        if (r <= 0)
        {
            path.AddRectangle(rect);
            return;
        }

        var d = r * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
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
