namespace CursorPocket.Core.Services;

public sealed class DoubleCircleGestureDetector
{
    private const double WindowSeconds = 1.8;
    private const double CooldownSeconds = 1.4;
    private const double MinimumDuration = 0.45;
    private const double MinimumStep = 2;
    private const int MinimumPoints = 18;
    private const double MinimumDiameter = 24;
    private const double MaximumDiameter = 180;
    private const double MaximumAspectRatio = 2.2;
    private const double MaximumRadiusVariation = 0.42;
    private const double MinimumDirectionality = 0.72;
    private const double MinimumAngularTravel = Math.PI * 3.4;

    private readonly Queue<GesturePoint> _points = new();
    private double _cooldownUntil;

    public bool Feed(int x, int y, double now)
    {
        if (now < _cooldownUntil)
        {
            _points.Clear();
            return false;
        }
        while (_points.TryPeek(out var first) && now - first.Time > WindowSeconds)
        {
            _points.Dequeue();
        }
        if (_points.TryPeekLast(out var last) && Distance(x, y, last.X, last.Y) < MinimumStep)
        {
            return false;
        }
        _points.Enqueue(new GesturePoint(now, x, y));
        if (_points.Count < MinimumPoints)
        {
            return false;
        }

        var points = _points.ToArray();
        for (var start = 0; start <= points.Length - MinimumPoints; start++)
        {
            var candidate = points[start..];
            if (candidate[^1].Time - candidate[0].Time < MinimumDuration)
            {
                break;
            }
            if (LooksLikeDoubleCircle(candidate))
            {
                _points.Clear();
                _cooldownUntil = now + CooldownSeconds;
                return true;
            }
        }
        return false;
    }

    public void Reset() => _points.Clear();

    private static bool LooksLikeDoubleCircle(IReadOnlyList<GesturePoint> points)
    {
        var minX = points.Min(point => point.X);
        var maxX = points.Max(point => point.X);
        var minY = points.Min(point => point.Y);
        var maxY = points.Max(point => point.Y);
        var width = maxX - minX;
        var height = maxY - minY;
        var diameter = Math.Max(width, height);
        var smaller = Math.Min(width, height);
        if (smaller < MinimumDiameter || diameter > MaximumDiameter || diameter / smaller > MaximumAspectRatio)
        {
            return false;
        }

        var centerX = (minX + maxX) / 2;
        var centerY = (minY + maxY) / 2;
        var radii = points.Select(point => Distance(point.X, point.Y, centerX, centerY)).ToArray();
        var meanRadius = radii.Average();
        if (meanRadius <= 0 || radii.Min() < meanRadius * 0.25)
        {
            return false;
        }
        var variation = Math.Sqrt(radii.Average(radius => Math.Pow(radius - meanRadius, 2))) / meanRadius;
        if (variation > MaximumRadiusVariation || Distance(points[0].X, points[0].Y, points[^1].X, points[^1].Y) > Math.Max(18, meanRadius * 0.8))
        {
            return false;
        }

        var angles = points.Select(point => Math.Atan2(point.Y - centerY, point.X - centerX)).ToArray();
        double signedTravel = 0;
        double absoluteTravel = 0;
        for (var index = 1; index < angles.Length; index++)
        {
            var rawDelta = angles[index] - angles[index - 1];
            var delta = Math.Atan2(Math.Sin(rawDelta), Math.Cos(rawDelta));
            signedTravel += delta;
            absoluteTravel += Math.Abs(delta);
        }
        return Math.Abs(signedTravel) >= MinimumAngularTravel
            && absoluteTravel > 0
            && Math.Abs(signedTravel) / absoluteTravel >= MinimumDirectionality;
    }

    private static double Distance(double x1, double y1, double x2, double y2) =>
        Math.Sqrt(Math.Pow(x1 - x2, 2) + Math.Pow(y1 - y2, 2));

    private readonly record struct GesturePoint(double Time, double X, double Y);
}

internal static class QueueExtensions
{
    public static bool TryPeekLast<T>(this Queue<T> queue, out T value)
    {
        if (queue.Count == 0)
        {
            value = default!;
            return false;
        }
        value = queue.Last();
        return true;
    }
}
