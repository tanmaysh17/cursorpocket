using System.Globalization;
using System.Text;

namespace CursorPocket.Core.Feedback;

internal enum FeedbackCategory
{
    General,
    Idea,
    Problem,
}

internal sealed record FeedbackSystemContext(
    string AppVersion,
    string WindowsVersion,
    string ProcessArchitecture,
    int DisplayScalePercent);

internal sealed record FeedbackDiagnosticExcerpt(string Text);

internal sealed record FeedbackDraft(
    FeedbackCategory Category,
    string Message,
    FeedbackSystemContext SystemContext,
    FeedbackDiagnosticExcerpt? RecentCrashDetails = null);

internal sealed record FeedbackIssueDocument(
    string Title,
    string Body,
    string Details,
    Uri IssueUri)
{
    public string ClipboardText => $"{Title}{Environment.NewLine}{Environment.NewLine}{Body}";
}

/// <summary>
/// Produces the exact public GitHub issue draft that the app shows to the user.
/// It owns no network client and cannot submit an issue.
/// </summary>
internal static class FeedbackIssueComposer
{
    public const int MaximumMessageLength = 2_000;
    public const int MaximumTitleLength = 120;
    public static readonly Uri NewIssueEndpoint = new("https://github.com/tanmaysh17/cursorpocket/issues/new");

    public static FeedbackIssueDocument Compose(FeedbackDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(draft.SystemContext);

        var message = NormalizeMessage(draft.Message);
        var title = BuildTitle(draft.Category, message);
        var details = BuildDetails(draft.SystemContext, draft.RecentCrashDetails);
        var body = $"{message}\n\n---\n\n{details}";
        var issueUri = new Uri(
            $"{NewIssueEndpoint}?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}");
        return new FeedbackIssueDocument(title, body, details, issueUri);
    }

    public static string BuildDetails(
        FeedbackSystemContext context,
        FeedbackDiagnosticExcerpt? recentCrashDetails = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var builder = new StringBuilder()
            .AppendLine("### System details")
            .Append("- CursorPocket: ").AppendLine(context.AppVersion)
            .Append("- Windows: ").AppendLine(context.WindowsVersion)
            .Append("- Architecture: ").AppendLine(context.ProcessArchitecture)
            .Append("- Display scale: ").Append(context.DisplayScalePercent).Append('%');

        if (recentCrashDetails is { Text.Length: > 0 })
        {
            builder
                .AppendLine()
                .AppendLine()
                .AppendLine("<details>")
                .AppendLine("<summary>Recent crash details (included by user)</summary>")
                .AppendLine()
                .AppendLine("```text")
                .AppendLine(recentCrashDetails.Text.Replace("```", "'''", StringComparison.Ordinal))
                .AppendLine("```")
                .Append("</details>");
        }

        return builder.ToString();
    }

    private static string NormalizeMessage(string? message)
    {
        var normalized = (message ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Feedback cannot be empty.", nameof(message));
        }
        if (normalized.Length > MaximumMessageLength)
        {
            throw new ArgumentException(
                $"Feedback cannot be longer than {MaximumMessageLength} characters.",
                nameof(message));
        }
        return normalized;
    }

    private static string BuildTitle(FeedbackCategory category, string message)
    {
        var prefix = category switch
        {
            FeedbackCategory.Idea => "[Idea] ",
            FeedbackCategory.Problem => "[Problem] ",
            _ => "[Feedback] ",
        };
        var firstLine = message
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .First();
        var available = MaximumTitleLength - prefix.Length;
        return prefix + TruncateTextElements(firstLine, available);
    }

    private static string TruncateTextElements(string value, int maximumLength)
    {
        if (value.Length <= maximumLength)
        {
            return value;
        }
        const string suffix = "…";
        var elementStarts = StringInfo.ParseCombiningCharacters(value);
        var allowed = Math.Max(1, maximumLength - suffix.Length);
        var lastElement = Array.FindLastIndex(elementStarts, start => start <= allowed);
        var length = lastElement < 0 ? allowed : elementStarts[lastElement];
        return value[..Math.Max(1, length)].TrimEnd() + suffix;
    }
}
