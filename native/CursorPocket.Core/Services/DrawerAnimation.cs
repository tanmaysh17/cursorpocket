using CursorPocket.Core.Models;

namespace CursorPocket.Core.Services;

/// <summary>
/// Drives a surface that opens and closes like a drawer.
/// <para>
/// Window geometry cannot be animated by the composition engine, so the HUD steps
/// its own size each frame. Snapping between two sizes on hover read as abrupt; a
/// short eased travel, plus opening on approach rather than on contact, is what
/// makes it feel like something being pulled open.
/// </para>
/// </summary>
public static class DrawerAnimation
{
    public const double DefaultDurationMs = 190;

    /// <summary>How far outside the surface the pointer counts as approaching.</summary>
    public const int DefaultProximityPadding = 88;

    /// <summary>
    /// Moves <paramref name="progress"/> toward <paramref name="target"/> at a rate
    /// that covers the full range in <paramref name="durationMs"/>, never overshooting.
    /// </summary>
    public static double Advance(double progress, double target, double elapsedMs, double durationMs = DefaultDurationMs)
    {
        var from = Clamp(progress);
        var to = Clamp(target);
        if (durationMs <= 0 || elapsedMs <= 0)
        {
            return to;
        }
        var step = elapsedMs / durationMs;
        if (Math.Abs(to - from) <= step)
        {
            return to;
        }
        return Clamp(from + (to > from ? step : -step));
    }

    /// <summary>Smoothstep, so the travel starts and ends gently instead of sliding at a constant rate.</summary>
    public static double Ease(double progress)
    {
        var clamped = Clamp(progress);
        return clamped * clamped * (3 - (2 * clamped));
    }

    public static int Lerp(int from, int to, double eased) =>
        from + (int)Math.Round((to - from) * Clamp(eased));

    public static bool IsPointerNear(CaptureBounds bounds, int x, int y, int padding = DefaultProximityPadding)
    {
        var safePadding = Math.Max(0, padding);
        return x >= bounds.Left - safePadding && x <= bounds.Right + safePadding &&
            y >= bounds.Top - safePadding && y <= bounds.Bottom + safePadding;
    }

    private static double Clamp(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
}
