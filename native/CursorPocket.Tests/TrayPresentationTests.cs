using CursorPocket.Core.Models;
using CursorPocket_App.Services;

namespace CursorPocket.Tests;

public sealed class TrayPresentationTests
{
    [Theory]
    [InlineData(RecordingState.Starting)]
    [InlineData(RecordingState.Recording)]
    [InlineData(RecordingState.Finalizing)]
    public void Active_states_use_the_recording_orbit(RecordingState state)
    {
        var presentation = TrayPresentation.For(state);

        Assert.Equal("CursorPocket · recording", presentation.Tooltip);
        Assert.Equal("TrayRecording.ico", presentation.IconFilename);
    }

    [Theory]
    [InlineData(RecordingState.Idle)]
    [InlineData(RecordingState.Failed)]
    public void Inactive_states_use_the_primary_orbit(RecordingState state)
    {
        var presentation = TrayPresentation.For(state);

        Assert.Equal("CursorPocket · ready", presentation.Tooltip);
        Assert.Equal("TrayReady.ico", presentation.IconFilename);
    }
}
