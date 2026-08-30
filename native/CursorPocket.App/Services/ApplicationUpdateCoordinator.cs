using System.Diagnostics;
using System.Reflection;
using CursorPocket.Core.Models;
using CursorPocket.Core.Services;
using CursorPocket.Core.Updates;

namespace CursorPocket_App.Services;

public sealed class ApplicationUpdateCoordinator : IDisposable
{
    // Public releases are intentionally unsigned while CursorPocket is a free project.
    // Set this to the certificate publisher name if code signing is added later.
    public const string? ExpectedPublisher = null;
    public static readonly Uri ManifestUri = new("https://github.com/tanmaysh17/cursorpocket/releases/latest/download/update.json");
    private readonly IApplicationUpdateService _service;
    private readonly ISettingsUpdateQueue _settings;
    private readonly Func<AppSettings> _currentSettings;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<ProcessStartInfo, Process?> _startInstaller;
    private readonly string _pendingUpdatePath;
    private readonly SemaphoreSlim _downloadGate = new(1, 1);
    private CancellationTokenSource? _scheduledCheck;

    public ApplicationUpdateCoordinator(
        IApplicationUpdateService service,
        ISettingsUpdateQueue settings,
        Func<AppSettings> currentSettings,
        Func<DateTimeOffset>? clock = null,
        Func<ProcessStartInfo, Process?>? startInstaller = null,
        string? pendingUpdatePath = null)
    {
        _service = service;
        _settings = settings;
        _currentSettings = currentSettings;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _startInstaller = startInstaller ?? Process.Start;
        _pendingUpdatePath = pendingUpdatePath ?? DefaultPendingUpdatePath();
    }

    public string CurrentVersion => GetCurrentVersion();
    public string StatusMessage { get; private set; } = "Updates are checked privately through GitHub.";
    public bool IsChecking { get; private set; }
    public bool IsDownloading { get; private set; }
    public event EventHandler? StateChanged;
    public event EventHandler<ApplicationUpdateInfo>? UpdateAvailable;

    internal static bool ShouldRescheduleAutomaticCheck(bool wasEnabled, bool isEnabled) =>
        !wasEnabled && isEnabled;

