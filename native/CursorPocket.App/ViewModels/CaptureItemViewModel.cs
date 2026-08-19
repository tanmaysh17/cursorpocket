using CursorPocket.Core.Models;

namespace CursorPocket_App.ViewModels;

public sealed class CaptureItemViewModel(CaptureRecord record, string absolutePath)
{
    public CaptureRecord Record { get; } = record;
    public string AbsolutePath { get; } = absolutePath;
    public string Id => Record.Id;
    public string Preview => Record.Preview;
    public string KindLabel => Record.CaptureKind switch
    {
        CaptureKind.Screenshot => "Screenshot",
        CaptureKind.Video => "Video",
        CaptureKind.Audio => "Audio note",
        CaptureKind.Text => "Text snippet",
        CaptureKind.Link => "Web link",
        _ => "Capture",
    };
    public string IconGlyph => Record.CaptureKind switch
    {
        CaptureKind.Screenshot => "\uE91B",
        CaptureKind.Video => "\uE714",
        CaptureKind.Audio => "\uE720",
        CaptureKind.Text => "\uE8C1",
        CaptureKind.Link => "\uE71B",
        _ => "\uE7C3",
    };
    public string CreatedLabel => Record.Created == DateTimeOffset.MinValue
        ? string.Empty
        : Record.Created.LocalDateTime.ToString("MMM d · h:mm tt");
    public string DateGroup => Record.Created == DateTimeOffset.MinValue
        ? "Earlier"
        : Record.Created.LocalDateTime.Date == DateTime.Today
            ? "Today"
            : Record.Created.LocalDateTime.Date == DateTime.Today.AddDays(-1)
                ? "Yesterday"
                : Record.Created.LocalDateTime.ToString("dddd, MMMM d");
    public bool IsPlayable => Record.CaptureKind is CaptureKind.Video or CaptureKind.Audio;
    public bool IsImage => Record.CaptureKind == CaptureKind.Screenshot;
}
