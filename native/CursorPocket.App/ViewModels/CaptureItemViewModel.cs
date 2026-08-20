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

    /// <summary>The file type, shown as a chip so a scan down the list separates kinds.</summary>
    public string KindTag
    {
        get
        {
            var extension = Path.GetExtension(Record.RelativePath);
            if (!string.IsNullOrWhiteSpace(extension))
            {
                return extension.TrimStart('.').ToUpperInvariant();
            }
            return Record.CaptureKind == CaptureKind.Link ? "URL" : "FILE";
        }
    }

    /// <summary>On-disk size, or an em dash when the file is gone or never had one.</summary>
    public string SizeLabel
    {
        get
        {
            try
            {
                var file = new FileInfo(AbsolutePath);
                return file.Exists ? FormatBytes(file.Length) : "—";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return "—";
            }
        }
    }

    /// <summary>Time for today, a weekday for this week, a date beyond that.</summary>
    public string TimeLabel
    {
        get
        {
            if (Record.Created == DateTimeOffset.MinValue)
            {
                return string.Empty;
            }
            var created = Record.Created.LocalDateTime;
            var age = DateTime.Today - created.Date;
            return age.Days switch
            {
                0 => created.ToString("h:mm tt"),
                1 => "Yesterday",
                < 7 => created.ToString("ddd"),
                _ => created.ToString("d MMM"),
            };
        }
    }

    public string CreatedLabel => Record.Created == DateTimeOffset.MinValue
        ? string.Empty
        : Record.Created.LocalDateTime.ToString("MMM d · h:mm tt");

    /// <summary>Kind and file size on one line, so a row stays a single compact strip.</summary>
    public string MetaLabel => FileSize.Describe(AbsolutePath) is { Length: > 0 } size
        ? $"{KindLabel} · {size}"
        : KindLabel;

    public string SavedLabel => Record.Created == DateTimeOffset.MinValue
        ? "Unknown"
        : Record.Created.LocalDateTime.ToString("d MMM yyyy, h:mm tt");
    public string FileName => Path.GetFileName(Record.RelativePath);
    public string DateGroup => Record.Created == DateTimeOffset.MinValue
        ? "Earlier"
        : Record.Created.LocalDateTime.Date == DateTime.Today
            ? "Today"
            : Record.Created.LocalDateTime.Date == DateTime.Today.AddDays(-1)
                ? "Yesterday"
                : Record.Created.LocalDateTime.ToString("dddd, MMMM d");
    public bool IsPlayable => Record.CaptureKind is CaptureKind.Video or CaptureKind.Audio;
    public bool IsImage => Record.CaptureKind == CaptureKind.Screenshot;

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }
        if (bytes < 1024L * 1024)
        {
            return $"{bytes / 1024.0:0.#} KB";
        }
        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024):0.#} MB";
        }
        return $"{bytes / (1024.0 * 1024 * 1024):0.##} GB";
    }
}
