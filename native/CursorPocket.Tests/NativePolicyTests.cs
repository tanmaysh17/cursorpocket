using CursorPocket.Core.Models;
using CursorPocket.Core.Services;

namespace CursorPocket.Tests;

public sealed class NativePolicyTests
{
    [Fact]
    public void HotkeyCollisionFallsBackToFirstConfirmedCandidate()
    {
        var attempts = new List<string>();
        var registered = HotkeyCandidateResolver.RegisterFirstAvailable("Ctrl+Shift+Space", candidate =>
        {
            attempts.Add(candidate);
            return candidate == "Win+Alt+Space";
        });

        Assert.Equal("Win+Alt+Space", registered);
        Assert.Equal(["Ctrl+Shift+Space", "Win+Alt+Space"], attempts);
    }

    [Fact]
    public void RememberedDeviceWinsThenDefaultThenFirst()
    {
        var devices = new[]
        {
            new MediaDeviceDescriptor("one", "Desk mic", "audio"),
            new MediaDeviceDescriptor("two", "Headset", "audio", true),
        };

        Assert.Equal("Desk mic", MediaDeviceSelector.SelectRemembered(devices, "desk MIC")?.Name);
        Assert.Equal("Headset", MediaDeviceSelector.SelectRemembered(devices, "missing")?.Name);
        Assert.Null(MediaDeviceSelector.SelectRemembered([], "missing"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void SourceWindowIsRestoredOnlyWhenCurrentlyMinimized(bool minimized, bool expected) =>
        Assert.Equal(expected, WindowActivationPolicy.ShouldIssueRestore(minimized));
}
