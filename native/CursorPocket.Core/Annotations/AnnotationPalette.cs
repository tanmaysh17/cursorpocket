namespace CursorPocket.Core.Annotations;

/// <summary>
/// The ink the user paints with, held here so the toolbar swatches and the renderer
/// cannot disagree. Deliberately excludes every state colour: green means ready, live,
/// the one primary action, or the current selection, and the annotation surface needs
/// green for the active tool and the crop handles. A green arrow would be
/// indistinguishable from CursorPocket talking.
/// </summary>
public static class AnnotationPalette
{
    /// <summary>Alpha applied to a highlighter stroke, composited once per stroke.</summary>
    public const byte HighlightAlpha = 92;

    private static readonly AnnotationInk[] InkSet =
    [
        new("Signal", "#F4353F"),
        new("Amber", "#F0B056"),
        new("Citron", "#F5E663"),
        new("Cyan", "#2FD8E8"),
        new("Violet", "#B57BF0"),
        new("Chalk", "#FFFFFF"),
    ];

    public static IReadOnlyList<AnnotationInk> Inks => InkSet;

    /// <summary>The ink a fresh editor starts on.</summary>
    public static AnnotationInk Default => InkSet[0];

    /// <summary>
    /// Maps the digit keys 1..6 to their ink. Returns null for anything else, so an
    /// unrelated key press is simply not a colour change.
    /// </summary>
    public static AnnotationInk? ForKey(int digit) =>
        digit >= 1 && digit <= InkSet.Length ? InkSet[digit - 1] : null;

    /// <summary>
    /// Whichever of near-black and near-white reads on top of this ink. Shared so the
    /// number inside a step marker is the same colour on screen as in the file — Citron
    /// needs dark digits and Violet needs light ones, and two renderers guessing
    /// separately is how they end up disagreeing.
    /// </summary>
    public static AnnColor OnInk(AnnColor ink)
    {
        // Rec. 709 luma, which tracks perceived brightness far better than a plain mean:
        // a mean calls Citron and Cyan similarly bright when one clearly is not.
        var luma = ((0.2126 * ink.R) + (0.7152 * ink.G) + (0.0722 * ink.B)) / 255;
        return luma > 0.55
            ? new AnnColor(255, 11, 16, 15)
            : new AnnColor(255, 242, 247, 244);
    }
}

/// <summary>One ink: the name shown to the user and the colour it paints.</summary>
public sealed record AnnotationInk(string Name, string Hex)
{
    public AnnColor Colour { get; } = AnnColor.FromHex(Hex);
}
