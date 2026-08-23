namespace CursorPocket.Core.Annotations;

/// <summary>A horizontal strip of the image to remove, in source pixels.</summary>
public readonly record struct CutBand(double Offset, double Length)
{
    public double End => Offset + Length;
}

/// <summary>
/// Padding, fill, and shadow placed around the exported image.
/// </summary>
public sealed record BackdropSettings(
    double Padding,
    double CornerRadius,
    AnnColor Fill,
    double ShadowBlur,
    double ShadowOpacity)
{
    public static BackdropSettings None { get; } = new(0, 0, new AnnColor(0, 0, 0, 0), 0, 0);

    public bool IsEnabled => Padding > 0;
}

/// <summary>One run of surviving source rows and where it lands in the output.</summary>
public readonly record struct Slab(AnnRect Source, AnnRect Output);

/// <summary>
/// Maps between screenshot pixels and exported pixels once the image has been cropped,
/// had strips cut out of it, or been placed on a backdrop.
/// </summary>
/// <remarks>
/// <para>
/// Composition order is fixed and matters: <b>crop, then cuts, then backdrop padding</b>.
/// Crop and cuts are both stored in <i>source</i> coordinates, which is what makes them
/// independently undoable — expressing cuts in crop-space would make each depend on the
/// other's history.
/// </para>
/// <para>
/// Cuts remove horizontal strips and collapse the gap vertically. That is the long-list
/// and long-log case, and keeping it to one axis keeps the map one-dimensional; supporting
/// both axes at once would turn the slab list into a 2-D grid for very little gain.
/// </para>
/// <para>
/// Marks are never rewritten. They stay in source coordinates and are mapped through this
/// on the way out, so undoing a cut restores every mark's original position rather than
/// leaving it at a position derived from the cut.
/// </para>
/// </remarks>
public sealed class DocumentTransform
{
    private readonly AnnRect _crop;
    private readonly List<CutBand> _bands;
    private readonly double _padding;

    private DocumentTransform(AnnRect crop, List<CutBand> bands, BackdropSettings backdrop)
    {
        _crop = crop;
        _bands = bands;
        _padding = backdrop.IsEnabled ? backdrop.Padding : 0;
        Backdrop = backdrop;

        var removed = bands.Sum(band => band.Length);
        ContentWidth = Math.Max(1, (int)Math.Round(crop.Width));
        ContentHeight = Math.Max(1, (int)Math.Round(crop.Height - removed));
        OutputWidth = ContentWidth + (int)Math.Round(_padding * 2);
        OutputHeight = ContentHeight + (int)Math.Round(_padding * 2);
    }

    public BackdropSettings Backdrop { get; }

    /// <summary>The image itself, without the backdrop's padding.</summary>
    public int ContentWidth { get; }

    public int ContentHeight { get; }

    /// <summary>The exported image, including the backdrop's padding.</summary>
    public int OutputWidth { get; }

    public int OutputHeight { get; }

    /// <summary>True when nothing has changed the geometry, so the export is a plain overlay.</summary>
    public bool IsIdentity { get; private init; }

    public static DocumentTransform Build(
        int sourceWidth,
        int sourceHeight,
        AnnRect? crop,
        IReadOnlyList<CutBand> cuts,
        BackdropSettings backdrop)
    {
        var frame = crop is { } requested && requested.Width >= 1 && requested.Height >= 1
            ? AnnotationGeometry.ClampToImage(requested, sourceWidth, sourceHeight)
            : new AnnRect(0, 0, sourceWidth, sourceHeight);

        if (frame.Width < 1 || frame.Height < 1)
        {
            frame = new AnnRect(0, 0, sourceWidth, sourceHeight);
        }

        var bands = Merge(cuts, frame);
        var identity = crop is null && bands.Count == 0 && !backdrop.IsEnabled;
        return new DocumentTransform(frame, bands, backdrop) { IsIdentity = identity };
    }

