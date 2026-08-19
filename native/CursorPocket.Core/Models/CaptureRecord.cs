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

    [JsonIgnore]
    public CaptureKind CaptureKind => CaptureKindExtensions.ParseStorageValue(Kind);

    [JsonIgnore]
    public DateTimeOffset Created => DateTimeOffset.TryParse(CreatedAt, out var value)
        ? value
        : DateTimeOffset.MinValue;
}

public sealed record CaptureCompletedEventArgs(CaptureRecord Record, string AbsolutePath);
