using System.Diagnostics;
using System.Globalization;
using System.Text;
using CursorPocket.Core.Models;
using CursorPocket.Core.Services;
using CursorPocket.Core.Storage;
using NAudio.Wave;
using Windows.Devices.Enumeration;

namespace CursorPocket_App.Services;

public sealed class RecordingService : IRecordingService, IDisposable
{
    private readonly CaptureStore _store;
    private readonly string _ffmpegPath;
    private readonly System.Threading.Timer _elapsedTimer;
    private Stopwatch? _stopwatch;
    private Process? _videoProcess;
    private CaptureReservation? _videoReservation;
    private RecordingOptions? _videoOptions;
    private readonly StringBuilder _videoError = new();
    private WaveInEvent? _videoWaveIn;
    private WaveFileWriter? _videoWaveWriter;
    private string? _videoRawPath;
    private string? _videoAudioPath;
    private string? _videoMuxPath;
    private TaskCompletionSource _videoAudioStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _waveWriter;
    private CaptureReservation? _audioReservation;
    private TaskCompletionSource _audioStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Func<(bool NoiseSuppression, bool AutoLevel)>? _audioCleanupDefaults;

    public RecordingService(CaptureStore store, string ffmpegPath, Func<(bool NoiseSuppression, bool AutoLevel)>? audioCleanupDefaults = null)
    {
        _store = store;
        _ffmpegPath = ffmpegPath;
        _audioCleanupDefaults = audioCleanupDefaults;
        _elapsedTimer = new System.Threading.Timer(_ =>
        {
            if (_stopwatch?.IsRunning == true)
            {
                ElapsedChanged?.Invoke(this, _stopwatch.Elapsed);
            }
        }, null, Timeout.Infinite, Timeout.Infinite);
    }

    public RecordingState State { get; private set; } = RecordingState.Idle;
    public bool IsVideo => _videoProcess is not null;
    public double AudioLevel { get; private set; }
    public event EventHandler<RecordingState>? StateChanged;
    public event EventHandler<TimeSpan>? ElapsedChanged;
    public event EventHandler<double>? AudioLevelChanged;

    public IReadOnlyList<MediaDeviceDescriptor> GetMicrophones()
    {
        var devices = new List<MediaDeviceDescriptor>();
        for (var index = 0; index < WaveIn.DeviceCount; index++)
        {
            var capabilities = WaveIn.GetCapabilities(index);
            devices.Add(new MediaDeviceDescriptor(index.ToString(CultureInfo.InvariantCulture), capabilities.ProductName, "audio", index == 0));
        }
        return devices;
    }

