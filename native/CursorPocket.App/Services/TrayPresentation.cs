using CursorPocket.Core.Models;

namespace CursorPocket_App.Services;

internal readonly record struct TrayPresentation(string Tooltip, string IconFilename)
{
    public static TrayPresentation For(RecordingState state) =>
        state is RecordingState.Starting or RecordingState.Recording or RecordingState.Finalizing
            ? new("CursorPocket · recording", "TrayRecording.ico")
            : new("CursorPocket · ready", "TrayReady.ico");
}
