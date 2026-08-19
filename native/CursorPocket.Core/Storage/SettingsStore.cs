using System.Text.Json;
using CursorPocket.Core.Models;
using CursorPocket.Core.Services;

namespace CursorPocket.Core.Storage;

public sealed class SettingsStore(string? settingsPath = null)
{
    public string SettingsPath { get; } = settingsPath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CursorPocket",
        "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SettingsPath))
        {
            return Normalize(new AppSettings());
        }

        try
        {
            await using var stream = File.OpenRead(SettingsPath);
            var value = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken);
            return Normalize(value ?? new AppSettings());
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            return Normalize(new AppSettings());
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(settings);
        var directory = Path.GetDirectoryName(SettingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = SettingsPath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, normalized, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temporaryPath, SettingsPath, true);
    }

    public static AppSettings Normalize(AppSettings value)
    {
        var fps = value.VideoFramesPerSecond is 15 or 24 or 30 or 60
            ? value.VideoFramesPerSecond
            : 30;
        var countdown = value.VideoCountdownSeconds is 0 or 3 or 5
            ? value.VideoCountdownSeconds
            : 0;
        var position = value.VideoCameraPosition is "top-left" or "top-right" or "bottom-left" or "bottom-right"
            ? value.VideoCameraPosition
            : "bottom-right";
        var source = value.VideoSourceKind is "display" or "region" or "window"
            ? value.VideoSourceKind
            : "display";
        var cameraWidth = value.VideoCameraWidth is 240 or 360 or 480
            ? value.VideoCameraWidth
            : 360;
        var companionMode = !value.FollowCursor && value.CursorCompanionMode == "while-moving"
            ? "off"
            : value.CursorCompanionMode is "off" or "while-moving" or "always"
                ? value.CursorCompanionMode
                : value.FollowCursor ? "while-moving" : "off";
        var captureDirectory = string.IsNullOrWhiteSpace(value.CaptureDirectory)
            ? new AppSettings().CaptureDirectory
            : value.CaptureDirectory;
        // A corrupt or out-of-range anchor must never put command mode off screen.
        var anchorX = ClampAnchor(value.CommandPanelAnchorX, CommandPanelPlacement.DefaultAnchorX);
        var anchorY = ClampAnchor(value.CommandPanelAnchorY, CommandPanelPlacement.DefaultAnchorY);

        return value with
        {
            CaptureDirectory = captureDirectory,
            VideoFramesPerSecond = fps,
            VideoCountdownSeconds = countdown,
            VideoCameraPosition = position,
            VideoSourceKind = source,
            VideoCameraWidth = cameraWidth,
            CursorCompanionMode = companionMode,
            CommandPanelAnchorX = anchorX,
            CommandPanelAnchorY = anchorY,
        };
    }

    private static double ClampAnchor(double value, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1) : fallback;
}
