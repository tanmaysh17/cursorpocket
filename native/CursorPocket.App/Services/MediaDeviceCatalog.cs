using CursorPocket.Core.Models;

namespace CursorPocket_App.Services;

public enum MediaDeviceCatalogState
{
    Loading,
    Fresh,
    Stale,
    Empty,
    Error,
}

public sealed record MediaDeviceSnapshot(
    IReadOnlyList<MediaDeviceDescriptor> Audio,
    IReadOnlyList<MediaDeviceDescriptor> Video,
    MediaDeviceCatalogState State,
    DateTimeOffset? UpdatedAt = null,
    string? Error = null);

public interface IMediaDeviceCatalog
{
    MediaDeviceSnapshot Current { get; }
    event EventHandler<MediaDeviceSnapshot>? Changed;
    Task<MediaDeviceSnapshot> RefreshAsync(bool force = false, CancellationToken cancellationToken = default);
}

/// <summary>
/// Makes the recording shell independent of FFmpeg and device enumeration latency.
/// A stale, known-good inventory remains selectable while a bounded refresh runs.
/// </summary>
public sealed class MediaDeviceCatalog(
    Func<CancellationToken, Task<(IReadOnlyList<MediaDeviceDescriptor> Audio, IReadOnlyList<MediaDeviceDescriptor> Video)>> enumerate)
    : IMediaDeviceCatalog
{
    private static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private MediaDeviceSnapshot _current = new([], [], MediaDeviceCatalogState.Loading);

    public MediaDeviceSnapshot Current => _current;
    public event EventHandler<MediaDeviceSnapshot>? Changed;

    public async Task<MediaDeviceSnapshot> RefreshAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        if (!force && _current.UpdatedAt is { } updated && DateTimeOffset.UtcNow - updated < FreshFor)
        {
            return _current;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!force && _current.UpdatedAt is { } afterWait && DateTimeOffset.UtcNow - afterWait < FreshFor)
            {
                return _current;
            }

            Publish(_current with
            {
                State = _current.UpdatedAt is null ? MediaDeviceCatalogState.Loading : MediaDeviceCatalogState.Stale,
                Error = null,
            });
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                var (audio, video) = await enumerate(timeout.Token);
                var state = audio.Count == 0 && video.Count == 0
                    ? MediaDeviceCatalogState.Empty
                    : MediaDeviceCatalogState.Fresh;
                Publish(new MediaDeviceSnapshot(audio, video, state, DateTimeOffset.UtcNow));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                PublishFailure("Device discovery timed out. Cached devices remain available.");
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                PublishFailure(error.Message);
            }

            return _current;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void PublishFailure(string error) => Publish(_current with
    {
        State = _current.UpdatedAt is null ? MediaDeviceCatalogState.Error : MediaDeviceCatalogState.Stale,
        Error = error,
    });

    private void Publish(MediaDeviceSnapshot snapshot)
    {
        _current = snapshot;
        Changed?.Invoke(this, snapshot);
    }
}
