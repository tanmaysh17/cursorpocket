namespace CursorPocket.Core.Models;

public enum CaptureKind
{
    Screenshot,
    Video,
    Audio,
    Text,
    Link,
}

public static class CaptureKindExtensions
{
    public static string ToStorageValue(this CaptureKind kind) => kind switch
    {
        CaptureKind.Screenshot => "screenshot",
        CaptureKind.Video => "video",
        CaptureKind.Audio => "audio",
        CaptureKind.Text => "text",
        CaptureKind.Link => "link",
        _ => kind.ToString().ToLowerInvariant(),
    };

    // Every manifest line runs through this on load, so it avoids the reflection
    // and allocation of Enum.TryParse.
    public static CaptureKind ParseStorageValue(string value) => value switch
    {
        "screenshot" => CaptureKind.Screenshot,
        "video" => CaptureKind.Video,
        "audio" => CaptureKind.Audio,
        "text" => CaptureKind.Text,
        "link" => CaptureKind.Link,
        _ => Enum.TryParse<CaptureKind>(value, true, out var kind) ? kind : CaptureKind.Text,
    };
}
