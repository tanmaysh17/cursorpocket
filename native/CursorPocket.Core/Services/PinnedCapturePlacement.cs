using CursorPocket.Core.Models;

namespace CursorPocket.Core.Services;

/// <summary>
/// Sizes and places a pinned capture: a screenshot the user chose to leave on screen while
/// they carry on working.
/// </summary>
/// <remarks>
/// A pin is only ever created by an explicit action, and it is never restored after a
/// restart. Both matter: a floating window the user did not ask for, or one that reappears
/// after a reboot with no explanation, is exactly the unexplained floating widget the
/// product's anti-references warn against. The Library holds the durable copy.
/// </remarks>
public static class PinnedCapturePlacement
{
    /// <summary>A pin never takes more than this share of the screen's width.</summary>
    public const double MaximumWidthShare = 1d / 3d;

    /// <summary>...nor this share of its height.</summary>
    public const double MaximumHeightShare = 0.5;

    /// <summary>Inset from the work area's corner.</summary>
    public const int Margin = 24;

    /// <summary>How far each pin is offset from the one before it.</summary>
    public const int CascadeStep = 28;

    /// <summary>Smallest a pin can be scaled to, as a share of its natural size.</summary>
    public const double MinimumScale = 0.2;

    public const double MaximumScale = 1.0;

    /// <summary>
    /// The size a pin should be at a given scale, never exceeding the screen caps and
    /// always preserving the image's aspect ratio.
    /// </summary>
    public static (int Width, int Height) Size(
        CaptureBounds workArea,
        int imageWidth,
        int imageHeight,
        double scale)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
        {
            return (1, 1);
        }

        var clampedScale = Math.Clamp(scale, MinimumScale, MaximumScale);
        var width = imageWidth * clampedScale;
        var height = imageHeight * clampedScale;

        var maxWidth = Math.Max(1, workArea.Width * MaximumWidthShare);
        var maxHeight = Math.Max(1, workArea.Height * MaximumHeightShare);

        // One factor for both axes, so the aspect ratio survives the cap.
        var fit = Math.Min(1, Math.Min(maxWidth / width, maxHeight / height));
        return (
            Math.Max(1, (int)Math.Round(width * fit)),
            Math.Max(1, (int)Math.Round(height * fit)));
    }

    /// <summary>
    /// Where the pin at <paramref name="index"/> goes. Pins cascade from the top-right
    /// like stacked windows — predictable, and it needs no tiling algorithm — wrapping back
    /// to the corner rather than marching off the screen.
    /// </summary>
    public static CaptureBounds Place(
        CaptureBounds workArea,
        int width,
        int height,
        int index)
    {
        var anchorX = workArea.Right - Margin - width;
        var anchorY = workArea.Top + Margin;

        var step = Math.Max(0, index) * CascadeStep;
        // Wrap once the cascade would push the pin past the far edge, so the tenth pin is
        // still on screen.
        var room = Math.Max(1, Math.Min(
            workArea.Width - (Margin * 2) - width,
            workArea.Height - (Margin * 2) - height));
        var offset = room <= 0 ? 0 : step % room;

        var left = Math.Clamp(anchorX - offset, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
        var top = Math.Clamp(anchorY + offset, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height));
        return new CaptureBounds(left, top, left + width, top + height);
    }

    /// <summary>Steps the scale by one wheel notch, stopping at the ends.</summary>
    public static double StepScale(double scale, int direction) =>
        Math.Clamp(scale + (Math.Sign(direction) * 0.1), MinimumScale, MaximumScale);
}
