namespace CursorPocket.Core.Annotations;

/// <summary>
/// One mark on a screenshot. Marks are plain data: they hold no WinUI element, which
/// is what makes redo possible — the first annotation surface stored its live visual
/// on the operation, so an undone mark could never be rebuilt.
/// </summary>
/// <remarks>
/// A sealed record per kind rather than one record with a tool discriminator and a row
/// of nullable payloads. The discriminator version already existed here and showed the
/// failure mode: a Points collection that was null for four tools of five, beside a
/// Text that was null for four of five, with nothing preventing a redaction carrying
/// text. Pattern matching over the hierarchy gives each kind exactly its own payload.
/// </remarks>
public abstract record AnnotationMark
{
    /// <summary>
    /// Identity, so the surface can map a mark to the element drawing it. Records
    /// compare by value, so two visually identical marks would collide as keys.
    /// </summary>
    public required int Id { get; init; }

    public required AnnColor Colour { get; init; }

    /// <summary>Stroke weight in source-image pixels.</summary>
    public required double StrokeWidth { get; init; }
}

public sealed record ArrowMark : AnnotationMark
{
    public required AnnPoint Start { get; init; }
    public required AnnPoint End { get; init; }
}

public sealed record LineMark : AnnotationMark
{
    public required AnnPoint Start { get; init; }
    public required AnnPoint End { get; init; }
}

/// <summary>Freehand pen or highlighter. Highlighter differs by alpha and blend, not shape.</summary>
public sealed record StrokeMark : AnnotationMark
{
    public required IReadOnlyList<AnnPoint> Points { get; init; }

    /// <summary>
    /// A highlighter composites once at its own alpha. Drawing it as a single
    /// translucent polyline made a stroke darken itself wherever it crossed over.
    /// </summary>
    public bool Highlight { get; init; }
}

public sealed record BoxMark : AnnotationMark
{
    public required AnnRect Rect { get; init; }
    public bool Filled { get; init; }
    public double CornerRadius { get; init; }
}

public sealed record EllipseMark : AnnotationMark
{
    public required AnnRect Rect { get; init; }
    public bool Filled { get; init; }
}

/// <summary>
/// A numbered step marker. The number is stored on the mark, never derived at render
/// time: undoing marker 3 and drawing a new one has to produce 3 again, and a text mark
/// that says "see 2" must not be invalidated by deleting marker 1.
/// </summary>
public sealed record MarkerMark : AnnotationMark
{
    public required AnnPoint Center { get; init; }
    public required int Number { get; init; }
    public required double Radius { get; init; }
}

/// <summary>
/// A patch of the screenshot obliterated in place. Carries its own style so a document
/// can mix a solid block over a password with a pixelated face.
/// </summary>
public sealed record RedactMark : AnnotationMark
{
    public required AnnRect Rect { get; init; }
    public RedactStyle Style { get; init; } = RedactStyle.Solid;
}

/// <summary>
/// Draws the eye to one region: everything outside it is dimmed, and in Loupe mode the
/// inside is also magnified.
/// </summary>
public sealed record FocusMark : AnnotationMark
{
    public required AnnRect Rect { get; init; }
    public FocusMode Mode { get; init; } = FocusMode.Dim;
    public FocusShape Shape { get; init; } = FocusShape.Rectangle;
    public double Magnification { get; init; } = 2;
}

public sealed record TextMark : AnnotationMark
{
    public required AnnPoint Anchor { get; init; }
    public required string Text { get; init; }
    public required double FontSize { get; init; }

    /// <summary>
    /// A readability pill behind the glyphs. Unreadable annotation text over busy
    /// content is the most common real failure of a screenshot annotator, so the pill
    /// is on by default and T toggles it.
    /// </summary>
    public bool Pill { get; init; } = true;
}
