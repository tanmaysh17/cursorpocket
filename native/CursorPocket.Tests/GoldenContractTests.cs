using System.Text.Json;
using CursorPocket.Core.Models;

namespace CursorPocket.Tests;

// The macOS test suite (GoldenContractTests.swift) reads the exact same
// files in spec/capture-manifest/, so either platform drifting from the
// shared storage contract breaks a CI.
public sealed class GoldenContractTests
{
    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static string[] GoldenLines() =>
        File.ReadAllLines(FixturePath("golden.jsonl"))
            .Where(line => line.Length > 0)
            .ToArray();

    private static CaptureRecord Deserialize(string line) =>
        JsonSerializer.Deserialize<CaptureRecord>(line)
            ?? throw new InvalidOperationException($"Golden line deserialized to null: {line}");

    [Fact]
    public void GoldenManifestCoversEveryKindAndDeserializesWithTheRealRecord()
    {
        var lines = GoldenLines();
        Assert.Equal(5, lines.Length);
        var records = lines.Select(Deserialize).ToArray();

        Assert.Equal(
            [CaptureKind.Screenshot, CaptureKind.Video, CaptureKind.Audio, CaptureKind.Text, CaptureKind.Link],
            records.Select(record => record.CaptureKind));

        foreach (var record in records)
        {
            Assert.Equal(2, record.SchemaVersion);
            Assert.False(string.IsNullOrEmpty(record.Id));
            Assert.False(string.IsNullOrEmpty(record.Preview));
            // Forward-slash relative paths under a dated folder — never
            // backslashes, so both platforms resolve the same file.
            Assert.DoesNotContain('\\', record.RelativePath);
            Assert.StartsWith("2026-08-18/", record.RelativePath, StringComparison.Ordinal);
            // .NET "O" timestamps must parse to a real instant.
            Assert.NotEqual(DateTimeOffset.MinValue, record.Created);
        }
    }

    [Fact]
    public void GoldenMetadataValueShapesSurvive()
    {
        var records = GoldenLines().Select(Deserialize).ToArray();

        Assert.Equal(1920, records[0].Metadata["width"].GetInt32());
        Assert.Equal(1080, records[0].Metadata["height"].GetInt32());
        Assert.False(records[0].Metadata["recovered"].GetBoolean());
        Assert.Equal(42.5, records[1].Metadata["duration_seconds"].GetDouble(), 3);
        Assert.True(records[1].Metadata["recovered"].GetBoolean());
        Assert.Equal(14.2, records[2].Metadata["duration_seconds"].GetDouble(), 3);
        Assert.Empty(records[3].Metadata);
        Assert.Equal("https://www.example.com/docs/page", records[4].Metadata["url"].GetString());
        Assert.Equal("example.com", records[4].Metadata["host"].GetString());
    }

    [Fact]
    public void ReserializingKeepsTheExactWireKeySet()
    {
        foreach (var line in GoldenLines())
        {
            var record = Deserialize(line);
            using var original = JsonDocument.Parse(line);
            using var reserialized = JsonDocument.Parse(JsonSerializer.Serialize(record));

            // Sorted so the assertion is order-independent and certain.
            static string[] Keys(JsonElement element) =>
                element.EnumerateObject()
                    .Select(property => property.Name)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();

            Assert.Equal(Keys(original.RootElement), Keys(reserialized.RootElement));
            Assert.Equal(
                Keys(original.RootElement.GetProperty("metadata")),
                Keys(reserialized.RootElement.GetProperty("metadata")));
        }
    }

    [Fact]
    public void GoldenUrlBodyIsTheCanonicalInternetShortcut()
    {
        var body = File.ReadAllText(FixturePath("golden.url"));
        Assert.StartsWith("[InternetShortcut]", body, StringComparison.Ordinal);
        Assert.Contains("URL=https://www.example.com/docs/page", body, StringComparison.Ordinal);

        var linkRecord = Deserialize(GoldenLines()[4]);
        Assert.Equal(linkRecord.Metadata["url"].GetString(), ReadShortcutUrl(body));
    }

    // Mirrors the parse both apps apply to a `.url` body.
    private static string? ReadShortcutUrl(string body)
    {
        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
            {
                var value = line[4..].Trim();
                return value.Length == 0 ? null : value;
            }
        }
        return null;
    }
}
