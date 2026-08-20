using CursorPocket.Core.Services;

namespace CursorPocket.Tests;

public class SquircleGeometryTests
{
    [Fact]
    public void StaysInsideTheWindowBox()
    {
        var points = SquircleGeometry.ComputePolygon(360, 360);
        Assert.All(points, point =>
        {
            Assert.InRange(point.X, 0, 360);
            Assert.InRange(point.Y, 0, 360);
        });
    }

    [Fact]
    public void ReachesEveryEdgeSoTheShapeFillsItsWindow()
    {
        var points = SquircleGeometry.ComputePolygon(240, 240);
        Assert.Contains(points, point => point.X == 0);
        Assert.Contains(points, point => point.X == 240);
        Assert.Contains(points, point => point.Y == 0);
        Assert.Contains(points, point => point.Y == 240);
    }

    /// <summary>
    /// The whole point of the squircle: corners are far plumper than an ellipse
    /// would give. At 45° the superellipse sits well outside the circle.
    /// </summary>
    [Fact]
    public void IsPlumperThanAnEllipse()
    {
        var points = SquircleGeometry.ComputePolygon(200, 200);
        var radius = 100d;
        var corner = points
            .Select(point => (X: point.X - radius, Y: point.Y - radius))
            .OrderByDescending(point => Math.Min(point.X, point.Y))
            .First();
        var ellipseDiagonal = radius / Math.Sqrt(2);
        Assert.True(corner.X > ellipseDiagonal * 1.15, $"Corner {corner} was not plumper than an ellipse.");
    }

    [Fact]
    public void FollowsANonSquareBoxWithoutDistorting()
    {
        var points = SquircleGeometry.ComputePolygon(400, 200);
        Assert.Contains(points, point => point.X == 400);
        Assert.Contains(points, point => point.Y == 200);
        Assert.All(points, point => Assert.InRange(point.Y, 0, 200));
    }

    [Fact]
    public void ProducesNoDuplicateConsecutivePoints()
    {
        var points = SquircleGeometry.ComputePolygon(160, 160);
        for (var index = 1; index < points.Length; index++)
        {
            Assert.NotEqual(points[index - 1], points[index]);
        }
    }

    [Theory]
    [InlineData(1, 200)]
    [InlineData(200, 1)]
    public void RejectsSizesTooSmallToDraw(int width, int height) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SquircleGeometry.ComputePolygon(width, height));

    [Fact]
    public void RejectsExponentsThatWouldDentTheSides() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SquircleGeometry.ComputePolygon(200, 200, exponent: 1.5));
}
