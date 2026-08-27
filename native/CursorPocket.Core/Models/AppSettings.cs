using System.Text.Json;
using System.Text.Json.Serialization;
using CursorPocket.Core.Services;

namespace CursorPocket.Core.Models;

public enum AppThemeMode
{
    System,
    Light,
    Dark,
}

public enum GlassTransparencyLevel
{
    Clear,
    Balanced,
    Solid,
}

public enum MouseGestureSensitivity
{
    Low,
    Balanced,
    High,
}

public sealed class MouseGestureSensitivityJsonConverter : JsonConverter<MouseGestureSensitivity>
{
    public override MouseGestureSensitivity Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String &&
            Enum.TryParse<MouseGestureSensitivity>(reader.GetString(), ignoreCase: true, out var named) &&
            Enum.IsDefined(named))
        {
            return named;
        }
        if (reader.TokenType == JsonTokenType.Number &&
            reader.TryGetInt32(out var numeric) &&
            Enum.IsDefined((MouseGestureSensitivity)numeric))
        {
            return (MouseGestureSensitivity)numeric;
        }
        return MouseGestureSensitivity.Balanced;
    }

    public override void Write(
        Utf8JsonWriter writer,
        MouseGestureSensitivity value,
        JsonSerializerOptions options) => writer.WriteStringValue(
            Enum.IsDefined(value) ? value.ToString() : MouseGestureSensitivity.Balanced.ToString());
}

public sealed class GlassTransparencyLevelJsonConverter : JsonConverter<GlassTransparencyLevel>
{
    public override GlassTransparencyLevel Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String &&
            Enum.TryParse<GlassTransparencyLevel>(reader.GetString(), ignoreCase: true, out var named) &&
            Enum.IsDefined(named))
        {
            return named;
        }
        if (reader.TokenType == JsonTokenType.Number &&
            reader.TryGetInt32(out var numeric) &&
            Enum.IsDefined((GlassTransparencyLevel)numeric))
        {
            return (GlassTransparencyLevel)numeric;
        }
        return GlassTransparencyLevel.Balanced;
    }

    public override void Write(
        Utf8JsonWriter writer,
        GlassTransparencyLevel value,
        JsonSerializerOptions options) => writer.WriteStringValue(
            Enum.IsDefined(value) ? value.ToString() : GlassTransparencyLevel.Balanced.ToString());
}

public sealed record AppSettings
{
    [JsonPropertyName("theme_mode")]
    [JsonConverter(typeof(JsonStringEnumConverter<AppThemeMode>))]
    public AppThemeMode ThemeMode { get; init; } = AppThemeMode.System;

    [JsonPropertyName("glass_transparency")]
    [JsonConverter(typeof(GlassTransparencyLevelJsonConverter))]
    public GlassTransparencyLevel GlassTransparency { get; init; } = GlassTransparencyLevel.Balanced;

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

    [JsonPropertyName("mouse_gesture_sensitivity")]
    [JsonConverter(typeof(MouseGestureSensitivityJsonConverter))]
    public MouseGestureSensitivity MouseGestureSensitivity { get; init; } = MouseGestureSensitivity.Balanced;

    /// <summary>Hold both mouse buttons together to open command mode.</summary>
    [JsonPropertyName("mouse_chord_enabled")]
    public bool MouseChordEnabled { get; init; } = true;

    [JsonPropertyName("onboarding_seen")]
    public bool OnboardingSeen { get; init; }

    [JsonPropertyName("onboarding_version")]
    public int OnboardingVersion { get; init; }

    [JsonPropertyName("automatically_check_for_updates")]
    public bool AutomaticallyCheckForUpdates { get; init; } = true;

    [JsonPropertyName("last_update_check_at")]
    public DateTimeOffset? LastUpdateCheckAt { get; init; }

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

    [JsonPropertyName("video_camera_shape")]
    public string VideoCameraShape { get; init; } = "rounded";

    [JsonPropertyName("video_camera_background")]
    public string VideoCameraBackground { get; init; } = "none";

    [JsonPropertyName("video_camera_background_image")]
    public string VideoCameraBackgroundImage { get; init; } = string.Empty;

    [JsonPropertyName("video_camera_touch_up")]
    public int VideoCameraTouchUp { get; init; }

    [JsonPropertyName("video_camera_brightness")]
    public int VideoCameraBrightness { get; init; }

    [JsonPropertyName("video_camera_warmth")]
    public int VideoCameraWarmth { get; init; }

    [JsonPropertyName("video_camera_contrast")]
    public int VideoCameraContrast { get; init; }

    [JsonPropertyName("audio_noise_suppression")]
    public bool AudioNoiseSuppression { get; init; }

    [JsonPropertyName("audio_auto_level")]
    public bool AudioAutoLevel { get; init; }

    [JsonPropertyName("video_fps")]
    public int VideoFramesPerSecond { get; init; } = 30;

    [JsonPropertyName("video_countdown_seconds")]
    public int VideoCountdownSeconds { get; init; }

    [JsonPropertyName("video_draw_cursor")]
    public bool VideoDrawCursor { get; init; } = true;

    [JsonPropertyName("start_with_windows")]
    public bool StartWithWindows { get; init; }

    [JsonPropertyName("library_window_geometry")]
    public string LibraryWindowGeometry { get; init; } = string.Empty;

    // Where the user dragged command mode to, as a fraction of the free space on
    // the display it is shown on. See CommandPanelPlacement.
    [JsonPropertyName("command_panel_anchor_x")]
    public double CommandPanelAnchorX { get; init; } = CommandPanelPlacement.DefaultAnchorX;

    [JsonPropertyName("command_panel_anchor_y")]
    public double CommandPanelAnchorY { get; init; } = CommandPanelPlacement.DefaultAnchorY;
}
