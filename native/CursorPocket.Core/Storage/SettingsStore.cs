using System.Text.Json;
using CursorPocket.Core.Models;
using CursorPocket.Core.Services;

namespace CursorPocket.Core.Storage;

public sealed class SettingsStore(string? settingsPath = null)
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
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
            var backupPath = SettingsPath + ".bak";
            if (File.Exists(backupPath))
            {
                try
                {
                    await using var backup = File.OpenRead(backupPath);
                    var recovered = await JsonSerializer.DeserializeAsync<AppSettings>(backup, JsonOptions, cancellationToken);
                    return Normalize(recovered ?? new AppSettings());
                }
                catch (Exception backupError) when (backupError is IOException or JsonException or UnauthorizedAccessException)
                {
                    // Both copies are invalid; defaults are safer than blocking launch.
                }
            }
            return Normalize(new AppSettings());
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        var temporaryPath = string.Empty;
        try
        {
            var normalized = Normalize(settings);
            var directory = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(directory, $".{Path.GetFileName(SettingsPath)}.{Guid.NewGuid():N}.tmp");
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, normalized, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(SettingsPath))
            {
                await ReplaceWithRetryAsync(temporaryPath, SettingsPath, SettingsPath + ".bak", cancellationToken);
            }
            else
            {
                File.Move(temporaryPath, SettingsPath);
            }
            temporaryPath = string.Empty;
        }
        finally
        {
            if (!string.IsNullOrEmpty(temporaryPath))
            {
                try { File.Delete(temporaryPath); } catch (IOException) { }
            }
            _writeLock.Release();
        }
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
        var cameraShape = value.VideoCameraShape is "rounded" or "squircle"
            ? value.VideoCameraShape
            : "rounded";
        var cameraBackground = value.VideoCameraBackground is "none" or "blur" or "image"
            ? value.VideoCameraBackground
            : "none";
        // "image" with nothing to show would run segmentation every frame and
        // then composite nothing, so it is not a real selection.
        if (cameraBackground == "image" && string.IsNullOrWhiteSpace(value.VideoCameraBackgroundImage))
        {
            cameraBackground = "none";
        }
        var cameraTouchUp = Math.Clamp(value.VideoCameraTouchUp, 0, 2);
        var cameraBrightness = ClampAdjustment(value.VideoCameraBrightness);
        var cameraWarmth = ClampAdjustment(value.VideoCameraWarmth);
        var cameraContrast = ClampAdjustment(value.VideoCameraContrast);
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
        var themeMode = Enum.IsDefined(value.ThemeMode) ? value.ThemeMode : AppThemeMode.System;

        var onboardingVersion = value.OnboardingVersion > 0
            ? value.OnboardingVersion
            : value.OnboardingSeen ? OnboardingFlow.CurrentVersion : 0;
        DateTimeOffset? lastUpdateCheckAt = value.LastUpdateCheckAt is { } checkedAt &&
            checkedAt > DateTimeOffset.UnixEpoch && checkedAt < DateTimeOffset.UtcNow.AddDays(1)
                ? checkedAt
                : null;

        return value with
        {
            ThemeMode = themeMode,
            CaptureDirectory = captureDirectory,
            VideoFramesPerSecond = fps,
            VideoCountdownSeconds = countdown,
            VideoCameraPosition = position,
            VideoSourceKind = source,
            VideoCameraWidth = cameraWidth,
            VideoCameraShape = cameraShape,
            VideoCameraBackground = cameraBackground,
            VideoCameraTouchUp = cameraTouchUp,
            VideoCameraBrightness = cameraBrightness,
            VideoCameraWarmth = cameraWarmth,
            VideoCameraContrast = cameraContrast,
            CursorCompanionMode = companionMode,
            OnboardingSeen = onboardingVersion >= OnboardingFlow.CurrentVersion,
            OnboardingVersion = onboardingVersion,
            LastUpdateCheckAt = lastUpdateCheckAt,
            CommandPanelAnchorX = anchorX,
            CommandPanelAnchorY = anchorY,
        };
    }

    private static double ClampAnchor(double value, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1) : fallback;

    private static int ClampAdjustment(int value) => Math.Clamp(value, -100, 100);

    private static async Task ReplaceWithRetryAsync(
        string source,
        string destination,
        string backup,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Replace(source, destination, backup, ignoreMetadataErrors: true);
                return;
            }
            catch (IOException) when (attempt < 5)
            {
                // Search indexers and anti-malware scanners can briefly open the old
                // settings or backup between close and replace. The write remains
                // serialized and atomic; this only waits for that transient lease.
                await Task.Delay(TimeSpan.FromMilliseconds(20 * (attempt + 1)), cancellationToken);
            }
        }
    }
}
