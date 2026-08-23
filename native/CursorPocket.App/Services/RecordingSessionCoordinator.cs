using CursorPocket.Core.Models;
using CursorPocket.Core.Services;

namespace CursorPocket_App.Services;

public sealed class RecordingSessionCoordinator : IRecordingSessionCoordinator, IDisposable
{
    private readonly RecordingService _recording;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public RecordingSessionCoordinator(RecordingService recording)
    {
        _recording = recording;
        _recording.StateChanged += Recording_StateChanged;
    }

    public RecordingSessionState State { get; private set; } = RecordingSessionState.Idle;
    public bool IsActive => State is RecordingSessionState.Starting or RecordingSessionState.Recording or RecordingSessionState.Finalizing;
    public bool IsVideo => _recording.IsVideo;
    public event EventHandler<RecordingSessionState>? StateChanged;

    public async Task StartVideoAsync(RecordingOptions options, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureIdle();
            SetState(RecordingSessionState.Starting);
            await _recording.StartVideoAsync(options, cancellationToken);
        }
        catch
        {
            SetState(RecordingSessionState.Failed);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StartAudioAsync(string? microphoneId = null, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureIdle();
            SetState(RecordingSessionState.Starting);
            await _recording.StartAudioAsync(microphoneId, cancellationToken);
        }
        catch
        {
            SetState(RecordingSessionState.Failed);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<CaptureRecord?> FinishAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!IsActive)
            {
                return null;
            }
            SetState(RecordingSessionState.Finalizing);
            var record = IsVideo
                ? await _recording.StopVideoAsync(cancellationToken: cancellationToken)
                : await _recording.StopAudioAsync(cancellationToken: cancellationToken);
            SetState(record is null ? RecordingSessionState.Failed : RecordingSessionState.Completed);
            return record;
        }
        catch
        {
            SetState(RecordingSessionState.Failed);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DiscardAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!IsActive)
            {
                return;
            }
            SetState(RecordingSessionState.Finalizing);
            if (IsVideo)
            {
                await _recording.StopVideoAsync(discard: true, cancellationToken);
            }
            else
            {
                await _recording.StopAudioAsync(discard: true, cancellationToken);
            }
            SetState(RecordingSessionState.Discarded);
        }
        catch
        {
            SetState(RecordingSessionState.Failed);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void Recording_StateChanged(object? sender, RecordingState state)
    {
        var mapped = state switch
        {
            RecordingState.Starting => RecordingSessionState.Starting,
            RecordingState.Recording => RecordingSessionState.Recording,
            RecordingState.Finalizing => RecordingSessionState.Finalizing,
            RecordingState.Failed => RecordingSessionState.Failed,
            _ => State is RecordingSessionState.Finalizing or RecordingSessionState.Completed or RecordingSessionState.Discarded
                ? State
                : RecordingSessionState.Idle,
        };
        SetState(mapped);
    }

    private void EnsureIdle()
    {
        if (IsActive)
        {
            throw new InvalidOperationException("Finish the current recording first.");
        }
    }

    private void SetState(RecordingSessionState state)
    {
        if (State == state)
        {
            return;
        }
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public void Dispose() => _recording.StateChanged -= Recording_StateChanged;
}
