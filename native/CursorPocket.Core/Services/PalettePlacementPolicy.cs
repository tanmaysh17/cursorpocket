namespace CursorPocket.Core.Services;

public enum PaletteCorner
{
    TopRight,
    BottomRight,
    BottomLeft,
    TopLeft,
}

public readonly record struct PaletteRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;

    public static PaletteRect FromEdges(int left, int top, int right, int bottom) =>
        new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));

    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
}

/// <summary>
/// Decides where the compact command-mode panel sits on the pointer's display and
/// when it has to step out of the pointer's way. Command mode is a small surface
/// rather than a full-screen overlay, so it can end up directly under whatever the
/// user is reaching for; these rules keep it clear of the pointer without hiding it.
/// Kept free of Windows UI types so the placement decisions stay unit-testable.
/// </summary>
public static class PalettePlacementPolicy
{
    /// <summary>
    /// Candidate anchors in preference order. Ties resolve to the earlier entry so
    /// repeated placements are deterministic.
    /// </summary>
    public static readonly PaletteCorner[] Corners =
    [
        PaletteCorner.TopRight,
        PaletteCorner.BottomRight,
        PaletteCorner.BottomLeft,
        PaletteCorner.TopLeft,
    ];

    public static PaletteRect RectFor(PaletteCorner corner, PaletteRect workArea, int width, int height, int margin)
    {
        var safeMargin = Math.Max(0, margin);
        var panelWidth = Math.Clamp(width, 1, Math.Max(1, workArea.Width));
        var panelHeight = Math.Clamp(height, 1, Math.Max(1, workArea.Height));
        // A panel that nearly fills the work area keeps whatever gap is left
        // instead of being pushed off screen by the preferred margin.
        var gapX = Math.Min(safeMargin, Math.Max(0, workArea.Width - panelWidth));
        var gapY = Math.Min(safeMargin, Math.Max(0, workArea.Height - panelHeight));
        var left = corner is PaletteCorner.TopLeft or PaletteCorner.BottomLeft
            ? workArea.Left + gapX
            : workArea.Right - panelWidth - gapX;
        var top = corner is PaletteCorner.TopLeft or PaletteCorner.TopRight
            ? workArea.Top + gapY
            : workArea.Bottom - panelHeight - gapY;
        return new PaletteRect(left, top, panelWidth, panelHeight);
    }

    public static double DistanceToRect(PaletteRect rect, int x, int y)
    {
        var dx = (double)Math.Max(Math.Max(rect.Left - x, x - rect.Right), 0);
        var dy = (double)Math.Max(Math.Max(rect.Top - y, y - rect.Bottom), 0);
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    public static bool IsPointerEncroaching(PaletteRect rect, int x, int y, int padding) =>
        DistanceToRect(rect, x, y) <= Math.Max(0, padding);

    /// <summary>
    /// Picks the anchor that leaves the most room between the panel and the pointer.
    /// Anchors clear of the keep-away band always beat crowded ones, so on a display
    /// too small for any clear anchor the panel still lands as far away as it can.
    /// Pass <paramref name="avoid"/> to exclude the anchor currently in use, which
    /// guarantees a visible move when the pointer closes in.
    /// </summary>
    public static PaletteCorner ChooseCorner(
        PaletteRect workArea,
        int width,
        int height,
        int margin,
        int pointerX,
        int pointerY,
        int padding,
        PaletteCorner? avoid = null)
    {
        PaletteCorner? best = null;
        var bestIsClear = false;
        var bestDistance = -1d;
        foreach (var corner in Corners)
        {
            if (avoid == corner)
            {
                continue;
            }
            var candidate = RectFor(corner, workArea, width, height, margin);
            var distance = DistanceToRect(candidate, pointerX, pointerY);
            var isClear = distance > Math.Max(0, padding);
            var better = best is null
                || (isClear && !bestIsClear)
                || (isClear == bestIsClear && distance > bestDistance);
            if (better)
            {
                best = corner;
                bestIsClear = isClear;
                bestDistance = distance;
            }
        }
        return best ?? Corners[0];
    }
}
