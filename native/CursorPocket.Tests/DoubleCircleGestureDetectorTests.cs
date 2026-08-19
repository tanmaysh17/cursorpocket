using CursorPocket.Core.Services;

namespace CursorPocket.Tests;

public sealed class DoubleCircleGestureDetectorTests
{
    /// <summary>
    /// Draws <paramref name="turns"/> loops of the given radius, sampled at
    /// <paramref name="samplesPerTurn"/> points spaced <paramref name="secondsPerSample"/>
    /// apart, and returns how many times the gesture fired.
    /// </summary>
    private static int DrawCircles(
        double radius,
        double turns = 2,
        int samplesPerTurn = 24,
        double secondsPerSample = 0.025,
        int centerX = 400,
        int centerY = 300,
        bool clockwise = true,
        double startSeconds = 0,
        DoubleCircleGestureDetector? detector = null)
    {
        detector ??= new DoubleCircleGestureDetector();
        var samples = (int)Math.Round(samplesPerTurn * turns);
        var triggered = 0;
        for (var index = 0; index <= samples; index++)
        {
            var angle = index / (double)samplesPerTurn * Math.PI * 2 * (clockwise ? 1 : -1);
            var x = centerX + (int)Math.Round(Math.Cos(angle) * radius);
            var y = centerY + (int)Math.Round(Math.Sin(angle) * radius);
            if (detector.Feed(x, y, startSeconds + (index * secondsPerSample)))
            {
                triggered++;
            }
        }
        return triggered;
    }

    [Fact]
    public void TwoQuickCirclesTriggerOnce() => Assert.Equal(1, DrawCircles(36));

    [Theory]
    [InlineData(10)]
    [InlineData(18)]
    [InlineData(36)]
    [InlineData(90)]
    [InlineData(200)]
    [InlineData(380)]
    public void CirclesOfAnySizeTrigger(double radius) =>
        Assert.Equal(1, DrawCircles(radius));

    [Theory]
    // A fast wrist flick: two small loops inside a fifth of a second.
    [InlineData(0.004, 12)]
    [InlineData(0.010, 20)]
    // A slow, deliberate sweep spread over three seconds.
    [InlineData(0.060, 24)]
    [InlineData(0.075, 20)]
    public void CirclesAtAnySpeedTrigger(double secondsPerSample, int samplesPerTurn) =>
        Assert.Equal(1, DrawCircles(60, samplesPerTurn: samplesPerTurn, secondsPerSample: secondsPerSample));

    [Fact]
    public void CounterClockwiseCirclesTrigger() => Assert.Equal(1, DrawCircles(40, clockwise: false));

    [Fact]
    public void CoarselySampledCirclesStillTrigger() =>
        // A fast sweep gives the hook few points per loop; the shape is still a circle.
        Assert.Equal(1, DrawCircles(150, samplesPerTurn: 8, secondsPerSample: 0.02));

    [Fact]
    public void SloppyOvalsStillTrigger()
    {
        var detector = new DoubleCircleGestureDetector();
        var triggered = 0;
        for (var index = 0; index <= 56; index++)
        {
            var angle = index / 28d * Math.PI * 2;
            // Squashed and slightly wobbly, the way a real hand draws it.
            var x = 500 + (int)Math.Round(Math.Cos(angle) * 70);
            var y = 400 + (int)Math.Round(Math.Sin(angle) * 42 + Math.Sin(angle * 3) * 4);
            if (detector.Feed(x, y, index * 0.03))
            {
                triggered++;
            }
        }
        Assert.Equal(1, triggered);
    }

    [Fact]
    public void StraightMotionDoesNotTrigger()
    {
        var detector = new DoubleCircleGestureDetector();
        Assert.DoesNotContain(Enumerable.Range(0, 50), index => detector.Feed(index * 4, index * 2, index * 0.025));
    }

    [Fact]
    public void ASingleCircleDoesNotTrigger() => Assert.Equal(0, DrawCircles(40, turns: 1));

    [Fact]
    public void AGentleArcDoesNotTrigger() => Assert.Equal(0, DrawCircles(300, turns: 0.75, samplesPerTurn: 40));

    [Fact]
    public void BackAndForthMotionDoesNotTrigger()
    {
        var detector = new DoubleCircleGestureDetector();
        var triggered = 0;
        for (var index = 0; index <= 80; index++)
        {
            // Reverses direction repeatedly, so signed travel cancels out.
            var x = 400 + (int)Math.Round(Math.Sin(index / 6d) * 60);
            if (detector.Feed(x, 300 + (index % 3), index * 0.02))
            {
                triggered++;
            }
        }
        Assert.Equal(0, triggered);
    }

    [Fact]
    public void CirclesDrawnFarApartInTimeDoNotTrigger()
    {
        // One circle now and another ten seconds later is not the gesture: the
        // first has aged out of the detection window by then.
        var detector = new DoubleCircleGestureDetector();
        Assert.Equal(0, DrawCircles(40, turns: 1, detector: detector));
        Assert.Equal(0, DrawCircles(40, turns: 1, startSeconds: 10, detector: detector));
    }

    [Fact]
    public void RepeatedGesturesAreRateLimitedByTheCooldown()
    {
        var detector = new DoubleCircleGestureDetector();
        var triggered = 0;
        for (var index = 0; index <= 240; index++)
        {
            var angle = index / 24d * Math.PI * 2;
            var x = 400 + (int)Math.Round(Math.Cos(angle) * 36);
            var y = 300 + (int)Math.Round(Math.Sin(angle) * 36);
            if (detector.Feed(x, y, index * 0.025))
            {
                triggered++;
            }
        }
        // Ten continuous loops over six seconds: the cooldown keeps this from
        // reopening command mode on every extra turn.
        Assert.InRange(triggered, 1, 3);
    }

    [Fact]
    public void ResetForgetsPartialGestures()
    {
        var detector = new DoubleCircleGestureDetector();
        for (var index = 0; index < 20; index++)
        {
            var angle = index / 24d * Math.PI * 2;
            detector.Feed(400 + (int)Math.Round(Math.Cos(angle) * 36), 300 + (int)Math.Round(Math.Sin(angle) * 36), index * 0.025);
        }
        detector.Reset();
        var triggered = 0;
        for (var index = 20; index <= 30; index++)
        {
            var angle = index / 24d * Math.PI * 2;
            if (detector.Feed(400 + (int)Math.Round(Math.Cos(angle) * 36), 300 + (int)Math.Round(Math.Sin(angle) * 36), index * 0.025))
            {
                triggered++;
            }
        }
        Assert.Equal(0, triggered);
    }
}
