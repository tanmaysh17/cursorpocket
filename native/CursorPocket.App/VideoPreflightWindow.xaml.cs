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
    // Resolved when the user asked to record, not when Start is pressed.
    private readonly CaptureBounds _displayBounds;
    private readonly int? _displayOutputIndex;
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
    /// <summary>
    /// Serializes camera start and stop. Both are async and several UI events can
    /// trigger them at once — notably setting CameraBox.SelectedItem, which fires
    /// SelectionChanged synchronously while LoadDevicesAsync is still running. Two
    /// interleaved starts used to leave the first renderer orphaned, and an orphaned
    /// frame reader keeps the camera light on even after its MediaCapture is gone.
    /// </summary>
    private readonly SemaphoreSlim _cameraGate = new(1, 1);
    private bool _closing;

    public VideoPreflightWindow(long sourceWindow, CaptureBounds displayBounds, int? displayOutputIndex)
    {
        _sourceWindow = sourceWindow;
        _displayBounds = displayBounds;
        _displayOutputIndex = displayOutputIndex;
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
            // Seeding fires SelectionChanged synchronously, which would start the
            // preview here and race the explicit start below.
            _seeding = true;
            CameraBox.SelectedItem = CursorPocket.Core.Services.MediaDeviceSelector.SelectRemembered(devices.Video, App.Services.Settings.VideoCameraName);
            _seeding = false;
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
        await _cameraGate.WaitAsync();
        try
        {
            await StartCameraPreviewCoreAsync();
        }
        finally
        {
            _cameraGate.Release();
        }
    }

    private async Task StopCameraPreviewAsync()
    {
        await _cameraGate.WaitAsync();
        try
        {
            await StopCameraPreviewCoreAsync();
        }
        finally
        {
            _cameraGate.Release();
        }
    }

    private async Task StartCameraPreviewCoreAsync()
    {
        await StopCameraPreviewCoreAsync();
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
            var started = await CameraEffectRenderer.StartAsync(
                _mediaCapture,
                source,
                ReadEffectSettings(),
                CameraSlotEffectView,
                DispatcherQueue,
                PreviewAspect());
            // Belt and braces against the race above: if anything did slip through
            // and leave a renderer behind, it goes down rather than being stranded.
            if (_previewRenderer is not null)
            {
                await _previewRenderer.DisposeAsync().ConfigureAwait(true);
            }
            _previewRenderer = started;
            if (_previewRenderer is null)
            {
                _cameraPlayer = new MediaPlayer { AutoPlay = true, IsLoopingEnabled = true };
                _cameraPlayer.Source = MediaSource.CreateFromMediaFrameSource(source);
                CameraPreview.SetMediaPlayer(_cameraPlayer);
            }
            ShowCameraSlot(true, string.Empty);
            UpdateEffectAssetsNotice();
            ReportPreviewHealth();
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

    /// <summary>
    /// Keep the facts under the preview honest about what the file will contain.
    /// <para>
    /// Returns early until the whole tree exists. <c>SourceBox</c> carries both
    /// <c>SelectedIndex="0"</c> and a <c>SelectionChanged</c> handler in XAML, so the
    /// handler fires while <c>InitializeComponent</c> is still parsing — at which
    /// point the tags in the right-hand column, further down the document, are still
    /// null. The constructor calls this again once seeding is done.
    /// </para>
    /// </summary>
    private void UpdateSummaryTags()
    {
        if (FrameRateTag is null || MicrophoneTag is null || CountdownTag is null || FramingLabel is null)
        {
            return;
        }
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

    private async Task StopCameraPreviewCoreAsync()
    {
        var renderer = _previewRenderer;
        _previewRenderer = null;
        CameraSlotEffectView.Visibility = Visibility.Collapsed;
        CameraPreview.SetMediaPlayer(null);
        _cameraPlayer?.Dispose();
        _cameraPlayer = null;
        var capture = _mediaCapture;
        _mediaCapture = null;
        if (renderer is not null)
        {
            // Must complete before the MediaCapture below is released, and before
            // the recording self-view opens the same device.
            //
            // ConfigureAwait(false) is load-bearing: this runs from the window's
            // Closed handler, and a continuation posted back to a closing window's
            // dispatcher may never be drained — which left the MediaCapture below
            // undisposed and the camera light on until the process exited.
            await renderer.DisposeAsync().ConfigureAwait(false);
        }
        capture?.Dispose();
    }

    /// <summary>
    /// Says something truthful when the preview stays blank. A dark slot is
    /// otherwise indistinguishable from a camera that simply produced nothing, and
    /// the renderer swallows per-frame problems by design so a recording survives
    /// them — which means the reason has to be pulled out and shown here.
    /// </summary>
    private async void ReportPreviewHealth()
    {
        var renderer = _previewRenderer;
        if (renderer is null)
        {
            return;
        }
        // Long enough for a camera to warm up and hand over its first frames.
        await Task.Delay(2000);
        if (_closing || !ReferenceEquals(renderer, _previewRenderer))
        {
            return;
        }
        if (renderer.FramesPresented > 0)
        {
            CameraStatus.Text = $"Live · {renderer.FramesPresented} frames shown";
            return;
        }
        var diagnosis = renderer.Diagnosis ?? "no frames reached the preview";
        CameraStatus.Text = $"Camera opened but nothing is showing · {diagnosis}";
        ShowCameraSlot(false, "no signal");
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

    private void FramingWell_SizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        UpdateCameraSlotShape();
        _previewRenderer?.SetTargetAspect(PreviewAspect());
    }

    /// <summary>
    /// Aspect the preview crops to. The feed fills the panel, so this follows the
    /// panel rather than the recorded shape — which means the preview shows a little
    /// more than the file will. The auto-framing needs some aspect to work from, and
    /// showing extra context is better than cropping tighter than the recording.
    /// </summary>
    private double PreviewAspect() =>
        FramingWell is { ActualWidth: > 40, ActualHeight: > 40 }
            ? FramingWell.ActualWidth / FramingWell.ActualHeight
            : 0;

    /// <summary>
    /// Reports the recorded shape and framing as a caption over the feed. The feed
    /// itself fills the panel, so the shape is no longer previewed here — the
    /// recording still uses it, and the Shape control names it.
    /// </summary>
    private void UpdateCameraSlotShape()
    {
        if (CameraSlot is null || FramingLabel is null)
        {
            return;
        }
        var shape = (CameraShapeBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "squircle"
            ? "SQUIRCLE 1:1"
            : "ROUNDED 16:9";
        var source = ((SourceBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString()) switch
        {
            "region" => "SELECTED REGION",
            "window" => "PREVIOUS WINDOW",
            _ => "FULL DISPLAY",
        };
        var position = (CameraPositionBox?.SelectedItem as ComboBoxItem)?.Content?.ToString()?.ToUpperInvariant() ?? string.Empty;
        FramingLabel.Text = string.IsNullOrEmpty(position)
            ? $"{source} · {shape}"
            : $"{source} · {shape} · {position}";
        CameraSlot.Visibility = CameraToggle?.IsOn == true ? Visibility.Visible : Visibility.Collapsed;
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
            Bounds = sourceValue == "display" ? _displayBounds : null,
            DisplayOutputIndex = sourceValue == "display" ? _displayOutputIndex : null,
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
        if (_seeding)
        {
            return;
        }
        if (CameraToggle.IsOn)
        {
            await StartCameraPreviewAsync();
        }
    }

    private async void Root_KeyDown(object sender, KeyRoutedEventArgs eventArgs)
    {
        if (eventArgs.Key == VirtualKey.Enter) { eventArgs.Handled = true; await StartAsync(); }
        else if (eventArgs.Key == VirtualKey.Escape) { eventArgs.Handled = true; Cancel(); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs eventArgs) => Cancel();
    private void Cancel() { _closing = true; CleanupDevices(); Close(); }
    /// <summary>
    /// Releases both devices as the window goes away. The camera is torn down twice
    /// on purpose: the orderly async path first, then a direct dispose of the
    /// MediaCapture, because this is reached from <c>Closed</c> and an async
    /// continuation is not guaranteed to run once the window is gone. Holding a
    /// camera open after its window closed is the one outcome worth being blunt
    /// about — the capture light stays on and the next preview finds the device busy.
    /// </summary>
    private void CleanupDevices()
    {
        StopMicrophoneMeter();
        var capture = _mediaCapture;
        _ = StopCameraPreviewAsync();
        try
        {
            capture?.Dispose();
        }
        catch (Exception)
        {
            // Already being disposed by the async path; nothing further to do.
        }
    }

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
