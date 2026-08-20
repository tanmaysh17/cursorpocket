using CommunityToolkit.Mvvm.ComponentModel;
using CursorPocket.Core.Models;
using CursorPocket.Core.Services;
using Microsoft.UI.Xaml.Media;

namespace CursorPocket_App.ViewModels;

public sealed partial class CaptureItemViewModel(CaptureRecord record, string absolutePath) : ObservableObject
{
    public CaptureRecord Record { get; } = record;
    public string AbsolutePath { get; } = absolutePath;
    public string Id => Record.Id;
    public string Preview => Record.Preview;

    /// <summary>
    /// The real screenshot, video frame, or waveform for this capture. Filled in after
    /// the list appears so a folder of large captures does not delay it.
    /// </summary>
    [ObservableProperty] private ImageSource? _thumbnail;

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
        CaptureKind.Screenshot => "",
        CaptureKind.Video => "",
        CaptureKind.Audio => "",
        CaptureKind.Text => "",
        CaptureKind.Link => "",
        _ => "",
    };
    public string CreatedLabel => Record.Created == DateTimeOffset.MinValue
        ? string.Empty
        : Record.Created.LocalDateTime.ToString("MMM d · h:mm tt");

    /// <summary>Kind and file size on one line, so a row stays a single compact strip.</summary>
    public string MetaLabel => FileSize.Describe(AbsolutePath) is { Length: > 0 } size
        ? $"{KindLabel} · {size}"
        : KindLabel;

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
