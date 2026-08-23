using System.Text;
using System.Text.Json;
using CursorPocket.Core.Models;

namespace CursorPocket.Core.Storage;

public sealed class CaptureStore
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // Recoverable media only ever lands in these two per-day folders. Walking the
    // whole tree also visited every screenshot, text file, and cached preview.
    private static readonly (string Category, string Extension)[] MediaCategories =
    [
        ("videos", ".mp4"),
        ("audio", ".wav"),
    ];

    public CaptureStore(string rootDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(RootDirectory);
    }

    public string RootDirectory { get; }
    public string ManifestPath => Path.Combine(RootDirectory, "captures.jsonl");
    public string PreviewDirectory => Path.Combine(RootDirectory, ".cursorpocket", "previews");

    public event EventHandler<CaptureCompletedEventArgs>? CaptureCompleted;

    public async Task<IReadOnlyList<CaptureRecord>> RecentAsync(
        int limit = 250,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0 || !File.Exists(ManifestPath))
        {
            return [];
        }

        string[] lines;
        try
        {
            lines = await ReadRecentLinesAsync(limit, cancellationToken);
        }
        catch (IOException)
        {
            return [];
        }

        var records = new List<CaptureRecord>(Math.Min(limit, lines.Length));
        for (var index = lines.Length - 1; index >= 0 && records.Count < limit; index--)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                continue;
            }

            try
            {
                var record = JsonSerializer.Deserialize<CaptureRecord>(lines[index], JsonOptions);
                if (record is not null && IsSafeRelativePath(record.RelativePath))
                {
                    records.Add(record);
                }
            }
            catch (JsonException)
            {
                // A corrupt line never hides healthy captures around it.
            }
        }
        return records;
    }

    /// <summary>
    /// Reads only as much of the tail of the manifest as the requested record count
    /// can need, so opening the Library stays flat as history grows. A tail read can
    /// begin mid-line; that fragment fails to parse and is skipped like any corrupt
    /// line. An unbounded request still reads the whole file.
    /// </summary>
    private async Task<string[]> ReadRecentLinesAsync(int limit, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            ManifestPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        var wanted = limit == int.MaxValue ? long.MaxValue : Math.Max(64L * 1024, (long)limit * 2048);
        if (wanted < stream.Length)
        {
            stream.Seek(stream.Length - wanted, SeekOrigin.Begin);
        }
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var text = await reader.ReadToEndAsync(cancellationToken);
        return text.Split('\n');
    }

    public async Task<CaptureRecord> SaveTextAsync(string text, CancellationToken cancellationToken = default)
    {
        var value = text.Trim();
        if (value.Length == 0)
        {
            throw new ArgumentException("Text capture is empty.", nameof(text));
        }

        var reservation = Reserve(CaptureKind.Text, ".txt");
        await File.WriteAllTextAsync(reservation.AbsolutePath, value + Environment.NewLine, cancellationToken);
        return await AppendAsync(CreateRecord(reservation, Compact(value), []), cancellationToken);
    }

    public async Task<CaptureRecord> SaveLinkAsync(string url, CancellationToken cancellationToken = default)
    {
        var value = url.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ArgumentException("The active window is not a complete web page.", nameof(url));
        }

        var reservation = Reserve(CaptureKind.Link, ".url");
        await File.WriteAllTextAsync(reservation.AbsolutePath, $"[InternetShortcut]{Environment.NewLine}URL={value}{Environment.NewLine}", cancellationToken);
        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        var metadata = JsonMetadata(("url", value), ("host", host));
        return await AppendAsync(CreateRecord(reservation, Compact(value), metadata), cancellationToken);
    }

    public async Task<CaptureRecord> ImportFileAsync(
        CaptureKind kind,
        string sourcePath,
        string preview,
        IReadOnlyDictionary<string, object?>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("The capture output is missing.", sourcePath);
        }

        var suffix = Path.GetExtension(sourcePath);
        var reservation = Reserve(kind, suffix);
        await using (var input = File.OpenRead(sourcePath))
        await using (var output = File.Create(reservation.AbsolutePath))
        {
            await input.CopyToAsync(output, cancellationToken);
        }
        return await AppendAsync(CreateRecord(reservation, preview, JsonMetadata(metadata)), cancellationToken);
    }

    public async Task<CaptureRecord> RegisterExistingAsync(
        CaptureKind kind,
        string absolutePath,
        string preview,
        IReadOnlyDictionary<string, object?>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(absolutePath);
        EnsureInsideRoot(fullPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The capture output is missing.", fullPath);
        }

        var now = DateTimeOffset.Now;
        var record = new CaptureRecord
        {
            Id = $"{now:yyyyMMddTHHmmss}-{Guid.NewGuid():N}"[..22],
            Kind = kind.ToStorageValue(),
            CreatedAt = now.ToString("O"),
            RelativePath = Path.GetRelativePath(RootDirectory, fullPath).Replace('\\', '/'),
            Preview = Compact(preview),
            Metadata = JsonMetadata(metadata),
        };
        return await AppendAsync(record, cancellationToken);
    }

    public CaptureReservation Reserve(CaptureKind kind, string suffix)
    {
        var now = DateTimeOffset.Now;
        var shortId = Guid.NewGuid().ToString("N")[..6];
        var id = $"{now:yyyyMMddTHHmmss}-{shortId}";
        var category = kind switch
        {
            CaptureKind.Screenshot => "screenshots",
            CaptureKind.Video => "videos",
            CaptureKind.Audio => "audio",
            CaptureKind.Text => "text",
            CaptureKind.Link => "links",
            _ => kind.ToStorageValue(),
        };
        var directory = Path.Combine(RootDirectory, now.ToString("yyyy-MM-dd"), category);
        Directory.CreateDirectory(directory);
        var absolutePath = Path.Combine(directory, $"{now:HH-mm-ss}_{kind.ToStorageValue()}_{shortId}{suffix}");
        return new CaptureReservation(id, kind, now, absolutePath)
        {
            RelativePath = Path.GetRelativePath(RootDirectory, absolutePath).Replace('\\', '/'),
        };
    }

    public string AbsolutePath(CaptureRecord record)
    {
        if (!IsSafeRelativePath(record.RelativePath))
        {
            throw new InvalidDataException("Capture metadata contains an unsafe path.");
        }
        var fullPath = Path.GetFullPath(Path.Combine(RootDirectory, record.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        EnsureInsideRoot(fullPath);
        return fullPath;
    }

    public async Task RemoveFromIndexAsync(string id, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(ManifestPath))
            {
                return;
            }
            var lines = await File.ReadAllLinesAsync(ManifestPath, cancellationToken);
            var retained = lines.Where(line => !RecordHasId(line, id)).ToArray();
            var temporary = ManifestPath + ".tmp";
            await File.WriteAllLinesAsync(temporary, retained, cancellationToken);
            File.Move(temporary, ManifestPath, true);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<IReadOnlyList<CaptureRecord>> RecoverOrphanedMediaAsync(CancellationToken cancellationToken = default)
    {
        var candidates = EnumerateMediaCandidates().OrderBy(File.GetLastWriteTimeUtc).ToList();
        if (candidates.Count == 0)
        {
            return [];
        }

        // Reading the whole manifest is only worth it once something could actually
        // be orphaned.
        var indexed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in await RecentAsync(int.MaxValue, cancellationToken))
        {
            try { indexed.Add(AbsolutePath(record)); } catch (InvalidDataException) { }
        }

        var recovered = new List<CaptureRecord>();
        foreach (var path in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.GetFullPath(path);
            if (indexed.Contains(fullPath))
            {
                continue;
            }
            var kind = Path.GetExtension(fullPath).Equals(".mp4", StringComparison.OrdinalIgnoreCase) ? CaptureKind.Video : CaptureKind.Audio;
            var minimumBytes = kind == CaptureKind.Video ? 1024 : 44;
            if (new FileInfo(fullPath).Length < minimumBytes)
            {
                continue;
            }
            var record = await RegisterExistingAsync(
                kind,
                fullPath,
                kind == CaptureKind.Video ? "Recovered video" : "Recovered audio note",
                new Dictionary<string, object?> { ["recovered"] = true },
                cancellationToken);
            indexed.Add(fullPath);
            recovered.Add(record);
        }
        return recovered;
    }

    private IEnumerable<string> EnumerateMediaCandidates()
    {
        foreach (var dayDirectory in Directory.EnumerateDirectories(RootDirectory))
        {
            // .cursorpocket holds generated previews and in-flight mux files, never
            // captures, and it is by far the largest folder in a busy library.
            if (Path.GetFileName(dayDirectory).StartsWith('.'))
            {
                continue;
            }
            foreach (var (category, extension) in MediaCategories)
            {
                var categoryDirectory = Path.Combine(dayDirectory, category);
                if (!Directory.Exists(categoryDirectory))
                {
                    continue;
                }
                foreach (var path in Directory.EnumerateFiles(categoryDirectory, "*" + extension))
                {
                    yield return path;
                }
            }
        }
    }

    private async Task<CaptureRecord> AppendAsync(CaptureRecord record, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(RootDirectory);
            var line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;
            await File.AppendAllTextAsync(ManifestPath, line, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
        CaptureCompleted?.Invoke(this, new CaptureCompletedEventArgs(record, AbsolutePath(record)));
        return record;
    }

    private static CaptureRecord CreateRecord(
        CaptureReservation reservation,
        string preview,
        Dictionary<string, JsonElement> metadata) => new()
        {
            Id = reservation.Id,
            Kind = reservation.Kind.ToStorageValue(),
            CreatedAt = reservation.CreatedAt.ToString("O"),
            RelativePath = reservation.RelativePath,
            Preview = preview,
            Metadata = metadata,
        };

    private static string Compact(string value, int limit = 96)
    {
        var clean = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= limit ? clean : clean[..(limit - 1)].TrimEnd() + "…";
    }

    private static Dictionary<string, JsonElement> JsonMetadata(params (string Key, object? Value)[] values) =>
        JsonMetadata(values.ToDictionary(pair => pair.Key, pair => pair.Value));

    private static Dictionary<string, JsonElement> JsonMetadata(IReadOnlyDictionary<string, object?>? values)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        if (values is null)
        {
            return result;
        }
        foreach (var pair in values)
        {
            result[pair.Key] = JsonSerializer.SerializeToElement(pair.Value, JsonOptions);
        }
        return result;
    }

    private bool IsSafeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return false;
        }
        var parts = relativePath.Replace('\\', '/').Split('/');
        return !parts.Contains("..", StringComparer.Ordinal);
    }

    private void EnsureInsideRoot(string fullPath)
    {
        var relative = Path.GetRelativePath(RootDirectory, fullPath);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new InvalidDataException("Capture metadata contains an unsafe path.");
        }
    }

    private static bool RecordHasId(string line, string id)
    {
        try
        {
            return JsonSerializer.Deserialize<CaptureRecord>(line, JsonOptions)?.Id == id;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

public sealed record CaptureReservation(
    string Id,
    CaptureKind Kind,
    DateTimeOffset CreatedAt,
    string AbsolutePath)
{
    public string RelativePath { get; internal set; } = string.Empty;
}
