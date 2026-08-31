using CursorPocket.Core.Feedback;

namespace CursorPocket.Tests;

public sealed class FeedbackIssueComposerTests
{
    private static readonly FeedbackSystemContext Context = new(
        "0.5.0",
        "Microsoft Windows 10.0.26100",
        "X64",
        150);

    [Theory]
    [InlineData(0, "[Feedback]")]
    [InlineData(1, "[Idea]")]
    [InlineData(2, "[Problem]")]
    public void Category_only_changes_the_issue_title(int categoryValue, string prefix)
    {
        var category = (FeedbackCategory)categoryValue;
        var issue = FeedbackIssueComposer.Compose(new FeedbackDraft(category, "A clearer capture flow", Context));

        Assert.StartsWith(prefix, issue.Title, StringComparison.Ordinal);
        Assert.StartsWith("A clearer capture flow", issue.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(prefix, issue.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Feedback_is_required_and_bounded()
    {
        Assert.Throws<ArgumentException>(() => FeedbackIssueComposer.Compose(
            new FeedbackDraft(FeedbackCategory.General, " \r\n ", Context)));
        Assert.Throws<ArgumentException>(() => FeedbackIssueComposer.Compose(
            new FeedbackDraft(
                FeedbackCategory.General,
                new string('x', FeedbackIssueComposer.MaximumMessageLength + 1),
                Context)));
    }

    [Fact]
    public void Issue_uri_preserves_unicode_newlines_and_reserved_characters()
    {
        const string message = "Love the orbit 🟢\nPlease keep A&B = easy.";
        var issue = FeedbackIssueComposer.Compose(new FeedbackDraft(FeedbackCategory.Idea, message, Context));

        var body = QueryValue(issue.IssueUri, "body");
        var title = QueryValue(issue.IssueUri, "title");
        Assert.StartsWith("[Idea] Love the orbit 🟢", title, StringComparison.Ordinal);
        Assert.StartsWith(message, body, StringComparison.Ordinal);
        Assert.Equal("https", issue.IssueUri.Scheme);
        Assert.Equal("github.com", issue.IssueUri.Host);
        Assert.Equal("/tanmaysh17/cursorpocket/issues/new", issue.IssueUri.AbsolutePath);
    }

    [Fact]
    public void Long_titles_are_truncated_without_splitting_unicode()
    {
        var message = string.Concat(Enumerable.Repeat("capture 🟢 ", 30));
        var issue = FeedbackIssueComposer.Compose(new FeedbackDraft(FeedbackCategory.General, message, Context));

        Assert.True(issue.Title.Length <= FeedbackIssueComposer.MaximumTitleLength);
        Assert.EndsWith("…", issue.Title, StringComparison.Ordinal);
        Assert.False(char.IsHighSurrogate(issue.Title[^1]));
    }

    [Fact]
    public void Safe_system_details_are_always_included()
    {
        var issue = FeedbackIssueComposer.Compose(new FeedbackDraft(FeedbackCategory.General, "Small note", Context));

        Assert.Contains("CursorPocket: 0.5.0", issue.Details, StringComparison.Ordinal);
        Assert.Contains("Windows: Microsoft Windows 10.0.26100", issue.Details, StringComparison.Ordinal);
        Assert.Contains("Architecture: X64", issue.Details, StringComparison.Ordinal);
        Assert.Contains("Display scale: 150%", issue.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("Recent crash details", issue.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void Crash_details_are_only_added_when_the_user_includes_them()
    {
        var crash = new FeedbackDiagnosticExcerpt("System.InvalidOperationException: stopped");
        var issue = FeedbackIssueComposer.Compose(
            new FeedbackDraft(FeedbackCategory.Problem, "Recording stopped", Context, crash));

        Assert.Contains("Recent crash details (included by user)", issue.Details, StringComparison.Ordinal);
        Assert.Contains(crash.Text, issue.Details, StringComparison.Ordinal);
        Assert.Contains(issue.Details, issue.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Diagnostics_keep_only_the_latest_entry_and_remove_private_values()
    {
        var log = """
            [2026-08-29T09:00:00.0000000-07:00] System.Exception: old

            [2026-08-30T09:00:00.0000000-07:00] System.IO.IOException: C:\Users\Taylor\Captures\private-shot.png
               at CursorPocket.Save(String device) in C:\Users\Taylor\source\Save.cs:line 42
            Camera Model 9000
            20260830T090000-9f8e7d6c.png
            captures.jsonl
            settings.json
            """;

        var excerpt = FeedbackDiagnostics.FromCrashLog(
            log,
            ["Taylor", "Camera Model 9000", "captures.jsonl", "settings.json"]);

        Assert.NotNull(excerpt);
        Assert.DoesNotContain("old", excerpt.Text, StringComparison.Ordinal);
        Assert.Contains("<private path>", excerpt.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Taylor", excerpt.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-shot.png", excerpt.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Camera Model 9000", excerpt.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("20260830T090000-9f8e7d6c.png", excerpt.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<capture name>", excerpt.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("captures.jsonl", excerpt.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("settings.json", excerpt.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Diagnostics_ignore_unusable_logs_and_truncate_long_entries()
    {
        Assert.Null(FeedbackDiagnostics.FromCrashLog("not a CursorPocket crash record"));

        var log = $"[2026-08-30T09:00:00.0000000-07:00] System.Exception: {new string('x', 4_000)}";
        var excerpt = FeedbackDiagnostics.FromCrashLog(log);

        Assert.NotNull(excerpt);
        Assert.True(excerpt.Text.Length <= FeedbackDiagnostics.MaximumExcerptLength);
        Assert.EndsWith("… (truncated)", excerpt.Text, StringComparison.Ordinal);
    }

    private static string QueryValue(Uri uri, string name)
    {
        var pair = uri.Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Single(value => value.StartsWith(name + "=", StringComparison.Ordinal));
        return Uri.UnescapeDataString(pair[(name.Length + 1)..]);
    }
}
