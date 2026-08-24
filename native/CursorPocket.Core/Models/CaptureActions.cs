namespace CursorPocket.Core.Models;

public enum CaptureActionId
{
    Screenshot,
    Video,
    Audio,
    Library,
    Region,
    Window,
    Display,
    AllDisplays,
    PreviousRegion,
    RepeatVideo,
    Text,
    Link,
}

public sealed record CaptureActionDescriptor(
    CaptureActionId Id,
    string Title,
    string Description,
    string Key,
    string Glyph,
    bool IsPrimary = false);

public static class CaptureActionCatalog
{
    public static IReadOnlyList<CaptureActionDescriptor> Primary { get; } =
    [
        new(CaptureActionId.Screenshot, "Screenshot", "Choose a region, window, or display", "S", "\uE91B", true),
        new(CaptureActionId.Video, "Video", "Record a display, window, or region", "V", "\uE714", true),
        new(CaptureActionId.RepeatVideo, "Repeat video", "Record again with the previous video settings", "Shift+V", "\uE777", true),
        new(CaptureActionId.Audio, "Audio note", "Record from the selected microphone", "A", "\uE720", true),
        new(CaptureActionId.Text, "Highlighted text", "Save the text selected in the previous window", "T", "\uE8C1", true),
        new(CaptureActionId.Link, "Current link", "Save the active browser page", "L", "\uE71B", true),
        new(CaptureActionId.Library, "Library", "Open saved captures", "O", "\uE8B9", true),
    ];

    public static IReadOnlyList<CaptureActionDescriptor> ScreenshotChoices { get; } =
    [
        new(CaptureActionId.Region, "Region", "Draw a capture area", "R", "\uE7C4"),
        new(CaptureActionId.Window, "Window", "Capture the active window", "W", "\uE8A7"),
        new(CaptureActionId.Display, "Display", "Capture the current display", "D", "\uE7F4"),
        new(CaptureActionId.AllDisplays, "All displays", "Capture the full desktop", "A", "\uE8B1"),
        new(CaptureActionId.PreviousRegion, "Previous region", "Repeat the last region", "P", "\uE72C"),
    ];

    public static CaptureActionDescriptor Get(CaptureActionId id) =>
        Primary.Concat(ScreenshotChoices).First(action => action.Id == id);
}
