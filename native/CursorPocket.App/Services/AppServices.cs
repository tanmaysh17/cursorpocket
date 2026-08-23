using CursorPocket.Core.Models;
using CursorPocket.Core.Services;
using CursorPocket.Core.Storage;

namespace CursorPocket_App.Services;

public sealed class AppServices : IDisposable, ISettingsUpdateQueue
{
    private readonly SemaphoreSlim _settingsGate = new(1, 1);
    private AppServices(SettingsStore settingsStore, AppSettings settings, CaptureStore captureStore, string ffmpegPath)
    {
        SettingsStore = settingsStore;
        Settings = settings;
        CaptureStore = captureStore;
        FfmpegPath = ffmpegPath;
        Library = new LibraryService(captureStore);
        Screenshots = new ScreenshotCaptureService(captureStore);
        Recording = new RecordingService(captureStore, ffmpegPath, () => (Settings.AudioNoiseSuppression, Settings.AudioAutoLevel));
        MediaDevices = new MediaDeviceCatalog(cancellationToken => Recording.GetVideoDevicesAsync(cancellationToken));
        RecordingSession = new RecordingSessionCoordinator(Recording);
        Context = new WindowContextService();
        Hotkey = new GlobalHotkeyService();
        EscapeHotkey = new ScopedEscapeHotkeyService();
        Startup = new StartupService();
        Previews = new PreviewService(captureStore, ffmpegPath);
        CaptureStore.CaptureCompleted += CaptureStore_CaptureCompleted;
    }

    public SettingsStore SettingsStore { get; }
    public AppSettings Settings { get; private set; }
    public CaptureStore CaptureStore { get; private set; }
    public LibraryService Library { get; private set; }
    public ScreenshotCaptureService Screenshots { get; private set; }
    public RecordingService Recording { get; private set; }
    public IMediaDeviceCatalog MediaDevices { get; }
    public RecordingSessionCoordinator RecordingSession { get; private set; }
    public WindowContextService Context { get; }
    public GlobalHotkeyService Hotkey { get; }
    public ScopedEscapeHotkeyService EscapeHotkey { get; }
    public StartupService Startup { get; }
    public PreviewService Previews { get; private set; }
    public string FfmpegPath { get; }
    public event EventHandler<CaptureCompletedEventArgs>? CaptureCompleted;
    public event EventHandler<AppSettings>? SettingsChanged;

    public static async Task<AppServices> CreateAsync(CancellationToken cancellationToken = default)
    {
        var settingsStore = new SettingsStore();
        var settings = await settingsStore.LoadAsync(cancellationToken);
        var ffmpegPath = ResolveFfmpegPath();
        var captureStore = new CaptureStore(settings.CaptureDirectory);
        var services = new AppServices(settingsStore, settings, captureStore, ffmpegPath);
        // Keep the saved preference authoritative and rewrite the command on
        // every launch. This heals a development/portable path after the app
        // is installed somewhere else without requiring the user to toggle
        // startup off and on again.
        services.Startup.SetEnabled(settings.StartWithWindows);
        services.RegisterAvailableHotkey(settings.ActivationShortcut);
        return services;
    }

    /// <summary>
    /// Sweeps for recordings a crash left behind. This used to be awaited before the
    /// first window existed, so a large capture folder delayed every launch. Anything
    /// it finds reaches the Library through <see cref="CaptureCompleted"/>, exactly
    /// like a fresh capture, so nothing has to wait for it.
    /// </summary>
    public void StartOrphanRecovery()
    {
        var store = CaptureStore;
        _ = Task.Run(async () =>
        {
            try
            {
                await store.RecoverOrphanedMediaAsync();
            }
            catch (Exception)
            {
                // Recovery is best effort; a fresh session must still start clean.
            }
        });
    }

