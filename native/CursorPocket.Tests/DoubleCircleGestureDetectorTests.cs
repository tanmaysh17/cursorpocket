using CursorPocket.Core.Services;

namespace CursorPocket.Tests;

public sealed class DoubleCircleGestureDetectorTests
{
    [Fact]
    public void TwoQuickCirclesTriggerOnce()
    {
        var detector = new DoubleCircleGestureDetector();
        var triggered = 0;
        for (var index = 0; index <= 48; index++)
        {
            var angle = index / 48d * Math.PI * 4;
            var x = 400 + (int)Math.Round(Math.Cos(angle) * 36);
            var y = 300 + (int)Math.Round(Math.Sin(angle) * 36);
            if (detector.Feed(x, y, index * 0.025))
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
}
