namespace CursorPocket.Core.Services;

/// <summary>
/// Human-readable file sizes for the Library, which lists captures ranging from a
/// few hundred bytes of text to multi-gigabyte recordings.
/// </summary>
public static class FileSize
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>Returns an empty string when the file is gone, so a missing capture reads as blank rather than "0 B".</summary>
    public static string Describe(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? Describe(info.Length) : string.Empty;
        }
        catch (Exception)
        {
            // A path that cannot be inspected is not worth failing a list row over.
            return string.Empty;
        }
    }

    public static string Describe(long bytes)
    {
        if (bytes < 0)
        {
            return string.Empty;
        }
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        // One decimal below 10 keeps "1.4 MB" informative without "1.43829 MB" noise.
        return value < 10 ? $"{value:0.#} {Units[unit]}" : $"{Math.Round(value)} {Units[unit]}";
    }
}
