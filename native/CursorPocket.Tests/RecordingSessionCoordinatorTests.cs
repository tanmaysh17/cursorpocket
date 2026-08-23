using CursorPocket.Core.Models;
using CursorPocket.Core.Services;
using CursorPocket_App.Services;

namespace CursorPocket.Tests;

public sealed class RecordingSessionCoordinatorTests
{
    [Fact]
    public async Task Video_session_moves_through_recording_finalizing_and_completed()
    {
        var service = new FakeRecordingService();
        using var coordinator = new RecordingSessionCoordinator(service);
        var states = new List<RecordingSessionState>();
        coordinator.StateChanged += (_, state) => states.Add(state);

        await coordinator.StartVideoAsync(new RecordingOptions());
        var result = await coordinator.FinishAsync();

        Assert.NotNull(result);
        Assert.Equal(RecordingSessionState.Completed, coordinator.State);
        Assert.Equal(
            [RecordingSessionState.Starting, RecordingSessionState.Recording, RecordingSessionState.Finalizing, RecordingSessionState.Completed],
            states.Distinct());
    }

    [Fact]
    public async Task Discard_finishes_the_encoder_without_publishing_a_receipt_record()
    {
        var service = new FakeRecordingService();
        using var coordinator = new RecordingSessionCoordinator(service);
        await coordinator.StartAudioAsync("mic");

        await coordinator.DiscardAsync();

        Assert.True(service.Discarded);
        Assert.Equal(RecordingSessionState.Discarded, coordinator.State);
        Assert.False(coordinator.IsActive);
    }

    [Fact]
    public async Task Start_failure_is_an_explicit_failed_session()
    {
        var service = new FakeRecordingService { FailStart = true };
        using var coordinator = new RecordingSessionCoordinator(service);

        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.StartVideoAsync(new RecordingOptions()));

        Assert.Equal(RecordingSessionState.Failed, coordinator.State);
    }

    private sealed class FakeRecordingService : IRecordingService
    {
        public RecordingState State { get; private set; } = RecordingState.Idle;
        public bool IsVideo { get; private set; }
        public bool FailStart { get; init; }
        public bool Discarded { get; private set; }
        public event EventHandler<RecordingState>? StateChanged;
        public event EventHandler<TimeSpan>? ElapsedChanged
        {
            add { }
            remove { }
        }

        public Task StartVideoAsync(RecordingOptions options, CancellationToken cancellationToken = default)
        {
            IsVideo = true;
            return StartAsync();
        }

        public Task StartAudioAsync(string? microphoneId = null, CancellationToken cancellationToken = default)
        {
            IsVideo = false;
            return StartAsync();
        }

        private Task StartAsync()
        {
            SetState(RecordingState.Starting);
            if (FailStart) throw new InvalidOperationException("encoder failed");
            SetState(RecordingState.Recording);
            return Task.CompletedTask;
        }

        public Task<CaptureRecord?> StopVideoAsync(bool discard = false, CancellationToken cancellationToken = default) => StopAsync(discard);
        public Task<CaptureRecord?> StopAudioAsync(bool discard = false, CancellationToken cancellationToken = default) => StopAsync(discard);

        private Task<CaptureRecord?> StopAsync(bool discard)
        {
            SetState(RecordingState.Finalizing);
            Discarded = discard;
            SetState(RecordingState.Idle);
            return Task.FromResult<CaptureRecord?>(discard ? null : new CaptureRecord
            {
                Id = "capture",
                Kind = IsVideo ? "video" : "audio",
                CreatedAt = DateTimeOffset.Now.ToString("O"),
                RelativePath = IsVideo ? "video.mp4" : "audio.wav",
                Preview = "saved",
            });
        }

        private void SetState(RecordingState state)
        {
            State = state;
            StateChanged?.Invoke(this, state);
        }
    }
}
