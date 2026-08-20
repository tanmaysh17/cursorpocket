using CursorPocket.Core.Models;
using CursorPocket.Core.Services;
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
    private CameraEffectRenderer? _previewRenderer;
    private string _customBackgroundPath = string.Empty;
    private int _lastBackgroundIndex;
    /// <summary>
    /// True while the constructor seeds controls from settings. Assigning
    /// SelectedIndex fires SelectionChanged synchronously, and the background
    /// handler opens a file picker — which must never happen unprompted just
    /// because the user previously chose a custom image.
    /// </summary>
    private bool _seeding = true;
    private bool _closing;

    public VideoPreflightWindow(long sourceWindow)
    {
        _sourceWindow = sourceWindow;
        InitializeComponent();
        WindowPlacement.ResizeInDips(this, 940, 720);
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = true;
        }
        MicrophoneToggle.IsOn = App.Services.Settings.VideoMicrophoneEnabled;
        CameraToggle.IsOn = App.Services.Settings.VideoCameraEnabled;
        PointerToggle.IsOn = App.Services.Settings.VideoDrawCursor;
        FrameRateBox.SelectedIndex = App.Services.Settings.VideoFramesPerSecond == 60 ? 1 : 0;
        CountdownBox.SelectedIndex = App.Services.Settings.VideoCountdownSeconds switch { 0 => 0, 5 => 2, _ => 1 };
        SourceBox.SelectedIndex = App.Services.Settings.VideoSourceKind switch { "region" => 1, "window" => 2, _ => 0 };
        CameraPositionBox.SelectedIndex = App.Services.Settings.VideoCameraPosition switch { "bottom-left" => 1, "top-right" => 2, "top-left" => 3, _ => 0 };
        CameraSizeBox.SelectedIndex = App.Services.Settings.VideoCameraWidth switch { 240 => 0, 480 => 2, _ => 1 };
        NoiseSuppressionToggle.IsOn = App.Services.Settings.AudioNoiseSuppression;
        AutoLevelToggle.IsOn = App.Services.Settings.AudioAutoLevel;
        CameraShapeBox.SelectedIndex = App.Services.Settings.VideoCameraShape == "squircle" ? 1 : 0;
        CameraTouchUpBox.SelectedIndex = Math.Clamp(App.Services.Settings.VideoCameraTouchUp, 0, 2);
        SeedBackgroundSelection(App.Services.Settings.VideoCameraBackground, App.Services.Settings.VideoCameraBackgroundImage);
        BrightnessSlider.Value = App.Services.Settings.VideoCameraBrightness;
        WarmthSlider.Value = App.Services.Settings.VideoCameraWarmth;
        ContrastSlider.Value = App.Services.Settings.VideoCameraContrast;
        UpdateEffectValueReadouts();
        UpdateCameraSlotShape();
        _seeding = false;
        CameraOptions.Visibility = CameraToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        UpdateSummaryTags();
        FrameRateBox.SelectionChanged += Summary_SelectionChanged;
        CountdownBox.SelectionChanged += Summary_SelectionChanged;
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
            ReadinessDot.Fill = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PocketGreen"];
            var diskStatus = GetDiskSpaceStatus();
            ReadinessDetail.Text = File.Exists(App.Services.FfmpegPath)
                ? $"Recording stays local · {diskStatus}"
                : "FFmpeg is missing; rebuild or repair CursorPocket before recording.";
            StartButton.IsEnabled = File.Exists(App.Services.FfmpegPath);
            StartMicrophoneMeter();
            UpdateCameraSourceNotice();
            if (CameraToggle.IsOn)
            {
                await StartCameraPreviewAsync();
            }
        }
        catch (Exception error)
        {
            ReadinessTitle.Text = "Recording devices need attention";
            ReadinessDot.Fill = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PocketRed"];
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
            ShowCameraSlot(false, CameraToggle.IsOn ? "no camera" : "camera off");
            CameraStatus.Text = CameraToggle.IsOn ? "No camera is available" : "Off · no camera device is opened";
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
            // The preview always goes through the effect renderer so the slot
            // shows exactly the frames the recording self-view will render.
            _previewRenderer = await CameraEffectRenderer.StartAsync(_mediaCapture, source, ReadEffectSettings(), CameraSlotEffectView, DispatcherQueue);
            if (_previewRenderer is null)
            {
                _cameraPlayer = new MediaPlayer { AutoPlay = true, IsLoopingEnabled = true };
                _cameraPlayer.Source = MediaSource.CreateFromMediaFrameSource(source);
                CameraPreview.SetMediaPlayer(_cameraPlayer);
            }
            ShowCameraSlot(true, string.Empty);
            UpdateEffectAssetsNotice();
        }
        catch (Exception error)
        {
            ShowCameraSlot(false, "camera error");
            CameraStatus.Text = error.Message;
        }
    }

    private void ShowCameraSlot(bool live, string label)
    {
        CameraPreview.Visibility = live && _previewRenderer is null ? Visibility.Visible : Visibility.Collapsed;
        CameraSlotEffectView.Visibility = live && _previewRenderer is not null ? Visibility.Visible : Visibility.Collapsed;
        CameraSlotLabel.Visibility = live ? Visibility.Collapsed : Visibility.Visible;
        CameraSlotLabel.Text = label;
    }

    /// <summary>Keep the facts under the preview honest about what the file will contain.</summary>
    private void UpdateSummaryTags()
    {
        FrameRateTag.Text = (FrameRateBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "60" ? "60 FPS" : "30 FPS";
        MicrophoneTag.Text = MicrophoneToggle.IsOn ? "MIC ON" : "MIC OFF";
        var countdown = (CountdownBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "3";
        CountdownTag.Text = countdown == "0" ? "NO COUNTDOWN" : $"{countdown} S";
        FramingLabel.Text = ((SourceBox.SelectedItem as ComboBoxItem)?.Tag?.ToString()) switch
        {
            "region" => "SELECTED REGION",
            "window" => "PREVIOUS WINDOW",
            _ => "FULL DISPLAY",
        };
    }

    private async Task StopCameraPreviewAsync()
    {
        var renderer = _previewRenderer;
        _previewRenderer = null;
        CameraSlotEffectView.Visibility = Visibility.Collapsed;
        CameraPreview.SetMediaPlayer(null);
        _cameraPlayer?.Dispose();
        _cameraPlayer = null;
        if (renderer is not null)
        {
            // Must complete before the MediaCapture below is released, and
            // before the recording self-view opens the same device.
            await renderer.DisposeAsync();
        }
        _mediaCapture?.Dispose();
        _mediaCapture = null;
    }

    /// <summary>The effect configuration currently described by the controls.</summary>
    private CursorPocket.Core.Media.CameraEffectSettings ReadEffectSettings()
    {
        var (mode, imagePath) = ReadBackgroundSelection();
        return new CursorPocket.Core.Media.CameraEffectSettings
        {
            BackgroundMode = mode,
            BackgroundImagePath = imagePath,
            TouchUpLevel = int.TryParse((CameraTouchUpBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var touchUp) ? touchUp : 0,
            Brightness = (int)Math.Round(BrightnessSlider.Value),
            Warmth = (int)Math.Round(WarmthSlider.Value),
            Contrast = (int)Math.Round(ContrastSlider.Value),
        };
    }

    private (string Mode, string ImagePath) ReadBackgroundSelection()
    {
        var tag = (CameraBackgroundBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "none";
        return tag switch
        {
            "none" => ("none", string.Empty),
            "blur" => ("blur", string.Empty),
            "custom" => string.IsNullOrWhiteSpace(_customBackgroundPath) ? ("none", string.Empty) : ("image", _customBackgroundPath),
            _ => ("image", tag),
        };
    }

    private void SeedBackgroundSelection(string mode, string imagePath)
    {
        var index = 0;
        if (mode == "blur")
        {
            index = 1;
        }
        else if (mode == "image" && !string.IsNullOrWhiteSpace(imagePath))
        {
            index = 5;
            for (var item = 2; item <= 4; item++)
            {
                if (string.Equals((CameraBackgroundBox.Items[item] as ComboBoxItem)?.Tag?.ToString(), imagePath, StringComparison.OrdinalIgnoreCase))
                {
                    index = item;
                    break;
                }
            }
            if (index == 5)
            {
                _customBackgroundPath = imagePath;
            }
        }
        CameraBackgroundBox.SelectedIndex = index;
        _lastBackgroundIndex = index;
    }

    private void UpdateEffectValueReadouts()
    {
        // Slider events can fire while XAML is still constructing siblings.
        if (BrightnessValue is null || WarmthValue is null || ContrastValue is null)
        {
            return;
        }
        BrightnessValue.Text = ((int)Math.Round(BrightnessSlider.Value)).ToString();
        WarmthValue.Text = ((int)Math.Round(WarmthSlider.Value)).ToString();
        ContrastValue.Text = ((int)Math.Round(ContrastSlider.Value)).ToString();
    }

    private void UpdateEffectAssetsNotice()
    {
        if (EffectAssetsNotice is null)
        {
            return;
        }
        var needsSegmentation = ReadEffectSettings().NeedsSegmentation;
        EffectAssetsNotice.Visibility = needsSegmentation && _previewRenderer is not null && !_previewRenderer.SegmentationAvailable
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>The squircle records 1:1; keep the framing preview honest about that.</summary>
    private void UpdateCameraSlotShape()
    {
        if (CameraSlot is null)
        {
            return;
        }
        var squircle = (CameraShapeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "squircle";
        CameraSlot.Width = squircle ? 88 : 132;
        CameraSlot.CornerRadius = new CornerRadius(squircle ? 26 : 8);
    }

    private async void CameraEffect_SelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        UpdateCameraSlotShape();
        if (!_seeding)
        {
            await PushEffectSettingsAsync();
        }
    }

    private async void CameraEffect_SliderChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs eventArgs)
    {
        UpdateEffectValueReadouts();
        if (!_seeding)
        {
            await PushEffectSettingsAsync();
        }
    }

    private async void CameraBackgroundBox_SelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (_seeding)
        {
            return;
        }
        var tag = (CameraBackgroundBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        // Re-picking is only prompted when there is nothing remembered, so
        // reselecting "Custom image…" keeps the image the user already chose.
        if (tag == "custom" && string.IsNullOrWhiteSpace(_customBackgroundPath))
        {
            var picked = await PickCustomBackgroundAsync();
            if (!picked)
            {
                CameraBackgroundBox.SelectedIndex = _lastBackgroundIndex;
                return;
            }
        }
        _lastBackgroundIndex = CameraBackgroundBox.SelectedIndex;
        await PushEffectSettingsAsync();
    }

    private async Task<bool> PickCustomBackgroundAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
            };
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".bmp");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return false;
            }
            _customBackgroundPath = file.Path;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task PushEffectSettingsAsync()
    {
        if (_previewRenderer is null)
        {
            return;
        }
        await _previewRenderer.UpdateSettingsAsync(ReadEffectSettings());
        UpdateEffectAssetsNotice();
    }

    private async void Start_Click(object sender, RoutedEventArgs eventArgs) => await StartAsync();

    private async Task StartAsync()
    {
        if (_closing || !StartButton.IsEnabled)
        {
            return;
        }
        _closing = true;
        StopMicrophoneMeter();
        await StopCameraPreviewAsync();
        var sourceValue = (SourceBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "display";
        var microphone = MicrophoneBox.SelectedItem as MediaDeviceDescriptor;
        var camera = CameraBox.SelectedItem as MediaDeviceDescriptor;
        var effects = ReadEffectSettings();
        var options = new RecordingOptions
        {
            SourceKind = sourceValue switch { "region" => VideoSourceKind.Region, "window" => VideoSourceKind.Window, _ => VideoSourceKind.Display },
            WindowHandle = sourceValue == "window" ? _sourceWindow : null,
            IncludeMicrophone = MicrophoneToggle.IsOn && microphone is not null,
            MicrophoneId = microphone?.Id ?? string.Empty,
            MicrophoneName = microphone?.Name ?? string.Empty,
            NoiseSuppression = NoiseSuppressionToggle.IsOn,
            AutoLevel = AutoLevelToggle.IsOn,
            IncludeCamera = CameraToggle.IsOn && camera is not null,
            CameraName = camera?.Name ?? string.Empty,
            CameraPosition = (CameraPositionBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "bottom-right",
            CameraWidth = int.TryParse((CameraSizeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var cameraWidth) ? cameraWidth : 360,
            CameraShape = (CameraShapeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "rounded",
            CameraBackgroundMode = effects.BackgroundMode,
            CameraBackgroundImagePath = effects.BackgroundImagePath,
            CameraTouchUpLevel = effects.TouchUpLevel,
            CameraBrightness = effects.Brightness,
            CameraWarmth = effects.Warmth,
            CameraContrast = effects.Contrast,
            FramesPerSecond = int.TryParse((FrameRateBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var fps) ? fps : 30,
            CountdownSeconds = int.TryParse((CountdownBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var countdown) ? countdown : 3,
            DrawCursor = PointerToggle.IsOn,
            DisplayIndex = sourceValue == "display" ? WindowPlacement.DisplayIndexUnderPointer() : 0,
        };
        RecordingRequested?.Invoke(this, options);
        Close();
    }

    private async void CameraToggle_Toggled(object sender, RoutedEventArgs eventArgs)
    {
        CameraBox.IsEnabled = CameraToggle.IsOn;
        CameraPositionBox.IsEnabled = CameraToggle.IsOn;
        CameraSizeBox.IsEnabled = CameraToggle.IsOn;
        CameraShapeBox.IsEnabled = CameraToggle.IsOn;
        CameraTouchUpBox.IsEnabled = CameraToggle.IsOn;
        CameraBackgroundBox.IsEnabled = CameraToggle.IsOn;
        BrightnessSlider.IsEnabled = CameraToggle.IsOn;
        WarmthSlider.IsEnabled = CameraToggle.IsOn;
        ContrastSlider.IsEnabled = CameraToggle.IsOn;
        if (CameraToggle.IsOn) await StartCameraPreviewAsync(); else await StopCameraPreviewAsync();
        if (!CameraToggle.IsOn)
        {
            ShowCameraSlot(false, "camera off");
            CameraStatus.Text = "Off · no camera device is opened";
        }
        CameraOptions.Visibility = CameraToggle.IsOn ? Visibility.Visible : Visibility.Collapsed;
        UpdateSummaryTags();
        UpdateCameraSourceNotice();
    }

    private void SourceBox_SelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        UpdateSummaryTags();
        UpdateCameraSourceNotice();
    }

    /// <summary>
    /// The camera is recorded by being on screen inside the captured area, so a
    /// window recording keeps the live self-view but cannot carry it into the file.
    /// Say so here rather than letting the user discover it after recording.
    /// </summary>
    private void UpdateCameraSourceNotice()
    {
        if (CameraSourceNotice is null)
        {
            return;
        }
        var sourceValue = (SourceBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "display";
        var sourceKind = sourceValue switch
        {
            "region" => VideoSourceKind.Region,
            "window" => VideoSourceKind.Window,
            _ => VideoSourceKind.Display,
        };
        CameraSourceNotice.Visibility = CameraToggle.IsOn && !CameraSelfViewPlacement.IsRecordedForSource(sourceKind)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void MicrophoneToggle_Toggled(object sender, RoutedEventArgs eventArgs)
    {
        MicrophoneBox.IsEnabled = MicrophoneToggle.IsOn;
        StartMicrophoneMeter();
        UpdateSummaryTags();
    }

    private void Summary_SelectionChanged(object sender, SelectionChangedEventArgs eventArgs) => UpdateSummaryTags();

    private void MicrophoneBox_SelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (MicrophoneToggle.IsOn && _microphoneMonitor is not null)
        {
            StartMicrophoneMeter();
        }
    }

    private async void MoreOptions_Expanding(Expander sender, ExpanderExpandingEventArgs eventArgs)
    {
        // Let the expanded content participate in layout, then reveal it so
        // opening More never looks like an empty, clipped panel.
        await Task.Delay(80);
        OptionsScroll.ChangeView(null, OptionsScroll.ScrollableHeight, null, false);
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
