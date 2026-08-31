using System.Text.RegularExpressions;

namespace CursorPocket.Core.Feedback;

/// <summary>Extracts and redacts the one crash record a user may choose to include.</summary>
internal static partial class FeedbackDiagnostics
{
    public const int MaximumExcerptLength = 2_000;

    public static FeedbackDiagnosticExcerpt? FromCrashLog(
        string? logText,
        IEnumerable<string>? sensitiveValues = null)
    {
        if (string.IsNullOrWhiteSpace(logText))
        {
            return null;
        }

        var entries = CrashEntryStart().Matches(logText);
        if (entries.Count == 0)
        {
            return null;
        }

        var text = logText[entries[entries.Count - 1].Index..].Trim();
        // Remove any complete Windows or UNC path before replacing individual
        // names. That prevents a capture filename from surviving after its parent
        // capture directory has been replaced.
        text = RootedWindowsPath().Replace(text, "<private path>");
        text = CaptureFileName().Replace(text, "<capture name>");
        foreach (var value in (sensitiveValues ?? [])
                     .Where(value => !string.IsNullOrWhiteSpace(value) && value.Trim().Length >= 3)
                     .Select(value => value.Trim())
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(value => value.Length))
        {
            text = Regex.Replace(
                text,
                Regex.Escape(value),
                "<private>",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        text = ExcessBlankLines().Replace(text, $"{Environment.NewLine}{Environment.NewLine}");

        if (text.Length > MaximumExcerptLength)
        {
            text = text[..(MaximumExcerptLength - 16)].TrimEnd() + "\n… (truncated)";
        }
        return text.Length == 0 ? null : new FeedbackDiagnosticExcerpt(text);
    }

    [GeneratedRegex(@"(?m)^\[\d{4}-\d{2}-\d{2}T[^\]\r\n]+\]\s")]
    private static partial Regex CrashEntryStart();

    [GeneratedRegex(@"(?i)(?:[a-z]:\\|\\\\)[^\r\n]*")]
    private static partial Regex RootedWindowsPath();

    [GeneratedRegex(@"(?i)\.?\d{8}T\d{6}-[a-f0-9]{6,32}(?:\.[a-z0-9]+)?")]
    private static partial Regex CaptureFileName();

    [GeneratedRegex(@"(?:\r?\n){3,}")]
    private static partial Regex ExcessBlankLines();
}
