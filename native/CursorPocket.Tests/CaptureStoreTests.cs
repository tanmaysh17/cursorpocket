using System.Text.Json;
using CursorPocket.Core.Models;
using CursorPocket.Core.Storage;

namespace CursorPocket.Tests;

public sealed class CaptureStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CursorPocket.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ReadsExistingPythonManifestWithoutMigration()
    {
        Directory.CreateDirectory(Path.Combine(_root, "2026-08-18", "audio"));
        var line = "{\"id\":\"one\",\"kind\":\"audio\",\"created_at\":\"2026-08-18T12:30:00-07:00\",\"path\":\"2026-08-18/audio/note.wav\",\"preview\":\"Audio · 0:14\",\"metadata\":{\"duration_seconds\":14.2}}";
        await File.WriteAllTextAsync(Path.Combine(_root, "captures.jsonl"), line + Environment.NewLine);

        var records = await new CaptureStore(_root).RecentAsync();

        var record = Assert.Single(records);
        Assert.Equal(CaptureKind.Audio, record.CaptureKind);
        Assert.Equal("2026-08-18/audio/note.wav", record.RelativePath);
        Assert.Equal(14.2, record.Metadata["duration_seconds"].GetDouble(), 1);
    }

    [Fact]
    public async Task SavesEveryTextualTypeInDatedFolders()
    {
        var store = new CaptureStore(_root);

        var text = await store.SaveTextAsync("A useful highlighted thought");
        var link = await store.SaveLinkAsync("https://www.example.com/path");

        Assert.True(File.Exists(store.AbsolutePath(text)));
        Assert.True(File.Exists(store.AbsolutePath(link)));
        Assert.Contains("/text/", text.RelativePath);
        Assert.Contains("/links/", link.RelativePath);
        Assert.Equal("example.com", link.Metadata["host"].GetString());
        Assert.Equal([link.Id, text.Id], (await store.RecentAsync()).Select(record => record.Id));
    }

    [Fact]
    public async Task UpdatesTextContentAndManifestInPlace()
    {
        var store = new CaptureStore(_root);
        var first = await store.SaveTextAsync("First");
        var original = await store.SaveTextAsync("Before");
        var last = await store.SaveTextAsync("Last");
        var longText = $"  After editing{Environment.NewLine}inside the Library {new string('x', 120)}  ";

        var updated = await store.UpdateTextAsync(original, longText);

        Assert.Equal(original.Id, updated.Id);
        Assert.Equal(original.RelativePath, updated.RelativePath);
        Assert.Equal(96, updated.Preview.Length);
        Assert.StartsWith("After editing inside the Library", updated.Preview, StringComparison.Ordinal);
        Assert.Equal(longText.Trim(), (await File.ReadAllTextAsync(store.AbsolutePath(updated))).Trim());
        var records = await store.RecentAsync();
        Assert.Equal([last.Id, original.Id, first.Id], records.Select(record => record.Id));
        var indexed = Assert.Single(records, record => record.Id == original.Id);
        Assert.Equal(updated.Id, indexed.Id);
        Assert.Equal(updated.RelativePath, indexed.RelativePath);
        Assert.Equal(updated.Preview, indexed.Preview);
    }

    [Fact]
    public async Task RejectsInvalidTextUpdatesWithoutChangingTheCapture()
    {
        var store = new CaptureStore(_root);
        var original = await store.SaveTextAsync("Original");
        var link = await store.SaveLinkAsync("https://example.com");
        var contentBefore = await File.ReadAllTextAsync(store.AbsolutePath(original));
        var manifestBefore = await File.ReadAllTextAsync(store.ManifestPath);

        await Assert.ThrowsAsync<ArgumentException>(() => store.UpdateTextAsync(original, "  \r\n  "));
        await Assert.ThrowsAsync<ArgumentException>(() => store.UpdateTextAsync(link, "not a link edit"));

        Assert.Equal(contentBefore, await File.ReadAllTextAsync(store.AbsolutePath(original)));
        Assert.Equal(manifestBefore, await File.ReadAllTextAsync(store.ManifestPath));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.bak", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task RollsBackTextWhenManifestReplacementFails()
    {
        var store = new CaptureStore(_root);
        var original = await store.SaveTextAsync("Original");
        var contentBefore = await File.ReadAllTextAsync(store.AbsolutePath(original));
        var manifestBefore = await File.ReadAllTextAsync(store.ManifestPath);

        await using (File.Open(store.ManifestPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => store.UpdateTextAsync(original, "Should roll back"));
        }

        Assert.Equal(contentBefore, await File.ReadAllTextAsync(store.AbsolutePath(original)));
        Assert.Equal(manifestBefore, await File.ReadAllTextAsync(store.ManifestPath));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.bak", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task RejectsMissingTextFilesAndIndexRows()
    {
        var store = new CaptureStore(_root);
        var original = await store.SaveTextAsync("Original");
        var manifestBefore = await File.ReadAllTextAsync(store.ManifestPath);
        File.Delete(store.AbsolutePath(original));

        await Assert.ThrowsAsync<FileNotFoundException>(() => store.UpdateTextAsync(original, "After"));

        await File.WriteAllTextAsync(store.AbsolutePath(original), "Original" + Environment.NewLine);
        File.Delete(store.ManifestPath);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.UpdateTextAsync(original, "After"));

        await File.WriteAllTextAsync(store.ManifestPath, manifestBefore);
        await store.RemoveFromIndexAsync(original.Id);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.UpdateTextAsync(original, "After"));
        Assert.Equal("Original", (await File.ReadAllTextAsync(store.AbsolutePath(original))).Trim());
        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.bak", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task SkipsCorruptAndUnsafeManifestRows()
    {
        Directory.CreateDirectory(_root);
        var safe = JsonSerializer.Serialize(new CaptureRecord
        {
            Id = "safe",
            Kind = "text",
            CreatedAt = DateTimeOffset.Now.ToString("O"),
            RelativePath = "2026-08-18/text/safe.txt",
            Preview = "safe",
        });
        var unsafeRow = JsonSerializer.Serialize(new CaptureRecord
        {
            Id = "unsafe",
            Kind = "text",
            CreatedAt = DateTimeOffset.Now.ToString("O"),
            RelativePath = "../../outside.txt",
            Preview = "unsafe",
        });
        await File.WriteAllLinesAsync(Path.Combine(_root, "captures.jsonl"), ["{bad", unsafeRow, safe]);

        var records = await new CaptureStore(_root).RecentAsync();

        Assert.Equal("safe", Assert.Single(records).Id);
    }

    [Fact]
    public async Task RemovalRewritesOnlyTheIndex()
    {
        var store = new CaptureStore(_root);
        var first = await store.SaveTextAsync("first");
        var second = await store.SaveTextAsync("second");

        await store.RemoveFromIndexAsync(first.Id);

        Assert.Equal(second.Id, Assert.Single(await store.RecentAsync()).Id);
        Assert.True(File.Exists(store.AbsolutePath(first)));
    }

    [Fact]
    public async Task RecoversUnindexedInterruptedMediaExactlyOnce()
    {
        var videoDirectory = Path.Combine(_root, "2026-08-18", "videos");
        Directory.CreateDirectory(videoDirectory);
        var orphan = Path.Combine(videoDirectory, "12-00-00_video_orphan.mp4");
        await File.WriteAllBytesAsync(orphan, new byte[2048]);
        var store = new CaptureStore(_root);

        var firstPass = await store.RecoverOrphanedMediaAsync();
        var secondPass = await store.RecoverOrphanedMediaAsync();

        var record = Assert.Single(firstPass);
        Assert.Equal(CaptureKind.Video, record.CaptureKind);
        Assert.True(record.Metadata["recovered"].GetBoolean());
        Assert.Empty(secondPass);
        Assert.Single(await store.RecentAsync());
    }

    [Fact]
    public async Task Reconciles_unindexed_non_media_capture_files()
    {
        var screenshotDirectory = Path.Combine(_root, "2026-08-18", "screenshots");
        var textDirectory = Path.Combine(_root, "2026-08-18", "text");
        Directory.CreateDirectory(screenshotDirectory);
        Directory.CreateDirectory(textDirectory);
        await File.WriteAllBytesAsync(Path.Combine(screenshotDirectory, "shot.png"), new byte[32]);
        await File.WriteAllTextAsync(Path.Combine(textDirectory, "note.txt"), "saved note");

        var recovered = await new CaptureStore(_root).ReconcileUnindexedCapturesAsync();

        Assert.Equal(2, recovered.Count);
        Assert.Contains(recovered, record => record.CaptureKind == CaptureKind.Screenshot);
        Assert.Contains(recovered, record => record.CaptureKind == CaptureKind.Text);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