    public void ScheduleAutomaticCheck(TimeSpan? delay = null, TimeSpan? interval = null)
    {
        var initialDelay = delay ?? TimeSpan.FromSeconds(30);
        var repeatInterval = interval ?? ApplicationUpdateService.CheckInterval;
        if (initialDelay < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delay));
        if (repeatInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));
        _scheduledCheck?.Cancel();
        _scheduledCheck?.Dispose();
        var cancellation = _scheduledCheck = new CancellationTokenSource();
        _ = RunAutomaticChecksAsync(
            initialDelay,
            repeatInterval,
            cancellation.Token);
    }

    private async Task RunAutomaticChecksAsync(
        TimeSpan initialDelay,
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(initialDelay, cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                ApplicationUpdateCheckResult? result = null;
                try
                {
                    result = await CheckAsync(force: false, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception error)
                {
                    // An automatic check is background maintenance. A settings or
                    // service failure must not terminate the app or the daily loop.
                    Debug.WriteLine($"Automatic update check failed: {error}");
                }

                var nextDelay = interval;
                if (result?.Status == UpdateCheckStatus.Throttled &&
                    _currentSettings().LastUpdateCheckAt is { } lastCheck)
                {
                    nextDelay = lastCheck + ApplicationUpdateService.CheckInterval - _clock();
                    if (nextDelay < TimeSpan.Zero) nextDelay = TimeSpan.Zero;
                }
                await Task.Delay(nextDelay, cancellationToken);
            }
        }
        catch (OperationCanceledException) { }
    }

    public async Task<ApplicationUpdateCheckResult> CheckAsync(
        bool force,
        CancellationToken cancellationToken = default)
    {
        var settings = _currentSettings();
        IsChecking = true;
        StatusMessage = force ? "Checking for updates…" : StatusMessage;
        StateChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            var result = await _service.CheckAsync(
                CurrentVersion,
                settings.AutomaticallyCheckForUpdates,
                settings.LastUpdateCheckAt,
                force,
                cancellationToken);

            if (result.Status is not UpdateCheckStatus.Disabled and not UpdateCheckStatus.Throttled)
            {
                // "At most daily" applies to attempts, not only successful responses.
                // Otherwise an offline machine contacts GitHub on every relaunch.
                var checkedAt = result.CheckedAt ?? _clock();
                await _settings.UpdateAsync(value => value with { LastUpdateCheckAt = checkedAt }, cancellationToken);
            }

            StatusMessage = result.Status switch
            {
                UpdateCheckStatus.Available => $"CursorPocket {result.Update!.Version} is available.",
                UpdateCheckStatus.UpToDate => $"CursorPocket {CurrentVersion} is up to date.",
                UpdateCheckStatus.Disabled => "Automatic update checks are off.",
                UpdateCheckStatus.Throttled => FormatLastCheck(settings.LastUpdateCheckAt),
                UpdateCheckStatus.InvalidManifest => "The update information could not be verified.",
                _ => force ? "Could not reach GitHub. CursorPocket still works offline." : StatusMessage,
            };
            if (result.Status == UpdateCheckStatus.Available && result.Update is not null)
            {
                UpdateAvailable?.Invoke(this, result.Update);
            }
            return result;
        }
        finally
        {
            IsChecking = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task<DownloadedApplicationUpdate> DownloadAsync(
        ApplicationUpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _downloadGate.WaitAsync(cancellationToken);
        try
        {
            if (update.Version <= ParseCurrentVersion())
            {
                throw new InvalidOperationException("The selected update is not newer than the installed version.");
            }
            if (Environment.OSVersion.Version < update.MinimumWindowsVersion)
            {
                throw new InvalidOperationException($"This update requires Windows {update.MinimumWindowsVersion} or newer.");
            }
            IsDownloading = true;
            StatusMessage = $"Downloading CursorPocket {update.Version}…";
            StateChanged?.Invoke(this, EventArgs.Empty);
            var downloaded = await _service.DownloadAndVerifyAsync(update, ExpectedPublisher, progress, cancellationToken);
            StatusMessage = "Update verified. CursorPocket will restart to finish.";
            return downloaded;
        }
        catch
        {
            StatusMessage = "The update was not installed. Your current version is unchanged.";
            throw;
        }
        finally
        {
            IsDownloading = false;
            StateChanged?.Invoke(this, EventArgs.Empty);
            _downloadGate.Release();
        }
    }

    public void LaunchInstaller(DownloadedApplicationUpdate downloaded)
    {
        try
        {
            WritePendingUpdate(downloaded.Update);
            using var process = _startInstaller(new ProcessStartInfo
            {
                FileName = downloaded.InstallerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELAUNCH",
                UseShellExecute = true,
            });
            if (process is null)
            {
                throw new InvalidOperationException("Windows did not start the CursorPocket installer.");
            }
        }
        catch
        {
            DeletePendingUpdate();
            throw;
        }
    }

    public string? ConsumePendingUpdateResult()
    {
        var path = _pendingUpdatePath;
        if (!File.Exists(path)) return null;
        try
        {
            var target = File.ReadAllText(path).Trim();
            File.Delete(path);
            if (ReleaseVersion.TryParse(target, out var targetVersion) && ParseCurrentVersion() < targetVersion)
            {
                return $"CursorPocket {targetVersion} did not finish installing. Your current version is unchanged.";
            }
        }
        catch (IOException) { }
        return null;
    }

    private void WritePendingUpdate(ApplicationUpdateInfo update)
    {
        var path = _pendingUpdatePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, update.Version.ToString());
    }

    private void DeletePendingUpdate()
    {
        try { File.Delete(_pendingUpdatePath); } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private static string DefaultPendingUpdatePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CursorPocket",
        "pending-update.txt");

    private static string FormatLastCheck(DateTimeOffset? value) => value is null
        ? "Updates are checked privately through GitHub."
        : $"Last checked {value.Value.ToLocalTime():g}.";

    private static string GetCurrentVersion()
    {
        var informational = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        return string.IsNullOrWhiteSpace(informational)
            ? Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.0.0"
            : informational.Split('+', 2)[0];
    }

    private ReleaseVersion ParseCurrentVersion() => ReleaseVersion.TryParse(CurrentVersion, out var parsed)
        ? parsed
        : default;

    public void Dispose()
    {
        _scheduledCheck?.Cancel();
        _scheduledCheck?.Dispose();
        _downloadGate.Dispose();
        if (_service is IDisposable disposable) disposable.Dispose();
    }
}