    /// <summary>
    /// Clips every band to the crop, drops the empty ones, then merges anything that
    /// overlaps or touches. Merging keeps the row map monotone, which is what lets a
    /// single pass compute an output row and lets the inverse exist at all.
    /// </summary>
    private static List<CutBand> Merge(IReadOnlyList<CutBand> cuts, AnnRect frame)
    {
        var clipped = new List<CutBand>();
        foreach (var cut in cuts)
        {
            var start = Math.Max(cut.Offset, frame.Y);
            var end = Math.Min(cut.End, frame.Bottom);
            if (end - start > 0.5)
            {
                clipped.Add(new CutBand(start, end - start));
            }
        }

        clipped.Sort((a, b) => a.Offset.CompareTo(b.Offset));

        var merged = new List<CutBand>();
        foreach (var band in clipped)
        {
            if (merged.Count > 0 && band.Offset <= merged[^1].End)
            {
                var last = merged[^1];
                merged[^1] = new CutBand(last.Offset, Math.Max(last.End, band.End) - last.Offset);
                continue;
            }

            merged.Add(band);
        }

        // A cut that swallows the whole crop would leave nothing to export.
        var total = merged.Sum(band => band.Length);
        if (total >= frame.Height - 1)
        {
            return [];
        }

        return merged;
    }

    /// <summary>True when this source row was cut away and has no output row.</summary>
    public bool IsRemoved(double sourceY) =>
        _bands.Any(band => sourceY >= band.Offset && sourceY < band.End);

    public AnnPoint ToOutput(AnnPoint source)
    {
        var x = _padding + (source.X - _crop.X);
        var y = _padding + (source.Y - _crop.Y) - RemovedAbove(source.Y);
        return new AnnPoint(x, y);
    }

    public AnnRect ToOutput(AnnRect source)
    {
        // The anchor maps, the size is kept. A box straddling a seam therefore spans it,
        // which is right: a box is a callout, not a measurement.
        var origin = ToOutput(new AnnPoint(source.X, source.Y));
        return new AnnRect(origin.X, origin.Y, source.Width, source.Height);
    }

    public AnnPoint ToSource(AnnPoint output)
    {
        var x = output.X - _padding + _crop.X;
        var contentY = output.Y - _padding;

        // Walk the bands in order, adding back each one that sits at or before this
        // output row. The bands are merged and sorted, so one pass is exact.
        var y = contentY + _crop.Y;
        foreach (var band in _bands)
        {
            if (y >= band.Offset)
            {
                y += band.Length;
            }
        }

        return new AnnPoint(x, y);
    }

    /// <summary>
    /// How much of the cut total sits above this source row. A row inside a band
    /// collapses onto the band's own seam.
    /// </summary>
    private double RemovedAbove(double sourceY)
    {
        var removed = 0d;
        foreach (var band in _bands)
        {
            if (sourceY >= band.End)
            {
                removed += band.Length;
            }
            else if (sourceY > band.Offset)
            {
                removed += sourceY - band.Offset;
            }
        }

        return removed;
    }

    /// <summary>
    /// The surviving runs of source rows, paired with where each lands in the output. The
    /// exporter blits these rather than copying row by row.
    /// </summary>
    public IReadOnlyList<Slab> Slabs()
    {
        var slabs = new List<Slab>();
        var cursor = _crop.Y;
        var outputY = _padding;

        foreach (var band in _bands)
        {
            var height = band.Offset - cursor;
            if (height > 0.5)
            {
                slabs.Add(new Slab(
                    new AnnRect(_crop.X, cursor, _crop.Width, height),
                    new AnnRect(_padding, outputY, _crop.Width, height)));
                outputY += height;
            }

            cursor = band.End;
        }

        var tail = _crop.Bottom - cursor;
        if (tail > 0.5)
        {
            slabs.Add(new Slab(
                new AnnRect(_crop.X, cursor, _crop.Width, tail),
                new AnnRect(_padding, outputY, _crop.Width, tail)));
        }

        return slabs;
    }

    /// <summary>Output rows where a cut closed up, for drawing the seam marker.</summary>
    public IReadOnlyList<double> SeamOffsets()
    {
        var seams = new List<double>();
        foreach (var band in _bands)
        {
            seams.Add(ToOutput(new AnnPoint(0, band.Offset)).Y);
        }

        return seams;
    }
}
