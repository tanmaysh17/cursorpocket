namespace CursorPocket.Core.Annotations;

/// <summary>
/// The backdrops a screenshot can be exported on, cycled by pressing B.
/// </summary>
/// <remarks>
/// <para>
/// Flat fills, not mesh gradients. This is the one place the reference tool's look was
/// deliberately narrowed: a gradient generator inside the app leaks — the picker wants
/// preview swatches, the swatches want to look like the gradients, and the product's one
/// aesthetic rule about structure never coming from tinted blobs stops holding. A flat
/// ground plus a real shadow does the actual job, which is giving a tightly cropped
/// screenshot room to breathe and an edge to sit on.
/// </para>
/// <para>
/// Padding is a fraction of the image's short edge rather than a pixel count, so the same
/// preset looks the same on a small region capture and on a 4K shot.
/// </para>
/// </remarks>
public static class AnnotationBackdrops
{
    private static readonly AnnotationBackdrop[] Set =
    [
        new("None", 0, 0, "#00000000", 0, 0),
        new("Graphite", 0.06, 0.018, "#0B100F", 0.05, 0.55),
        new("Slate", 0.09, 0.022, "#1A2430", 0.07, 0.45),
        new("Paper", 0.09, 0.022, "#ECEFEA", 0.07, 0.35),
    ];

    public static IReadOnlyList<AnnotationBackdrop> Presets => Set;

    /// <summary>Steps to the next preset, wrapping back to None.</summary>
    public static int Next(int index) => Set.Length == 0 ? 0 : (index + 1) % Set.Length;

    public static AnnotationBackdrop At(int index) =>
        index >= 0 && index < Set.Length ? Set[index] : Set[0];

    /// <summary>Resolves a preset against a particular image size.</summary>
    public static BackdropSettings Resolve(int index, int width, int height)
    {
        var preset = At(index);
        if (preset.PaddingFraction <= 0)
        {
            return BackdropSettings.None;
        }

        var shortEdge = Math.Max(1, Math.Min(width, height));
        return new BackdropSettings(
            Math.Round(shortEdge * preset.PaddingFraction),
            Math.Round(shortEdge * preset.CornerFraction),
            AnnColor.FromHex(preset.Hex),
            Math.Round(shortEdge * preset.ShadowFraction),
            preset.ShadowOpacity);
    }
}

/// <summary>One backdrop preset, sized in fractions of the image's short edge.</summary>
public sealed record AnnotationBackdrop(
    string Name,
    double PaddingFraction,
    double CornerFraction,
    string Hex,
    double ShadowFraction,
    double ShadowOpacity);