    public async Task UpdateSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _settingsGate.WaitAsync(cancellationToken);
        try
        {
            await ApplySettingsAsync(settings, cancellationToken);
        }
        finally
        {
            _settingsGate.Release();
        }
    }

    public async Task<AppSettings> UpdateAsync(
        Func<AppSettings, AppSettings> update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        await _settingsGate.WaitAsync(cancellationToken);
        try
        {
            await ApplySettingsAsync(update(Settings), cancellationToken);
            return Settings;
        }
        finally
        {
            _settingsGate.Release();
        }
    }

    private async Task ApplySettingsAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        var normalized = SettingsStore.Normalize(settings);
        var folderChanged = !string.Equals(Settings.CaptureDirectory, normalized.CaptureDirectory, StringComparison.OrdinalIgnoreCase);
        if (folderChanged && RecordingSession.IsActive)
        {
            throw new InvalidOperationException("Finish the current recording before changing the capture folder.");
        }
        Settings = normalized;
        await SettingsStore.SaveAsync(normalized, cancellationToken);
        Startup.SetEnabled(normalized.StartWithWindows);
        RegisterAvailableHotkey(normalized.ActivationShortcut);

        if (folderChanged)
        {
            RecordingSession.Dispose();
            Recording.Dispose();
            CaptureStore.CaptureCompleted -= CaptureStore_CaptureCompleted;
            var replacementStore = new CaptureStore(normalized.CaptureDirectory);
            CaptureStore = replacementStore;
            CaptureStore.CaptureCompleted += CaptureStore_CaptureCompleted;
            Library = new LibraryService(CaptureStore);
            Screenshots = new ScreenshotCaptureService(CaptureStore);
            Recording = new RecordingService(CaptureStore, FfmpegPath, () => (Settings.AudioNoiseSuppression, Settings.AudioAutoLevel));
            RecordingSession = new RecordingSessionCoordinator(Recording);
            Previews = new PreviewService(CaptureStore, FfmpegPath);
            StartOrphanRecovery();
        }
        SettingsChanged?.Invoke(this, normalized);
    }

    public async Task UpdateRecordingDefaultsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        await _settingsGate.WaitAsync(cancellationToken);
        try
        {
            var normalized = SettingsStore.Normalize(settings);
            Settings = normalized;
            await SettingsStore.SaveAsync(normalized, cancellationToken);
            SettingsChanged?.Invoke(this, normalized);
        }
        finally
        {
            _settingsGate.Release();
        }
    }

    public async Task UpdateLibraryWindowGeometryAsync(string geometry, CancellationToken cancellationToken = default)
    {
        await _settingsGate.WaitAsync(cancellationToken);
        try
        {
            Settings = Settings with { LibraryWindowGeometry = geometry };
            await SettingsStore.SaveAsync(Settings, cancellationToken);
        }
        finally
        {
            _settingsGate.Release();
        }
    }

    /// <summary>
    /// Remembers where the user dragged command mode. Deliberately does not raise
    /// SettingsChanged: moving the panel is not a change other surfaces care about.
    /// </summary>
    public async Task UpdateCommandPanelAnchorAsync(double anchorX, double anchorY, CancellationToken cancellationToken = default)
    {
        await _settingsGate.WaitAsync(cancellationToken);
        try
        {
            Settings = SettingsStore.Normalize(Settings with { CommandPanelAnchorX = anchorX, CommandPanelAnchorY = anchorY });
            await SettingsStore.SaveAsync(Settings, cancellationToken);
        }
        finally
        {
            _settingsGate.Release();
        }
    }

    public void Dispose()
    {
        CaptureStore.CaptureCompleted -= CaptureStore_CaptureCompleted;
        Hotkey.Dispose();
        EscapeHotkey.Dispose();
        RecordingSession.Dispose();
        Recording.Dispose();
    }

    private void CaptureStore_CaptureCompleted(object? sender, CaptureCompletedEventArgs eventArgs) =>
        CaptureCompleted?.Invoke(this, eventArgs);

    private void RegisterAvailableHotkey(string preferred)
    {
        HotkeyCandidateResolver.RegisterFirstAvailable(preferred, Hotkey.TryRegister);
    }

    private static string ResolveFfmpegPath()
    {
        var besideApp = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(besideApp))
        {
            return besideApp;
        }
        return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "third_party", "ffmpeg", "bin", "ffmpeg.exe"));
    }
}
