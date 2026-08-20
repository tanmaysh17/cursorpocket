namespace CursorPocket.Core.Media;

/// <summary>
/// Everything the camera effect pipeline needs to know, resolved once per
/// recording. All values default to "off" so a user who never opens the effect
/// controls records exactly what the camera produces.
/// </summary>
public sealed record CameraEffectSettings
{
    public const string BackgroundNone = "none";
    public const string BackgroundBlur = "blur";
    public const string BackgroundImage = "image";

    /// <summary>One of <see cref="BackgroundNone"/>, <see cref="BackgroundBlur"/>, <see cref="BackgroundImage"/>.</summary>
    public string BackgroundMode { get; init; } = BackgroundNone;

    /// <summary>
    /// Image source when <see cref="BackgroundMode"/> is "image". Either an
    /// absolute file path or an <c>asset:</c> token naming a bundled background.
    /// </summary>
    public string BackgroundImagePath { get; init; } = string.Empty;

    /// <summary>0 = off, 1 = subtle, 2 = strong.</summary>
    public int TouchUpLevel { get; init; }

    /// <summary>−100..100, 0 = camera output unchanged.</summary>
    public int Brightness { get; init; }

    /// <summary>−100..100, negative = cooler, positive = warmer.</summary>
    public int Warmth { get; init; }

    /// <summary>−100..100, 0 = camera output unchanged.</summary>
    public int Contrast { get; init; }

    public bool HasColorAdjustment => Brightness != 0 || Warmth != 0 || Contrast != 0;

    /// <summary>Whether the background modes require a person mask.</summary>
    public bool NeedsSegmentation => BackgroundMode is BackgroundBlur or BackgroundImage;

    /// <summary>
    /// When false the app keeps the untouched MediaPlayer preview path, so a
    /// user with no effects enabled is on exactly the pre-effects pipeline.
    /// </summary>
    public bool HasAnyEffect => HasColorAdjustment || TouchUpLevel > 0 || NeedsSegmentation;
}
