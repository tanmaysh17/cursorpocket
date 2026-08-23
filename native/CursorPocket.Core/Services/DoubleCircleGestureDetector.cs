namespace CursorPocket.Core.Services;

/// <summary>
/// Recognises two quick circles drawn with the pointer.
/// <para>
/// The thresholds are deliberately permissive about <b>size</b> and <b>speed</b> — a
/// tiny flick of the wrist and a wide sweep of the whole arm both count, drawn fast
/// or slow — and strict only about <b>shape</b>: the path has to loop consistently
/// around one centre, in one direction, for clearly more than a single turn. That
/// split is what keeps the gesture easy to perform without firing during ordinary
/// mouse work.
/// </para>
/// <para>
/// This runs on the low-level mouse hook for every pointer move, so samples live in
/// a fixed ring buffer, each candidate window is evaluated in place by index, and a
/// running signed-heading total decides whether the geometric test is worth
/// reaching at all. Straight and back-and-forth movement is turned away by that
/// total alone, without allocating.
/// </para>
/// </summary>
public sealed class DoubleCircleGestureDetector
{
    // These sit midway between the original strict thresholds and a much looser pass
    // that turned out to trigger during ordinary mouse work. Small and large circles
    // and fast and slow ones all still register; a careless flick no longer does.
    private const double WindowSeconds = 3.0;
    private const double CooldownSeconds = 1.4;
    private const double MinimumDuration = 0.18;
    private const double MinimumStep = 2;
    private const int MinimumPoints = 14;
    // From a wrist circle to a wide sweep, without treating a sweep across a whole
    // 4K display as a gesture.
    private const double MinimumDiameter = 18;
    private const double MaximumDiameter = 520;
    private const double MaximumAspectRatio = 2.4;
    private const double MaximumRadiusVariation = 0.46;
    private const double MinimumDirectionality = 0.68;
    // Two loops, near enough. This is the strongest guard against false positives,
    // so it stays where it started.
    private const double MinimumAngularTravel = Math.PI * 3.4;
    // Bounds the cost of the sliding-window scan below, which runs on the
    // low-level mouse hook for every pointer move.
    private const int MaximumPoints = 320;
    private const int MaximumCandidates = 40;

    // Heading measured between consecutive samples only approximates the
    // centre-relative travel the geometric test computes, so this gate sits well
    // below MinimumAngularTravel. It exists to skip work, never to reject a
    // gesture: the geometric test remains the only thing that can accept one.
    private const double HeadingGateFactor = 0.55;
    private const double HeadingRebaseLimit = 1e6;

    private readonly GesturePoint[] _points = new GesturePoint[MaximumPoints];
    private int _oldest;
    private int _count;
    private double _cooldownUntil;
    private double _heading;
    private double _cumulativeHeading;

    public bool Feed(int x, int y, double now)
    {
        if (now < _cooldownUntil)
        {
            Reset();
            return false;
        }
        while (_count > 0 && now - Point(0).Time > WindowSeconds)
        {
            DropOldest();
        }
        if (_count > 0)
        {
            var last = Point(_count - 1);
            if (SquaredDistance(x, y, last.X, last.Y) < MinimumStep * MinimumStep)
            {
                return false;
            }
        }
        Append(x, y, now);
        if (_count < MinimumPoints)
        {
            return false;
        }

        var end = _count - 1;
        var lastStart = _count - MinimumPoints;
        if (!HeadingGateOpen(lastStart, end))
        {
            return false;
        }

        // Test the newest candidate first and stride through older ones, so a long
        // trail of movement cannot turn this into a quadratic scan.
        var stride = Math.Max(1, (int)Math.Ceiling((lastStart + 1) / (double)MaximumCandidates));
        for (var start = 0; start <= lastStart; start += stride)
        {
            if (Point(end).Time - Point(start).Time < MinimumDuration)
            {
                break;
            }
            if (LooksLikeDoubleCircle(start, end))
            {
                Reset();
                _cooldownUntil = now + CooldownSeconds;
                return true;
            }
        }
        return false;
    }

    public void Reset()
    {
        _oldest = 0;
        _count = 0;
        _heading = 0;
        _cumulativeHeading = 0;
    }

