using CursorPocket.Core.Models;

namespace CursorPocket.Core.Services;

/// <summary>
/// Turns the two corners the user dragged between into the rectangle to capture.
/// <para>
/// The inputs must be <b>physical screen pixels</b> in virtual-desktop coordinates.
/// Screen capture works in physical pixels, while a XAML pointer position is in
/// device-independent pixels; feeding the latter straight through captured a
/// rectangle smaller than the selection by the display's scale factor, losing the
/// right and bottom of every region on a scaled display.
/// </para>
/// </summary>
public static class RegionSelection
{
    /// <summary>Below this, a selection is a stray click rather than a region.</summary>
    public const int MinimumSide = 4;

    public static CaptureBounds FromCorners(int startX, int startY, int endX, int endY) =>
        new(Math.Min(startX, endX), Math.Min(startY, endY), Math.Max(startX, endX), Math.Max(startY, endY));

    public static bool IsUsable(CaptureBounds bounds) =>
        bounds.Width >= MinimumSide && bounds.Height >= MinimumSide;

    /// <summary>
    /// Where a selection sits inside the frozen desktop frame the selector showed, so the
    /// shot can be cut from that frame instead of grabbing the screen a second time. The
    /// frame starts at the virtual-desktop origin, which is negative when a monitor sits
    /// left of or above the primary one. Null when the selection misses the frame.
    /// </summary>
    public static CaptureBounds? WithinSnapshot(
        CaptureBounds bounds,
        int snapshotLeft,
        int snapshotTop,
        int snapshotWidth,
        int snapshotHeight)
    {
        var left = Math.Clamp(bounds.Left - snapshotLeft, 0, snapshotWidth);
        var top = Math.Clamp(bounds.Top - snapshotTop, 0, snapshotHeight);
        var right = Math.Clamp(bounds.Right - snapshotLeft, 0, snapshotWidth);
        var bottom = Math.Clamp(bounds.Bottom - snapshotTop, 0, snapshotHeight);
        var local = new CaptureBounds(left, top, right, bottom);
        return local.Width > 0 && local.Height > 0 ? local : null;
    }
}
