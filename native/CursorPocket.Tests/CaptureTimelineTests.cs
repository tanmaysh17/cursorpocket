using CursorPocket.Core.Diagnostics;

namespace CursorPocket.Tests;

public sealed class CaptureTimelineTests
{
    [Fact]
    public void Nothing_is_measured_unless_the_switch_is_set()
    {
        Assert.Null(CaptureTimeline.Start("region", _ => null));
        Assert.Null(CaptureTimeline.Start("region", _ => "0"));
        Assert.NotNull(CaptureTimeline.Start("region", _ => "1"));
    }

    [Fact]
    public void A_timeline_is_written_once_however_many_paths_try_to_close_it()
    {
        var timeline = CaptureTimeline.Start("display", _ => "1")!;
        timeline.Mark("pixels");

        var first = timeline.Complete(DateTimeOffset.UnixEpoch);
        var second = timeline.Complete(DateTimeOffset.UnixEpoch);

        Assert.NotNull(first);
        Assert.Contains(" display pixels=", first, StringComparison.Ordinal);
        Assert.Null(second);
    }

    [Fact]
    public void The_line_is_culture_invariant()
    {
        var line = CaptureTimeline.Format(
            new DateTimeOffset(2026, 10, 16, 9, 30, 0, TimeSpan.Zero),
            "region",
            [("selector-shown", 12.34), ("editor-ready", 156.78)]);

        Assert.Equal("2026-10-16T09:30:00.000+00:00 region selector-shown=12.3 editor-ready=156.8", line);
    }
}