    private bool HeadingGateOpen(int lastStart, int end)
    {
        // The signed heading total is kept per sample, so the travel of any candidate
        // suffix is a single subtraction. Straight motion accumulates nothing, and
        // motion that doubles back cancels itself out.
        var endHeading = Point(end).CumulativeHeading;
        var widest = 0d;
        for (var start = 0; start <= lastStart; start++)
        {
            var travel = Math.Abs(endHeading - Point(start).CumulativeHeading);
            if (travel > widest)
            {
                widest = travel;
            }
        }
        return widest >= MinimumAngularTravel * HeadingGateFactor;
    }

    private bool LooksLikeDoubleCircle(int start, int end)
    {
        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        for (var index = start; index <= end; index++)
        {
            var point = Point(index);
            if (point.X < minX) minX = point.X;
            if (point.X > maxX) maxX = point.X;
            if (point.Y < minY) minY = point.Y;
            if (point.Y > maxY) maxY = point.Y;
        }
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
        var count = end - start + 1;
        double radiusSum = 0;
        var smallestRadius = double.MaxValue;
        for (var index = start; index <= end; index++)
        {
            var point = Point(index);
            var radius = Math.Sqrt(SquaredDistance(point.X, point.Y, centerX, centerY));
            radiusSum += radius;
            if (radius < smallestRadius)
            {
                smallestRadius = radius;
            }
        }
        var meanRadius = radiusSum / count;
        if (meanRadius <= 0 || smallestRadius < meanRadius * 0.25)
        {
            return false;
        }

        double varianceSum = 0;
        for (var index = start; index <= end; index++)
        {
            var point = Point(index);
            var deviation = Math.Sqrt(SquaredDistance(point.X, point.Y, centerX, centerY)) - meanRadius;
            varianceSum += deviation * deviation;
        }
        var variation = Math.Sqrt(varianceSum / count) / meanRadius;
        var first = Point(start);
        var last = Point(end);
        var closingGap = Math.Max(20, meanRadius * 0.9);
        if (variation > MaximumRadiusVariation ||
            SquaredDistance(first.X, first.Y, last.X, last.Y) > closingGap * closingGap)
        {
            return false;
        }

        double signedTravel = 0;
        double absoluteTravel = 0;
        var previousAngle = Math.Atan2(first.Y - centerY, first.X - centerX);
        for (var index = start + 1; index <= end; index++)
        {
            var point = Point(index);
            var angle = Math.Atan2(point.Y - centerY, point.X - centerX);
            var delta = Normalize(angle - previousAngle);
            previousAngle = angle;
            signedTravel += delta;
            absoluteTravel += Math.Abs(delta);
        }
        return Math.Abs(signedTravel) >= MinimumAngularTravel
            && absoluteTravel > 0
            && Math.Abs(signedTravel) / absoluteTravel >= MinimumDirectionality;
    }

    private void Append(int x, int y, double now)
    {
        if (_count > 0)
        {
            var last = Point(_count - 1);
            var heading = Math.Atan2(y - last.Y, x - last.X);
            _cumulativeHeading += _count > 1 ? Normalize(heading - _heading) : 0;
            _heading = heading;
        }
        else
        {
            _heading = 0;
            _cumulativeHeading = 0;
        }
        if (Math.Abs(_cumulativeHeading) > HeadingRebaseLimit)
        {
            RebaseHeading();
        }

        if (_count == MaximumPoints)
        {
            DropOldest();
        }
        _points[(_oldest + _count) % MaximumPoints] = new GesturePoint(now, x, y, _cumulativeHeading);
        _count++;
    }

    private void RebaseHeading()
    {
        var offset = _cumulativeHeading;
        for (var index = 0; index < _count; index++)
        {
            var slot = (_oldest + index) % MaximumPoints;
            _points[slot] = _points[slot] with { CumulativeHeading = _points[slot].CumulativeHeading - offset };
        }
        _cumulativeHeading = 0;
    }

    private void DropOldest()
    {
        _oldest = (_oldest + 1) % MaximumPoints;
        _count--;
    }

    private ref GesturePoint Point(int index) => ref _points[(_oldest + index) % MaximumPoints];

    private static double SquaredDistance(double x1, double y1, double x2, double y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return dx * dx + dy * dy;
    }

    private static double Normalize(double radians)
    {
        while (radians > Math.PI)
        {
            radians -= Math.PI * 2;
        }
        while (radians < -Math.PI)
        {
            radians += Math.PI * 2;
        }
        return radians;
    }

    private readonly record struct GesturePoint(double Time, double X, double Y, double CumulativeHeading);
}
