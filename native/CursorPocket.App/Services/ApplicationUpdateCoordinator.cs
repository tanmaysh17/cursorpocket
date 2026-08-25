using System.Diagnostics;
using System.Reflection;
using CursorPocket.Core.Models;
using CursorPocket.Core.Services;
using CursorPocket.Core.Updates;

namespace CursorPocket_App.Services;

public sealed class ApplicationUpdateCoordinator : IDisposable
{
    public const string ExpectedPublisher = "Tanmay Sharma";
    public static readonly Uri ManifestUri = new("https://tanmaysh17.github.io/cursorpocket/update.json");
    private readonly IApplicationUpdateService _service;
    private readonly ISettingsUpdateQueue _settings;
    private readonly Func<AppSettings> _currentSettings;
    private readonly SemaphoreSlim _downloadGate = new(1, 1);
    private CancellationTokenSource? _scheduledCheck;

    public ApplicationUpdateCoordinator(
        IApplicationUpdateService service,
        ISettingsUpdateQueue settings,
        Func<AppSettings> currentSettings)
    {
        _service = service;
        _settings = settings;
        _currentSettings = currentSettings;
    }

    public string CurrentVersion => GetCurrentVersion();
    public string StatusMessage { get; private set; } = "Updates are checked privately through GitHub.";
    public bool IsChecking { get; private set; }
    public bool IsDownloading { get; private set; }
    public event EventHandler? StateChanged;
    public event EventHandler<ApplicationUpdateInfo>? UpdateAvailable;

    public void ScheduleAutomaticCheck(TimeSpan? delay = null)
    {
        _scheduledCheck?.Cancel();
        _scheduledCheck?.Dispose();
        var cancellation = _scheduledCheck = new CancellationTokenSource();
        _ = CheckAfterDelayAsync(delay ?? TimeSpan.FromSeconds(30), cancellation.Token);
    }

    private async Task CheckAfterDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
            await CheckAsync(force: false, cancellationToken);
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

            if (result.Status is UpdateCheckStatus.Available or UpdateCheckStatus.UpToDate && result.CheckedAt is { } checkedAt)
            {
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
        WritePendingUpdate(downloaded.Update);
        Process.Start(new ProcessStartInfo
        {
            FileName = downloaded.InstallerPath,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELAUNCH",
            UseShellExecute = true,
        });
    }

    public string? ConsumePendingUpdateResult()
    {
        var path = PendingUpdatePath();
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
        var path = PendingUpdatePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, update.Version.ToString());
    }

    private static string PendingUpdatePath() => Path.Combine(
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
