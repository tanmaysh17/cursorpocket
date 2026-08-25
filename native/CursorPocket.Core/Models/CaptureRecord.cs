using System.Text.Json;
using System.Text.Json.Serialization;

namespace CursorPocket.Core.Models;

public sealed record CaptureRecord
{
    [JsonPropertyName("schema_version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int SchemaVersion { get; init; } = 2;

    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("created_at")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("path")]
    public required string RelativePath { get; init; }

    [JsonPropertyName("preview")]
    public required string Preview { get; init; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, JsonElement> Metadata { get; init; } = [];

    // Both of these are read from filter predicates and from several list bindings
    // per row, and DateTimeOffset.TryParse is culture-aware and expensive. Parse
    // each string once per record instead of once per read.
    private CaptureKind? _captureKind;
    private DateTimeOffset? _created;

    [JsonIgnore]
    public CaptureKind CaptureKind => _captureKind ??= CaptureKindExtensions.ParseStorageValue(Kind);

    [JsonIgnore]
    public DateTimeOffset Created => _created ??= DateTimeOffset.TryParse(CreatedAt, out var value)
        ? value
        : DateTimeOffset.MinValue;
}

public sealed record CaptureCompletedEventArgs(CaptureRecord Record, string AbsolutePath);
