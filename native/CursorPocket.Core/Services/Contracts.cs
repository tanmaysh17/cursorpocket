using CursorPocket.Core.Models;

namespace CursorPocket.Core.Services;

public interface ICaptureService
{
    Task<CaptureRecord> CaptureDisplayAsync(CancellationToken cancellationToken = default);
    Task<CaptureRecord> CaptureAllDisplaysAsync(CancellationToken cancellationToken = default);
    Task<CaptureRecord> CaptureWindowAsync(long windowHandle, CancellationToken cancellationToken = default);
    Task<CaptureRecord> CaptureRegionAsync(CaptureBounds bounds, CancellationToken cancellationToken = default);
}

public interface IRecordingService
{
    RecordingState State { get; }
    event EventHandler<RecordingState>? StateChanged;
    event EventHandler<TimeSpan>? ElapsedChanged;
    Task StartVideoAsync(RecordingOptions options, CancellationToken cancellationToken = default);
    Task<CaptureRecord?> StopVideoAsync(bool discard = false, CancellationToken cancellationToken = default);
    Task StartAudioAsync(string? microphoneId = null, CancellationToken cancellationToken = default);
    Task<CaptureRecord?> StopAudioAsync(bool discard = false, CancellationToken cancellationToken = default);
}

public enum RecordingSessionState
{
    Idle,
    Starting,
    Recording,
    Finalizing,
    Completed,
    Failed,
    Discarded,
}

public interface IRecordingSessionCoordinator
{
    RecordingSessionState State { get; }
    bool IsActive { get; }
    bool IsVideo { get; }
    event EventHandler<RecordingSessionState>? StateChanged;
    Task StartVideoAsync(RecordingOptions options, CancellationToken cancellationToken = default);
    Task StartAudioAsync(string? microphoneId = null, CancellationToken cancellationToken = default);
    Task<CaptureRecord?> FinishAsync(CancellationToken cancellationToken = default);
    Task DiscardAsync(CancellationToken cancellationToken = default);
}

public interface ISettingsUpdateQueue
{
    Task<AppSettings> UpdateAsync(
        Func<AppSettings, AppSettings> update,
        CancellationToken cancellationToken = default);
}

public interface ILibraryService
{
    Task<IReadOnlyList<CaptureRecord>> GetRecentAsync(int limit = 250, CancellationToken cancellationToken = default);
    Task DeleteAsync(CaptureRecord record, CancellationToken cancellationToken = default);
    string GetAbsolutePath(CaptureRecord record);
}

public interface IContextCaptureService
{
    long SnapshotForegroundWindow();
    Task<string?> ReadSelectedTextAsync(long sourceWindow, CancellationToken cancellationToken = default);
    Task<string?> ReadBrowserLinkAsync(long sourceWindow, CancellationToken cancellationToken = default);
    void RestoreFocus(long sourceWindow);
}

public interface IHotkeyService : IDisposable
{
    string? RegisteredShortcut { get; }
    event EventHandler? Invoked;
    bool TryRegister(string shortcut);
    void Unregister();
}
