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
    public int DisplayIndex { get; init; }
    public CaptureBounds? Bounds { get; init; }
    public long? WindowHandle { get; init; }
    public int FramesPerSecond { get; init; } = 30;
    public int Quality { get; init; } = 72;
    public bool DrawCursor { get; init; } = true;
    public bool IncludeMicrophone { get; init; } = true;
    public string MicrophoneId { get; init; } = string.Empty;
    public string MicrophoneName { get; init; } = string.Empty;
    public bool NoiseSuppression { get; init; }
    public bool AutoLevel { get; init; }
    public bool IncludeCamera { get; init; }
    public string CameraName { get; init; } = string.Empty;
    public string CameraPosition { get; init; } = "bottom-right";
    public int CameraWidth { get; init; } = 360;
    public string CameraShape { get; init; } = "rounded";
    public string CameraBackgroundMode { get; init; } = "none";
    public string CameraBackgroundImagePath { get; init; } = string.Empty;
    public int CameraTouchUpLevel { get; init; }
    public int CameraBrightness { get; init; }
    public int CameraWarmth { get; init; }
    public int CameraContrast { get; init; }
    public int CountdownSeconds { get; init; } = 3;

    public Media.CameraEffectSettings ToCameraEffectSettings() => new()
    {
        BackgroundMode = CameraBackgroundMode,
        BackgroundImagePath = CameraBackgroundImagePath,
        TouchUpLevel = CameraTouchUpLevel,
        Brightness = CameraBrightness,
        Warmth = CameraWarmth,
        Contrast = CameraContrast,
    };
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
