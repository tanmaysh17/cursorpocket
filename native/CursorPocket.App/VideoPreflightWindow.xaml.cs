using CursorPocket.Core.Models;
using CursorPocket_App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using NAudio.Wave;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.System;
using Windows.Graphics;

namespace CursorPocket_App;

public sealed partial class VideoPreflightWindow : Window
{
    private readonly long _sourceWindow;
    private WaveInEvent? _microphoneMonitor;
    private MediaCapture? _mediaCapture;
    private MediaPlayer? _cameraPlayer;
    private bool _closing;

    public VideoPreflightWindow(long sourceWindow)
    {
        _sourceWindow = sourceWindow;
        InitializeComponent();
        AppWindow.Resize(new SizeInt32(940, 720));
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = true;
        }
        NativeMethods.SetWindowDisplayAffinity(WinRT.Interop.WindowNative.GetWindowHandle(this), NativeMethods.WdaExcludeFromCapture);
        MicrophoneToggle.IsOn = App.Services.Settings.VideoMicrophoneEnabled;
        CameraToggle.IsOn = App.Services.Settings.VideoCameraEnabled;
        PointerToggle.IsOn = App.Services.Settings.VideoDrawCursor;
        FrameRateBox.SelectedIndex = App.Services.Settings.VideoFramesPerSecond == 60 ? 1 : 0;
        CountdownBox.SelectedIndex = App.Services.Settings.VideoCountdownSeconds switch { 0 => 0, 5 => 2, _ => 1 };
        SourceBox.SelectedIndex = App.Services.Settings.VideoSourceKind switch { "region" => 1, "window" => 2, _ => 0 };
        CameraPositionBox.SelectedIndex = App.Services.Settings.VideoCameraPosition switch { "bottom-left" => 1, "top-right" => 2, "top-left" => 3, _ => 0 };
        CameraSizeBox.SelectedIndex = App.Services.Settings.VideoCameraWidth switch { 240 => 0, 480 => 2, _ => 1 };
        Activated += OnActivated;
        Closed += (_, _) => CleanupDevices();
    }

    public event EventHandler<RecordingOptions>? RecordingRequested;

    private async void OnActivated(object sender, WindowActivatedEventArgs eventArgs)
    {
        Activated -= OnActivated;
        Root.Focus(FocusState.Programmatic);
        await LoadDevicesAsync();
    }

    private async Task LoadDevicesAsync()
    {
        try
        {
            var devices = await App.Services.Recording.GetVideoDevicesAsync();
            MicrophoneBox.ItemsSource = devices.Audio;
            CameraBox.ItemsSource = devices.Video;
            MicrophoneBox.SelectedItem = CursorPocket.Core.Services.MediaDeviceSelector.SelectRemembered(devices.Audio, App.Services.Settings.VideoMicrophoneName);
            CameraBox.SelectedItem = CursorPocket.Core.Services.MediaDeviceSelector.SelectRemembered(devices.Video, App.Services.Settings.VideoCameraName);
            MicrophoneToggle.IsEnabled = devices.Audio.Count > 0;
            CameraToggle.IsEnabled = devices.Video.Count > 0;
            ReadinessTitle.Text = "Ready when you are";
            var diskStatus = GetDiskSpaceStatus();
            ReadinessDetail.Text = File.Exists(App.Services.FfmpegPath)
                ? $"Recording stays local · {diskStatus}"
                : "FFmpeg is missing; rebuild or repair CursorPocket before recording.";
            StartButton.IsEnabled = File.Exists(App.Services.FfmpegPath);
            StartMicrophoneMeter();
            if (CameraToggle.IsOn)
            {
                await StartCameraPreviewAsync();
            }
        }
        catch (Exception error)
        {
            ReadinessTitle.Text = "Recording devices need attention";
            ReadinessDetail.Text = error.Message;
            StartButton.IsEnabled = false;
        }
    }

    private void StartMicrophoneMeter()
    {
        StopMicrophoneMeter();
        if (!MicrophoneToggle.IsOn || WaveIn.DeviceCount < 1)
        {
            MicrophoneStatus.Text = MicrophoneToggle.IsOn ? "No microphone is available" : "Microphone is off";
            return;
        }
        var selected = MicrophoneBox.SelectedItem as MediaDeviceDescriptor;
        var deviceNumber = int.TryParse(selected?.Id, out var parsed) ? parsed : 0;
        _microphoneMonitor = new WaveInEvent { DeviceNumber = deviceNumber, WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 80 };
        _microphoneMonitor.DataAvailable += (_, args) =>
        {
            var peak = 0d;
            for (var index = 0; index + 1 < args.BytesRecorded; index += 2)
            {
                peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(args.Buffer, index) / 32768d));
            }
            DispatcherQueue.TryEnqueue(() =>
            {
                MicrophoneLevel.Value = Math.Min(1, peak * 2.4);
                MicrophoneStatus.Text = peak > 0.01 ? "Signal detected" : "Listening · speak to test the level";
            });
        };
        try
        {
            _microphoneMonitor.StartRecording();
        }
        catch (Exception error)
        {
            MicrophoneStatus.Text = error.Message;
        }
    }

    private void StopMicrophoneMeter()
    {
        if (_microphoneMonitor is null)
        {
            return;
        }
        try { _microphoneMonitor.StopRecording(); } catch (Exception) { }
        _microphoneMonitor.Dispose();
        _microphoneMonitor = null;
        MicrophoneLevel.Value = 0;
    }

    private async Task StartCameraPreviewAsync()
    {
        await StopCameraPreviewAsync();
        if (!CameraToggle.IsOn || CameraBox.SelectedItem is not MediaDeviceDescriptor selected)
        {
            CameraPreview.Visibility = Visibility.Collapsed;
            CameraEmpty.Visibility = Visibility.Visible;
            CameraStatus.Text = CameraToggle.IsOn ? "No camera is available" : "Camera is off";
            return;
        }
        try
        {
            var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            var device = devices.FirstOrDefault(item => string.Equals(item.Name, selected.Name, StringComparison.OrdinalIgnoreCase)) ?? devices.FirstOrDefault();
            if (device is null)
            {
                throw new InvalidOperationException("Windows did not expose this camera for preview.");
            }
            _mediaCapture = new MediaCapture();
            await _mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = device.Id,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
            });
            var source = _mediaCapture.FrameSources.Values.FirstOrDefault(frame => frame.Info.SourceKind == MediaFrameSourceKind.Color);
            if (source is null)
            {
                throw new InvalidOperationException("This camera did not provide a color preview.");
            }
            _cameraPlayer = new MediaPlayer { AutoPlay = true, IsLoopingEnabled = true };
            _cameraPlayer.Source = MediaSource.CreateFromMediaFrameSource(source);
            CameraPreview.SetMediaPlayer(_cameraPlayer);
            CameraPreview.Visibility = Visibility.Visible;
            CameraEmpty.Visibility = Visibility.Collapsed;
        }
        catch (Exception error)
        {
            CameraPreview.Visibility = Visibility.Collapsed;
            CameraEmpty.Visibility = Visibility.Visible;
            CameraStatus.Text = error.Message;
        }
    }

    private Task StopCameraPreviewAsync()
    {
        CameraPreview.SetMediaPlayer(null);
        _cameraPlayer?.Dispose();
        _cameraPlayer = null;
        _mediaCapture?.Dispose();
        _mediaCapture = null;
        return Task.CompletedTask;
    }

    private async void Start_Click(object sender, RoutedEventArgs eventArgs) => await StartAsync();

    private async Task StartAsync()
    {
        if (_closing)
        {
            return;
        }
        _closing = true;
        StopMicrophoneMeter();
        await StopCameraPreviewAsync();
        var sourceValue = (SourceBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "display";
        var microphone = MicrophoneBox.SelectedItem as MediaDeviceDescriptor;
        var camera = CameraBox.SelectedItem as MediaDeviceDescriptor;
        var options = new RecordingOptions
        {
            SourceKind = sourceValue switch { "region" => VideoSourceKind.Region, "window" => VideoSourceKind.Window, _ => VideoSourceKind.Display },
            WindowHandle = sourceValue == "window" ? _sourceWindow : null,
            IncludeMicrophone = MicrophoneToggle.IsOn && microphone is not null,
            MicrophoneName = microphone?.Name ?? string.Empty,
            IncludeCamera = CameraToggle.IsOn && camera is not null,
            CameraName = camera?.Name ?? string.Empty,
            CameraPosition = (CameraPositionBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "bottom-right",
            CameraWidth = int.TryParse((CameraSizeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var cameraWidth) ? cameraWidth : 360,
            FramesPerSecond = int.TryParse((FrameRateBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var fps) ? fps : 30,
            CountdownSeconds = int.TryParse((CountdownBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var countdown) ? countdown : 3,
            DrawCursor = PointerToggle.IsOn,
        };
        RecordingRequested?.Invoke(this, options);
        Close();
    }

    private async void CameraToggle_Toggled(object sender, RoutedEventArgs eventArgs)
    {
        CameraBox.IsEnabled = CameraToggle.IsOn;
        CameraPositionBox.IsEnabled = CameraToggle.IsOn;
        CameraSizeBox.IsEnabled = CameraToggle.IsOn;
        if (CameraToggle.IsOn) await StartCameraPreviewAsync(); else await StopCameraPreviewAsync();
        if (!CameraToggle.IsOn) { CameraPreview.Visibility = Visibility.Collapsed; CameraEmpty.Visibility = Visibility.Visible; CameraStatus.Text = "Camera is off"; }
    }

    private void MicrophoneToggle_Toggled(object sender, RoutedEventArgs eventArgs)
    {
        MicrophoneBox.IsEnabled = MicrophoneToggle.IsOn;
        StartMicrophoneMeter();
    }

    private void MicrophoneBox_SelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (MicrophoneToggle.IsOn && _microphoneMonitor is not null)
        {
            StartMicrophoneMeter();
        }
    }

    private async void CameraBox_SelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (CameraToggle.IsOn) await StartCameraPreviewAsync();
    }

    private async void Root_KeyDown(object sender, KeyRoutedEventArgs eventArgs)
    {
        if (eventArgs.Key == VirtualKey.Enter) { eventArgs.Handled = true; await StartAsync(); }
        else if (eventArgs.Key == VirtualKey.Escape) { eventArgs.Handled = true; Cancel(); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs eventArgs) => Cancel();
    private void Cancel() { _closing = true; CleanupDevices(); Close(); }
    private void CleanupDevices() { StopMicrophoneMeter(); _ = StopCameraPreviewAsync(); }

    private static string GetDiskSpaceStatus()
    {
        try
        {
            var root = Path.GetPathRoot(App.Services.Settings.CaptureDirectory);
            var drive = string.IsNullOrWhiteSpace(root) ? null : new DriveInfo(root);
            return drive is null ? "disk space checked when recording starts" : $"{drive.AvailableFreeSpace / 1_073_741_824d:0.0} GB free";
        }
        catch (Exception)
        {
            return "disk space checked when recording starts";
        }
    }
}
