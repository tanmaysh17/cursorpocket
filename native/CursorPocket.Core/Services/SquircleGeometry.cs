namespace CursorPocket.Core.Services;

/// <summary>
/// Superellipse ("squircle") outline for the camera self-view's plump-square
/// shape. Produces the integer polygon handed to <c>CreatePolygonRgn</c>, so a
/// point per output pixel of curvature is enough — GDI regions are not
/// antialiased anyway.
/// </summary>
public static class SquircleGeometry
{
    /// <summary>The classic squircle exponent: visibly squarer than an ellipse, rounder than a rounded rect.</summary>
    public const double Exponent = 4d;

    /// <summary>
    /// Points tracing |x/a|^n + |y/b|^n = 1 for a width×height box, clockwise
    /// from the rightmost point. Coordinates are window-local pixels.
    /// </summary>
    public static (int X, int Y)[] ComputePolygon(int width, int height, double exponent = Exponent, int pointsPerQuadrant = 32)
    {
        if (width < 2 || height < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "The squircle needs a visible size.");
        }
        if (exponent < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(exponent), "Exponents below 2 dent the sides inward.");
        }
        var a = width / 2d;
        var b = height / 2d;
        var points = new List<(int X, int Y)>(pointsPerQuadrant * 4);
        var total = pointsPerQuadrant * 4;
        for (var step = 0; step < total; step++)
        {
            var angle = step * (Math.PI * 2) / total;
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            // Superellipse parametrization: sign(t)·|t|^(2/n) traces the outline.
            var x = a + a * Math.Sign(cos) * Math.Pow(Math.Abs(cos), 2 / exponent);
            var y = b + b * Math.Sign(sin) * Math.Pow(Math.Abs(sin), 2 / exponent);
            var point = ((int)Math.Round(Math.Clamp(x, 0, width)), (int)Math.Round(Math.Clamp(y, 0, height)));
            if (points.Count == 0 || points[^1] != point)
            {
                points.Add(point);
            }
        }
        return [.. points];
    }
}
