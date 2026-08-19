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
    public static string ToStorageValue(this CaptureKind kind) => kind.ToString().ToLowerInvariant();

    public static CaptureKind ParseStorageValue(string value) =>
        Enum.TryParse<CaptureKind>(value, true, out var kind) ? kind : CaptureKind.Text;
}