    public async Task<(IReadOnlyList<MediaDeviceDescriptor> Audio, IReadOnlyList<MediaDeviceDescriptor> Video)> GetVideoDevicesAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_ffmpegPath))
        {
            return (GetMicrophones(), []);
        }
        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-hide_banner", "-list_devices", "true", "-f", "dshow", "-i", "dummy" })
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("The video component did not start.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            if (cancellationToken.IsCancellationRequested) throw;
            throw new TimeoutException("Recording-device discovery did not finish in time.");
        }
        var output = await errorTask;
        var ffmpegDevices = FfmpegDeviceParser.Parse(output);
        var audio = GetMicrophones().ToList();
        var video = ffmpegDevices.Video.ToList();

        try
        {
            var windowsAudio = await DeviceInformation.FindAllAsync(DeviceClass.AudioCapture).AsTask(cancellationToken);
            for (var index = 0; index < Math.Min(audio.Count, windowsAudio.Count); index++)
            {
                audio[index] = audio[index] with { Name = windowsAudio[index].Name };
            }

            var windowsVideo = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture).AsTask(cancellationToken);
            foreach (var device in windowsVideo)
            {
                if (video.All(item => !string.Equals(item.Name, device.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    video.Add(new MediaDeviceDescriptor(device.Id, device.Name, "video", video.Count == 0));
                }
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Privacy policy can restrict WinRT enumeration. NAudio and FFmpeg remain valid fallbacks.
        }

        return (audio, video);
    }

    public Task StartAudioAsync(string? microphoneId = null, CancellationToken cancellationToken = default)
    {
        EnsureIdle();
        if (WaveIn.DeviceCount < 1)
        {
            throw new InvalidOperationException("Windows did not report an available microphone.");
        }
        EnsureAvailableSpace(32L * 1024 * 1024, "audio note");
        var deviceNumber = int.TryParse(microphoneId, out var parsed) && parsed >= 0 && parsed < WaveIn.DeviceCount ? parsed : 0;
        _audioReservation = _store.Reserve(CaptureKind.Audio, ".wav");
        _waveIn = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(48000, 16, 1),
            BufferMilliseconds = 50,
            NumberOfBuffers = 4,
        };
        _waveWriter = new WaveFileWriter(_audioReservation.AbsolutePath, _waveIn.WaveFormat);
        _audioStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _waveIn.DataAvailable += OnAudioData;
        _waveIn.RecordingStopped += OnAudioStopped;
        SetState(RecordingState.Starting);
        try
        {
            _waveIn.StartRecording();
            StartClock();
            SetState(RecordingState.Recording);
            return Task.CompletedTask;
        }
        catch
        {
            CleanupAudio(deleteFile: true);
            SetState(RecordingState.Failed);
            throw;
        }
    }

    public async Task<CaptureRecord?> StopAudioAsync(bool discard = false, CancellationToken cancellationToken = default)
    {
        if (_waveIn is null || _audioReservation is null)
        {
            return null;
        }
        SetState(RecordingState.Finalizing);
        var duration = StopClock();
        var reservation = _audioReservation;
        _waveIn.StopRecording();
        try
        {
            await _audioStopped.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
        }
        catch (TimeoutException)
        {
            // A device can disappear during stop. Closing the writer still leaves a valid WAV header.
        }
        CleanupAudio(deleteFile: discard);
        if (discard || duration <= TimeSpan.Zero || !File.Exists(reservation.AbsolutePath))
        {
            SetState(RecordingState.Idle);
            return null;
        }
        var appliedCleanup = await TryCleanupAudioNoteAsync(reservation.AbsolutePath, cancellationToken);
        var record = await _store.RegisterReservationAsync(
            reservation,
            $"Audio · {FormatDuration(duration)}",
            new Dictionary<string, object?>
            {
                ["duration_seconds"] = Math.Round(duration.TotalSeconds, 3),
                ["sample_rate"] = 48000,
                ["channels"] = 1,
                ["audio_filters"] = appliedCleanup,
            },
            cancellationToken);
        SetState(RecordingState.Idle);
        return record;
    }

    public async Task StartVideoAsync(RecordingOptions options, CancellationToken cancellationToken = default)
    {
        EnsureIdle();
        if (!File.Exists(_ffmpegPath))
        {
            throw new FileNotFoundException("CursorPocket's FFmpeg video component is not installed.", _ffmpegPath);
        }
        EnsureAvailableSpace(256L * 1024 * 1024, "video");
        if (options.CountdownSeconds > 0)
        {
            SetState(RecordingState.Starting);
            await Task.Delay(TimeSpan.FromSeconds(options.CountdownSeconds), cancellationToken);
        }
        _videoReservation = _store.Reserve(CaptureKind.Video, ".mp4");
        _videoOptions = options;
        _videoRawPath = _videoReservation.AbsolutePath;
        if (options.IncludeMicrophone)
        {
            var temporaryDirectory = Path.Combine(_store.RootDirectory, ".cursorpocket", "temp");
            Directory.CreateDirectory(temporaryDirectory);
            _videoAudioPath = Path.Combine(temporaryDirectory, _videoReservation.Id + ".microphone.wav");
            _videoMuxPath = Path.Combine(temporaryDirectory, _videoReservation.Id + ".mux.mp4");
        }
        else
        {
            _videoAudioPath = null;
            _videoMuxPath = null;
        }
        _videoError.Clear();
        var command = FfmpegCommandBuilder.Build(
            _ffmpegPath,
            _videoRawPath,
            options with { IncludeMicrophone = false, MicrophoneName = string.Empty });
        var startInfo = new ProcessStartInfo
        {
            FileName = command[0],
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        foreach (var argument in command.Skip(1))
        {
            startInfo.ArgumentList.Add(argument);
        }
        SetState(RecordingState.Starting);
        _videoProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _videoProcess.ErrorDataReceived += (_, eventArgs) =>
        {
            if (!string.IsNullOrWhiteSpace(eventArgs.Data))
            {
                lock (_videoError)
                {
                    _videoError.AppendLine(eventArgs.Data);
                }
            }
        };
        if (!_videoProcess.Start())
        {
            CleanupVideo(deleteFile: true);
            SetState(RecordingState.Failed);
            throw new InvalidOperationException("The video recorder did not start.");
        }
        _videoProcess.BeginErrorReadLine();
        try
        {
            if (options.IncludeMicrophone)
            {
                StartVideoMicrophone(options);
            }
        }
        catch
        {
            if (!_videoProcess.HasExited)
            {
                _videoProcess.Kill(true);
                await _videoProcess.WaitForExitAsync(cancellationToken);
            }
            CleanupVideo(deleteFile: true);
            SetState(RecordingState.Failed);
            throw;
        }
        // Process.Start returns after FFmpeg has been created; yielding once lets
        // immediate startup failures surface without delaying the recording UI.
        await Task.Yield();
        if (_videoProcess.HasExited)
        {
            var detail = _videoError.ToString().Trim();
            CleanupVideo(deleteFile: true);
            SetState(RecordingState.Failed);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail) ? "The video recorder closed before receiving a frame." : detail);
        }
        StartClock();
        SetState(RecordingState.Recording);
    }

    public async Task<CaptureRecord?> StopVideoAsync(bool discard = false, CancellationToken cancellationToken = default)
    {
        if (_videoProcess is null || _videoReservation is null || _videoOptions is null)
        {
            return null;
        }
        var duration = StopClock();
        var process = _videoProcess;
        var reservation = _videoReservation;
        var options = _videoOptions;
        var rawPath = _videoRawPath ?? reservation.AbsolutePath;
        var audioPath = _videoAudioPath;
        var muxPath = _videoMuxPath;
        try
        {
            try
            {
                if (!process.HasExited)
                {
                    await process.StandardInput.WriteLineAsync("q");
                    await process.StandardInput.FlushAsync(cancellationToken);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    try
                    {
                        await process.WaitForExitAsync(timeout.Token);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        process.Kill(true);
                        await process.WaitForExitAsync(cancellationToken);
                    }
                }
            }
            finally
            {
                try
                {
                    await StopVideoMicrophoneAsync(cancellationToken);
                }
                finally
                {
                    process.Dispose();
                    _videoProcess = null;
                }
            }
        }
        catch
        {
            // Publish failure only after the capture process and microphone input
            // have been torn down, so camera dismissal can never remove the inset
            // from frames FFmpeg might still accept.
            SetState(RecordingState.Failed);
            throw;
        }

        // Finalizing begins only after FFmpeg has stopped accepting frames. The
        // camera self-view must remain visible through the last captured frame, but
        // it must not stay engaged while audio is muxed or the record is indexed.
        SetState(RecordingState.Finalizing);

        if (discard)
        {
            DeleteIfExists(rawPath);
            DeleteIfExists(audioPath);
            DeleteIfExists(muxPath);
            DeleteIfExists(reservation.AbsolutePath);
            ClearVideoState();
            SetState(RecordingState.Idle);
            return null;
        }

        string? microphoneWarning = null;
        if (options.IncludeMicrophone && audioPath is not null && muxPath is not null)
        {
            try
            {
                await MuxVideoMicrophoneAsync(rawPath, audioPath, muxPath, AudioCleanupFilterBuilder.Build(options.NoiseSuppression, options.AutoLevel), cancellationToken);
                File.Move(muxPath, reservation.AbsolutePath, true);
            }
            catch (Exception error) when (error is IOException or InvalidOperationException)
            {
                microphoneWarning = error.Message;
            }
            DeleteIfExists(audioPath);
            DeleteIfExists(muxPath);
        }

        if (!File.Exists(reservation.AbsolutePath) || new FileInfo(reservation.AbsolutePath).Length < 1024)
        {
            ClearVideoState();
            SetState(RecordingState.Idle);
            return null;
        }
        var record = await _store.RegisterReservationAsync(
            reservation,
            microphoneWarning is null
                ? $"Video · {FormatDuration(duration)}"
                : $"Video · {FormatDuration(duration)} · microphone was not saved",
            new Dictionary<string, object?>
            {
                ["duration_seconds"] = Math.Round(duration.TotalSeconds, 3),
                ["fps"] = options.FramesPerSecond,
                ["source_kind"] = options.SourceKind.ToString().ToLowerInvariant(),
                ["include_microphone"] = options.IncludeMicrophone,
                ["microphone_id"] = options.MicrophoneId,
                ["microphone_name"] = options.MicrophoneName,
                ["microphone_error"] = microphoneWarning,
                ["include_camera"] = options.IncludeCamera,
                ["camera_name"] = options.CameraName,
                ["camera_position"] = options.CameraPosition,
                ["camera_width"] = options.CameraWidth,
                ["camera_shape"] = options.CameraShape,
                ["camera_background"] = options.CameraBackgroundMode,
                ["audio_filters"] = AudioCleanupFilterBuilder.Build(options.NoiseSuppression, options.AutoLevel),
            },
            cancellationToken);
        ClearVideoState();
        SetState(RecordingState.Idle);
        return record;
    }

    public void Dispose()
    {
        if (_videoProcess is { HasExited: false } process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
            catch (Exception)
            {
                // Dispose is the last-resort path after coordinated shutdown.
            }
        }
        CleanupAudio(deleteFile: false);
        CleanupVideo(deleteFile: false);
        _elapsedTimer.Dispose();
    }

    private void OnAudioData(object? sender, WaveInEventArgs eventArgs)
    {
        _waveWriter?.Write(eventArgs.Buffer, 0, eventArgs.BytesRecorded);
        var peak = 0d;
        for (var index = 0; index + 1 < eventArgs.BytesRecorded; index += 2)
        {
            var sample = BitConverter.ToInt16(eventArgs.Buffer, index) / 32768d;
            peak = Math.Max(peak, Math.Abs(sample));
        }
        AudioLevel = Math.Min(1, peak * 2.4);
        AudioLevelChanged?.Invoke(this, AudioLevel);
    }

    private void OnAudioStopped(object? sender, StoppedEventArgs eventArgs)
    {
        _waveWriter?.Flush();
        _audioStopped.TrySetResult();
    }

    private void StartVideoMicrophone(RecordingOptions options)
    {
        if (string.IsNullOrWhiteSpace(_videoAudioPath))
        {
            return;
        }

        var microphones = GetMicrophones();
        var selected = microphones.FirstOrDefault(device => string.Equals(device.Id, options.MicrophoneId, StringComparison.OrdinalIgnoreCase))
            ?? microphones.FirstOrDefault(device => string.Equals(device.Name, options.MicrophoneName, StringComparison.OrdinalIgnoreCase))
            ?? microphones.FirstOrDefault();
        if (selected is null || !int.TryParse(selected.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var deviceNumber))
        {
            throw new InvalidOperationException("Windows did not report an available microphone for this recording.");
        }

        _videoAudioStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _videoWaveIn = new WaveInEvent
        {
            DeviceNumber = deviceNumber,
            WaveFormat = new WaveFormat(48000, 16, 1),
            BufferMilliseconds = 50,
            NumberOfBuffers = 4,
        };
        _videoWaveWriter = new WaveFileWriter(_videoAudioPath, _videoWaveIn.WaveFormat);
        _videoWaveIn.DataAvailable += OnVideoAudioData;
        _videoWaveIn.RecordingStopped += OnVideoAudioStopped;
        _videoWaveIn.StartRecording();
    }

    private void OnVideoAudioData(object? sender, WaveInEventArgs eventArgs)
    {
        _videoWaveWriter?.Write(eventArgs.Buffer, 0, eventArgs.BytesRecorded);
        var peak = 0d;
        for (var index = 0; index + 1 < eventArgs.BytesRecorded; index += 2)
        {
            peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(eventArgs.Buffer, index) / 32768d));
        }
        AudioLevel = Math.Min(1, peak * 2.4);
        AudioLevelChanged?.Invoke(this, AudioLevel);
    }

    private void OnVideoAudioStopped(object? sender, StoppedEventArgs eventArgs)
    {
        _videoWaveWriter?.Flush();
        _videoAudioStopped.TrySetResult();
    }

    private async Task StopVideoMicrophoneAsync(CancellationToken cancellationToken)
    {
        if (_videoWaveIn is null)
        {
            return;
        }
        try
        {
            _videoWaveIn.StopRecording();
            await _videoAudioStopped.Task.WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
        }
        catch (TimeoutException)
        {
            // Preserve the WAV header if a device was unplugged during finalization.
        }
        finally
        {
            _videoWaveIn.DataAvailable -= OnVideoAudioData;
            _videoWaveIn.RecordingStopped -= OnVideoAudioStopped;
            _videoWaveIn.Dispose();
            _videoWaveIn = null;
            _videoWaveWriter?.Dispose();
            _videoWaveWriter = null;
            AudioLevel = 0;
        }
    }

    /// <summary>
    /// Applies the configured cleanup chain to a finished audio note. The raw
    /// WAV is always written first and is only replaced when FFmpeg succeeds,
    /// so notes keep working with no FFmpeg sidecar at all. Returns the applied
    /// chain, or null when nothing was changed.
    /// </summary>
    private async Task<string?> TryCleanupAudioNoteAsync(string wavPath, CancellationToken cancellationToken)
    {
        var defaults = _audioCleanupDefaults?.Invoke() ?? (false, false);
        var chain = AudioCleanupFilterBuilder.Build(defaults.NoiseSuppression, defaults.AutoLevel);
        if (chain is null || !File.Exists(_ffmpegPath))
        {
            return null;
        }
        // Deliberately outside the dated capture folders: anything ending in
        // .wav under audio/<date> is picked up by CaptureStore's orphan
        // recovery, so a surviving temp file would reappear as a duplicate
        // note. The .cursorpocket/temp directory is not a capture category.
        var temporaryDirectory = Path.Combine(_store.RootDirectory, ".cursorpocket", "temp");
        var cleanedPath = Path.Combine(temporaryDirectory, Guid.NewGuid().ToString("N") + ".cleanup.wav");
        try
        {
            Directory.CreateDirectory(temporaryDirectory);
            var originalLength = new FileInfo(wavPath).Length;
            var startInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            foreach (var argument in new[]
            {
                "-y", "-hide_banner", "-loglevel", "error",
                "-i", wavPath, "-af", chain, "-c:a", "pcm_s16le", cleanedPath,
            })
            {
                startInfo.ArgumentList.Add(argument);
            }
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }
            // Without this a cancelled stop leaves ffmpeg running and holding
            // the temp file open.
            using var cancellation = cancellationToken.Register(() =>
            {
                try { process.Kill(true); } catch (Exception) { }
            });
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0 || !File.Exists(cleanedPath))
            {
                return null;
            }
            // Same format in and out, so a short result means the pass was
            // truncated — keep the full raw note instead of losing audio.
            if (new FileInfo(cleanedPath).Length < originalLength * 0.9)
            {
                return null;
            }
            File.Move(cleanedPath, wavPath, true);
            return chain;
        }
        catch (Exception)
        {
            // The untouched raw note is always the safe outcome.
            return null;
        }
        finally
        {
            try
            {
                if (File.Exists(cleanedPath))
                {
                    File.Delete(cleanedPath);
                }
            }
            catch (Exception)
            {
                // A leftover temp file is harmless where it lives; never let
                // cleanup of the cleanup stop the note from being registered.
            }
        }
    }

    private async Task MuxVideoMicrophoneAsync(string videoPath, string audioPath, string outputPath, string? audioFilter, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        var arguments = new List<string>
        {
            "-y", "-hide_banner", "-loglevel", "error",
            "-i", videoPath, "-i", audioPath,
            "-map", "0:v:0", "-map", "1:a:0",
            "-c:v", "copy",
        };
        if (audioFilter is not null)
        {
            // Cleanup runs here, at finalize time, where the video is a stream
            // copy anyway — a filter failure surfaces as the existing
            // "microphone was not saved" warning instead of losing the take.
            arguments.AddRange(["-af", audioFilter]);
        }
        arguments.AddRange(["-c:a", "aac", "-b:a", "128k", "-shortest", "-movflags", "+faststart", outputPath]);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var mux = Process.Start(startInfo) ?? throw new InvalidOperationException("The video finalizer did not start.");
        var errorTask = mux.StandardError.ReadToEndAsync(cancellationToken);
        await mux.WaitForExitAsync(cancellationToken);
        var error = (await errorTask).Trim();
        if (mux.ExitCode != 0 || !File.Exists(outputPath))
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? "The microphone track could not be added to the video."
                : $"The microphone track could not be added to the video: {error}");
        }
    }

    private void EnsureIdle()
    {
        if (State is not RecordingState.Idle and not RecordingState.Failed)
        {
            throw new InvalidOperationException("Finish the current recording first.");
        }
    }

    private void StartClock()
    {
        _stopwatch = Stopwatch.StartNew();
        // The HUD only renders whole seconds, so ten ticks a second was nine wasted.
        _elapsedTimer.Change(0, 250);
    }

    private TimeSpan StopClock()
    {
        _elapsedTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _stopwatch?.Stop();
        var duration = _stopwatch?.Elapsed ?? TimeSpan.Zero;
        _stopwatch = null;
        return duration;
    }

    private void SetState(RecordingState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    private void CleanupAudio(bool deleteFile)
    {
        if (_waveIn is not null)
        {
            _waveIn.DataAvailable -= OnAudioData;
            _waveIn.RecordingStopped -= OnAudioStopped;
            _waveIn.Dispose();
            _waveIn = null;
        }
        _waveWriter?.Dispose();
        _waveWriter = null;
        if (deleteFile && _audioReservation is not null)
        {
            File.Delete(_audioReservation.AbsolutePath);
        }
        _audioReservation = null;
        AudioLevel = 0;
    }

    private void CleanupVideo(bool deleteFile)
    {
        if (_videoWaveIn is not null)
        {
            try { _videoWaveIn.StopRecording(); } catch (Exception) { }
            _videoWaveIn.DataAvailable -= OnVideoAudioData;
            _videoWaveIn.RecordingStopped -= OnVideoAudioStopped;
            _videoWaveIn.Dispose();
            _videoWaveIn = null;
        }
        _videoWaveWriter?.Dispose();
        _videoWaveWriter = null;
        _videoProcess?.Dispose();
        _videoProcess = null;
        if (deleteFile)
        {
            DeleteIfExists(_videoReservation?.AbsolutePath);
            DeleteIfExists(_videoRawPath);
            DeleteIfExists(_videoAudioPath);
            DeleteIfExists(_videoMuxPath);
        }
        ClearVideoState();
    }

    private void ClearVideoState()
    {
        _videoReservation = null;
        _videoOptions = null;
        _videoRawPath = null;
        _videoAudioPath = null;
        _videoMuxPath = null;
    }

    private static void DeleteIfExists(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string FormatDuration(TimeSpan duration) => $"{(int)duration.TotalMinutes}:{duration.Seconds:00}";

    private void EnsureAvailableSpace(long minimumBytes, string captureKind)
    {
        try
        {
            var root = Path.GetPathRoot(_store.RootDirectory);
            if (!string.IsNullOrWhiteSpace(root) && new DriveInfo(root).AvailableFreeSpace < minimumBytes)
            {
                throw new IOException($"There is not enough free disk space to start this {captureKind}.");
            }
        }
        catch (ArgumentException)
        {
            // Network and virtual roots do not always expose DriveInfo; the file open remains authoritative.
        }
    }

}
