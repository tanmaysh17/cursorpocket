using CursorPocket.Core.Models;
using CursorPocket.Core.Services;
using CursorPocket.Core.Storage;

namespace CursorPocket_App.Services;

public sealed class AppServices : IDisposable
{
    private AppServices(SettingsStore settingsStore, AppSettings settings, CaptureStore captureStore, string ffmpegPath)
    {
        SettingsStore = settingsStore;
        Settings = settings;
        CaptureStore = captureStore;
        FfmpegPath = ffmpegPath;
        Library = new LibraryService(captureStore);
        Screenshots = new ScreenshotCaptureService(captureStore);
        Recording = new RecordingService(captureStore, ffmpegPath);
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
        await captureStore.RecoverOrphanedMediaAsync(cancellationToken);
        var services = new AppServices(settingsStore, settings, captureStore, ffmpegPath);
        // Keep the saved preference authoritative and rewrite the command on
        // every launch. This heals a development/portable path after the app
        // is installed somewhere else without requiring the user to toggle
        // startup off and on again.
        services.Startup.SetEnabled(settings.StartWithWindows);
        services.RegisterAvailableHotkey(settings.ActivationShortcut);
        return services;
    }

    public async Task UpdateSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var normalized = SettingsStore.Normalize(settings);
        var folderChanged = !string.Equals(Settings.CaptureDirectory, normalized.CaptureDirectory, StringComparison.OrdinalIgnoreCase);
        Settings = normalized;
        await SettingsStore.SaveAsync(normalized, cancellationToken);
        Startup.SetEnabled(normalized.StartWithWindows);
        RegisterAvailableHotkey(normalized.ActivationShortcut);

        if (folderChanged)
        {
            Recording.Dispose();
            CaptureStore.CaptureCompleted -= CaptureStore_CaptureCompleted;
            var replacementStore = new CaptureStore(normalized.CaptureDirectory);
            await replacementStore.RecoverOrphanedMediaAsync(cancellationToken);
            CaptureStore = replacementStore;
            CaptureStore.CaptureCompleted += CaptureStore_CaptureCompleted;
            Library = new LibraryService(CaptureStore);
            Screenshots = new ScreenshotCaptureService(CaptureStore);
            Recording = new RecordingService(CaptureStore, FfmpegPath);
            Previews = new PreviewService(CaptureStore, FfmpegPath);
        }
        SettingsChanged?.Invoke(this, normalized);
    }

    public async Task UpdateRecordingDefaultsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var normalized = SettingsStore.Normalize(settings);
        Settings = normalized;
        await SettingsStore.SaveAsync(normalized, cancellationToken);
        SettingsChanged?.Invoke(this, normalized);
    }

    public async Task UpdateLibraryWindowGeometryAsync(string geometry, CancellationToken cancellationToken = default)
    {
        Settings = Settings with { LibraryWindowGeometry = geometry };
        await SettingsStore.SaveAsync(Settings, cancellationToken);
    }

    /// <summary>
    /// Remembers where the user dragged command mode. Deliberately does not raise
    /// SettingsChanged: moving the panel is not a change other surfaces care about.
    /// </summary>
    public async Task UpdateCommandPanelAnchorAsync(double anchorX, double anchorY, CancellationToken cancellationToken = default)
    {
        Settings = SettingsStore.Normalize(Settings with { CommandPanelAnchorX = anchorX, CommandPanelAnchorY = anchorY });
        await SettingsStore.SaveAsync(Settings, cancellationToken);
    }

    public void Dispose()
    {
        CaptureStore.CaptureCompleted -= CaptureStore_CaptureCompleted;
        Hotkey.Dispose();
        EscapeHotkey.Dispose();
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
