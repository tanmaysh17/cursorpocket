namespace CursorPocket.Core.Models;

public enum VideoSourceKind
{
    Display,
    Region,
    Window,
}

public sealed record CaptureBounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

public sealed record RecordingOptions
{
    public VideoSourceKind SourceKind { get; init; } = VideoSourceKind.Display;

    /// <summary>
    /// The DXGI output index for Desktop Duplication. This is its own ordering, not a
    /// monitor enumeration index, and must only ever come from
    /// <c>DisplayOutputLocator</c>. Null means the target monitor could not be
    /// addressed that way, so <see cref="Bounds"/> is captured directly instead.
    /// </summary>
    public int? DisplayOutputIndex { get; init; }

    /// <summary>
    /// The exact rectangle being recorded. Set for a region, and also for a display
    /// so the right screen is captured even without a usable output index.
    /// </summary>
    public CaptureBounds? Bounds { get; init; }
    public long? WindowHandle { get; init; }
    public int FramesPerSecond { get; init; } = 30;
    public int Quality { get; init; } = 72;
    public bool DrawCursor { get; init; } = true;
    public bool IncludeMicrophone { get; init; } = true;
    public string MicrophoneId { get; init; } = string.Empty;
    public string MicrophoneName { get; init; } = string.Empty;
    public bool IncludeCamera { get; init; }
    public string CameraName { get; init; } = string.Empty;
    public string CameraPosition { get; init; } = "bottom-right";
    public int CameraWidth { get; init; } = 360;
    public int CountdownSeconds { get; init; } = 3;
}

public sealed record MediaDeviceDescriptor(string Id, string Name, string Kind, bool IsDefault = false);

public enum RecordingState
{
    Idle,
    Starting,
    Recording,
    Finalizing,
    Failed,
}
