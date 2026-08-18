using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CursorPocket.Core.Models;
using CursorPocket.Core.Services;
using CursorPocket.Core.Storage;
using NAudio.Wave;

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
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _waveWriter;
    private CaptureReservation? _audioReservation;
    private TaskCompletionSource _audioStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public RecordingService(CaptureStore store, string ffmpegPath)
    {
        _store = store;
        _ffmpegPath = ffmpegPath;
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
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await errorTask;
        return ParseDevices(output);
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
        var record = await _store.RegisterExistingAsync(
            CaptureKind.Audio,
            reservation.AbsolutePath,
            $"Audio · {FormatDuration(duration)}",
            new Dictionary<string, object?>
            {
                ["duration_seconds"] = Math.Round(duration.TotalSeconds, 3),
                ["sample_rate"] = 48000,
                ["channels"] = 1,
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
        _videoError.Clear();
        var command = FfmpegCommandBuilder.Build(_ffmpegPath, _videoReservation.AbsolutePath, options);
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
        await Task.Delay(350, cancellationToken);
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
        SetState(RecordingState.Finalizing);
        var duration = StopClock();
        var process = _videoProcess;
        var reservation = _videoReservation;
        var options = _videoOptions;
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
            CleanupVideo(deleteFile: discard);
        }
        if (discard || !File.Exists(reservation.AbsolutePath) || new FileInfo(reservation.AbsolutePath).Length < 1024)
        {
            SetState(RecordingState.Idle);
            return null;
        }
        var record = await _store.RegisterExistingAsync(
            CaptureKind.Video,
            reservation.AbsolutePath,
            $"Video · {FormatDuration(duration)}",
            new Dictionary<string, object?>
            {
                ["duration_seconds"] = Math.Round(duration.TotalSeconds, 3),
                ["fps"] = options.FramesPerSecond,
                ["source_kind"] = options.SourceKind.ToString().ToLowerInvariant(),
                ["include_microphone"] = options.IncludeMicrophone,
                ["microphone_name"] = options.MicrophoneName,
                ["include_camera"] = options.IncludeCamera,
                ["camera_name"] = options.CameraName,
                ["camera_position"] = options.CameraPosition,
                ["camera_width"] = options.CameraWidth,
            },
            cancellationToken);
        SetState(RecordingState.Idle);
        return record;
    }

    public void Dispose()
    {
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
        _elapsedTimer.Change(0, 100);
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
        _videoProcess?.Dispose();
        _videoProcess = null;
        if (deleteFile && _videoReservation is not null)
        {
            File.Delete(_videoReservation.AbsolutePath);
        }
        _videoReservation = null;
        _videoOptions = null;
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

    private static (IReadOnlyList<MediaDeviceDescriptor> Audio, IReadOnlyList<MediaDeviceDescriptor> Video) ParseDevices(string output)
    {
        var audio = new List<MediaDeviceDescriptor>();
        var video = new List<MediaDeviceDescriptor>();
        string? section = null;
        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Contains("DirectShow video devices", StringComparison.OrdinalIgnoreCase))
            {
                section = "video";
                continue;
            }
            if (line.Contains("DirectShow audio devices", StringComparison.OrdinalIgnoreCase))
            {
                section = "audio";
                continue;
            }
            var match = Regex.Match(line, "\\\"(?<name>[^\\\"]+)\\\"");
            if (!match.Success || line.Contains("Alternative name", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var name = match.Groups["name"].Value;
            if (section == "video" && video.All(device => device.Name != name))
            {
                video.Add(new MediaDeviceDescriptor(name, name, "video", video.Count == 0));
            }
            else if (section == "audio" && audio.All(device => device.Name != name))
            {
                audio.Add(new MediaDeviceDescriptor(name, name, "audio", audio.Count == 0));
            }
        }
        return (audio, video);
    }
}
