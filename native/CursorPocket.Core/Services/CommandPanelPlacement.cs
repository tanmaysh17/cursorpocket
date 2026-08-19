using CursorPocket.Core.Models;

namespace CursorPocket.Core.Services;

/// <summary>
/// Where the command panel sits, and how a position the user dragged it to is
/// remembered.
/// <para>
/// The position is stored as a pair of fractions of the free space rather than as
/// screen coordinates: 0,0 is the top-left corner of the work area and 1,1 the
/// bottom-right. That keeps a remembered position meaningful across a different
/// display, a different resolution, and a different DPI — the panel lands in the
/// same relative spot and can never be restored off screen.
/// </para>
/// </summary>
public static class CommandPanelPlacement
{
    /// <summary>Top-right, the position command mode has always opened in.</summary>
    public const double DefaultAnchorX = 1;
    public const double DefaultAnchorY = 0;

    public static CaptureBounds Resolve(
        CaptureBounds workArea,
        int width,
        int height,
        double anchorX,
        double anchorY,
        int margin)
    {
        var available = Inset(workArea, margin, width, height);
        var panelWidth = Math.Clamp(width, 1, Math.Max(1, available.Width));
        var panelHeight = Math.Clamp(height, 1, Math.Max(1, available.Height));
        var left = available.Left + (int)Math.Round(Math.Max(0, available.Width - panelWidth) * Clamp(anchorX));
        var top = available.Top + (int)Math.Round(Math.Max(0, available.Height - panelHeight) * Clamp(anchorY));
        return new CaptureBounds(left, top, left + panelWidth, top + panelHeight);
    }

    /// <summary>
    /// The inverse of <see cref="Resolve"/>: turns the position the user dragged the
    /// panel to back into fractions worth persisting. A drag past the margin is
    /// clamped rather than rejected, so the panel can be pushed flush to an edge.
    /// </summary>
    public static (double X, double Y) AnchorFor(
        CaptureBounds workArea,
        int width,
        int height,
        int left,
        int top,
        int margin)
    {
        var available = Inset(workArea, margin, width, height);
        var panelWidth = Math.Clamp(width, 1, Math.Max(1, available.Width));
        var panelHeight = Math.Clamp(height, 1, Math.Max(1, available.Height));
        var freeX = Math.Max(0, available.Width - panelWidth);
        var freeY = Math.Max(0, available.Height - panelHeight);
        return (
            freeX == 0 ? DefaultAnchorX : Clamp((left - available.Left) / (double)freeX),
            freeY == 0 ? DefaultAnchorY : Clamp((top - available.Top) / (double)freeY));
    }

    /// <summary>
    /// Deflates the work area by the preferred margin, giving the margin up first if
    /// the panel would not otherwise fit.
    /// </summary>
    private static CaptureBounds Inset(CaptureBounds workArea, int margin, int width, int height)
    {
        var safeMargin = Math.Max(0, margin);
        var insetX = Math.Min(safeMargin, Math.Max(0, (workArea.Width - width) / 2));
        var insetY = Math.Min(safeMargin, Math.Max(0, (workArea.Height - height) / 2));
        return new CaptureBounds(
            workArea.Left + insetX,
            workArea.Top + insetY,
            workArea.Right - insetX,
            workArea.Bottom - insetY);
    }

    private static double Clamp(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}
