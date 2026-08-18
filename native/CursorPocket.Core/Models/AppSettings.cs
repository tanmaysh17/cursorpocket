using System.Text.Json.Serialization;

namespace CursorPocket.Core.Models;

public sealed record AppSettings
{
    [JsonPropertyName("capture_dir")]
    public string CaptureDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "CursorPocket Captures");

    [JsonPropertyName("follow_cursor")]
    public bool FollowCursor { get; init; } = true;

    [JsonPropertyName("cursor_companion_mode")]
    public string CursorCompanionMode { get; init; } = "while-moving";

    [JsonPropertyName("mouse_gesture_enabled")]
    public bool MouseGestureEnabled { get; init; } = true;

    [JsonPropertyName("onboarding_seen")]
    public bool OnboardingSeen { get; init; }

    [JsonPropertyName("panel_geometry")]
    public string PanelGeometry { get; init; } = string.Empty;

    [JsonPropertyName("activation_shortcut")]
    public string ActivationShortcut { get; init; } = "Ctrl+Shift+Space";

    [JsonPropertyName("video_microphone_enabled")]
    public bool VideoMicrophoneEnabled { get; init; } = true;

    [JsonPropertyName("video_camera_enabled")]
    public bool VideoCameraEnabled { get; init; }

    [JsonPropertyName("video_microphone_name")]
    public string VideoMicrophoneName { get; init; } = string.Empty;

    [JsonPropertyName("video_camera_name")]
    public string VideoCameraName { get; init; } = string.Empty;

    [JsonPropertyName("video_source_kind")]
    public string VideoSourceKind { get; init; } = "display";

    [JsonPropertyName("video_camera_position")]
    public string VideoCameraPosition { get; init; } = "bottom-right";

    [JsonPropertyName("video_camera_width")]
    public int VideoCameraWidth { get; init; } = 360;

    [JsonPropertyName("video_fps")]
    public int VideoFramesPerSecond { get; init; } = 30;

    [JsonPropertyName("video_countdown_seconds")]
    public int VideoCountdownSeconds { get; init; } = 3;

    [JsonPropertyName("video_draw_cursor")]
    public bool VideoDrawCursor { get; init; } = true;

    [JsonPropertyName("start_with_windows")]
    public bool StartWithWindows { get; init; }

    [JsonPropertyName("library_window_geometry")]
    public string LibraryWindowGeometry { get; init; } = string.Empty;
}
