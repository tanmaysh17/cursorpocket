using System.Text.Json;
using CursorPocket.Core.Models;
using CursorPocket.Core.Services;
using CursorPocket.Core.Storage;

namespace CursorPocket.Tests;

public sealed class RemediationPolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CursorPocket.Remediation.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Capture_action_catalog_preserves_all_commands_and_original_mnemonics()
    {
        Assert.Equal(
            [CaptureActionId.Screenshot, CaptureActionId.Video, CaptureActionId.RepeatVideo, CaptureActionId.Audio, CaptureActionId.Text, CaptureActionId.Link, CaptureActionId.Library],
            CaptureActionCatalog.Primary.Select(action => action.Id));
        Assert.Equal(["S", "V", "Shift+V", "A", "T", "L", "O"], CaptureActionCatalog.Primary.Select(action => action.Key));
        Assert.Equal(["R", "W", "D", "A", "P"], CaptureActionCatalog.ScreenshotChoices.Select(action => action.Key));
        Assert.All(CaptureActionCatalog.Primary, action => Assert.True(action.IsPrimary));
    }

    [Fact]
    public void Annotation_catalog_has_sixteen_visible_unique_tools_and_keys()
    {
        Assert.Equal(16, CursorPocket.Core.Annotations.AnnotationToolCatalog.All.Count);
        Assert.Equal(16, CursorPocket.Core.Annotations.AnnotationToolCatalog.All.Select(tool => tool.Tool).Distinct().Count());
        Assert.Equal(16, CursorPocket.Core.Annotations.AnnotationToolCatalog.All.Select(tool => tool.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain(CursorPocket.Core.Annotations.AnnotationTool.Loupe, CursorPocket.Core.Annotations.AnnotationToolCatalog.All.Select(tool => tool.Tool));
    }

    [Theory]
    [InlineData(1920, 1080, 1.25, TransientLayoutMode.Regular)]
    [InlineData(800, 600, 2.5, TransientLayoutMode.Constrained)]
    public void Transient_layout_never_leaves_the_work_area(int width, int height, double scale, TransientLayoutMode expectedMode)
    {
        var work = new CaptureBounds(0, 0, width, height);
        var result = TransientWindowLayoutPolicy.Resolve(work, 360, 354, scale);

        Assert.Equal(expectedMode, result.Mode);
        Assert.InRange(result.Bounds.Left, work.Left, work.Right);
        Assert.InRange(result.Bounds.Top, work.Top, work.Bottom);
        Assert.InRange(result.Bounds.Right, work.Left, work.Right);
        Assert.InRange(result.Bounds.Bottom, work.Top, work.Bottom);
    }

    [Fact]
    public async Task Concurrent_settings_writes_leave_one_valid_document_and_no_temporary_files()
    {
        var path = Path.Combine(_root, "settings", "settings.json");
        var store = new SettingsStore(path);
        await Task.WhenAll(Enumerable.Range(0, 20).Select(index =>
            store.SaveAsync(new AppSettings { ActivationShortcut = $"Win+Alt+{index}" })));

        await using var stream = File.OpenRead(path);
        Assert.NotNull(await JsonSerializer.DeserializeAsync<AppSettings>(stream));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Fact]
    public async Task Settings_load_recovers_the_last_valid_backup()
    {
        var path = Path.Combine(_root, "settings.json");
        var store = new SettingsStore(path);
        await store.SaveAsync(new AppSettings { VideoFramesPerSecond = 30 });
        await store.SaveAsync(new AppSettings { VideoFramesPerSecond = 60 });
        await File.WriteAllTextAsync(path, "{broken");

        var recovered = await store.LoadAsync();

        Assert.Equal(30, recovered.VideoFramesPerSecond);
    }

    [Fact]
    public async Task Capture_transaction_publishes_only_after_a_complete_write()
    {
        var store = new CaptureStore(_root);
        var transaction = new CaptureTransaction(store);

        var result = await transaction.CommitAsync(
            new CaptureTransactionRequest(CaptureKind.Text, ".txt", "transactional note"),
            (path, cancellationToken) => File.WriteAllTextAsync(path, "complete", cancellationToken));

        Assert.True(File.Exists(result.AbsolutePath));
        Assert.Equal("complete", await File.ReadAllTextAsync(result.AbsolutePath));
        Assert.Equal(result.Record.Id, Assert.Single(await store.RecentAsync()).Id);
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(result.AbsolutePath)!, "*.tmp"));
    }

    [Fact]
    public async Task Capture_transaction_rejects_empty_output_without_indexing_it()
    {
        var store = new CaptureStore(_root);
        var transaction = new CaptureTransaction(store);

        await Assert.ThrowsAsync<InvalidDataException>(() => transaction.CommitAsync(
            new CaptureTransactionRequest(CaptureKind.Text, ".txt", "empty"),
            (path, _) => Task.Run(() => File.WriteAllBytes(path, []))));

        Assert.Empty(await store.RecentAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
