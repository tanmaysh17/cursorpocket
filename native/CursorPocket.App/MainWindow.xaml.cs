using System.Drawing;
using System.Runtime.InteropServices.WindowsRuntime;
using CursorPocket.Core.Annotations;
using CursorPocket.Core.Models;
using CursorPocket_App.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace CursorPocket_App;

public sealed partial class MainWindow : Window
{
    private CommandPaletteWindow? _palette;
    private VideoPreflightWindow? _preflight;
    private CameraSelfViewWindow? _selfView;
    private NativeCompanionWindow? _companion;
    private RecordingService? _subscribedRecording;
    private MouseActivityService? _mouseActivity;
    private System.Windows.Forms.NotifyIcon? _tray;
    private System.Windows.Forms.ContextMenuStrip? _trayMenu;
    private System.Windows.Forms.ToolStripMenuItem? _companionTrayItem;
    private long _lastSourceWindow;
    private (CaptureBounds Bounds, int? OutputIndex)? _displayTarget;
    private CaptureBounds? _lastRegion;
    private bool _openLibraryAfterPaletteCloses;
    private bool _quitting;
    private readonly ReceiptCoordinator _receipts;

    // Pins are held only for their lifetime and never restored after a restart: a window
    // that reappears after a reboot with no explanation is exactly the unexplained
    // floating widget the anti-references warn against. The Library holds the durable copy.
    private readonly List<PinnedCaptureWindow> _pins = [];

    public MainWindow()
    {
        InitializeComponent();
        App.Theme.Register(this, Root, SurfaceRole.Persistent);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        WindowPlacement.ResizeInDips(this, 1100, 760);
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 720;
            presenter.PreferredMinimumHeight = 540;
        }
        RestoreGeometry();
        AppWindow.Closing += AppWindow_Closing;
        RootFrame.Navigate(typeof(MainPage));
        InitializeCommandPalette();
        InitializeCompanion();
        InitializeTray();
        App.Theme.ThemeChanged += Theme_ThemeChanged;
        SubscribeToRecordingState();
        App.Services.SettingsChanged += Services_SettingsChanged;
        _receipts = new ReceiptCoordinator(ShowLibrary);
    }

    public void ShowLibrary()
    {
        AppWindow.Show(true);
        var page = RootFrame.Content as MainPage;
        page?.NavigateTo("library");
        _ = page?.EnsureLibraryLoadedAsync();
        ActivateMainWindow();
    }

    public void ShowSettings()
    {
        AppWindow.Show(true);
        var page = RootFrame.Content as MainPage;
        page?.NavigateTo("settings");
        _ = page?.EnsureLibraryLoadedAsync();
        ActivateMainWindow();
    }

    private void ActivateMainWindow() => WindowPlacement.ForceForeground(this);

    public void ShowCommandPalette(string? initialMode = null)
    {
        var source = App.Services.Context.SnapshotForegroundWindow();
        if (source != 0)
        {
            _lastSourceWindow = source;
        }
        SnapshotDisplayTarget();
        _palette!.Show(_lastSourceWindow, initialMode);
    }

    /// <summary>
    /// Remembers which screen the user was on when they asked for CursorPocket. It
    /// cannot be resolved later from the pointer: by the time Start is pressed the
    /// pointer is over the preflight window, which Windows may have opened on
    /// another display.
    /// </summary>
    private void SnapshotDisplayTarget() => _displayTarget = WindowPlacement.DisplayTargetUnderPointer();

    private void InitializeCommandPalette()
    {
        _palette = new CommandPaletteWindow();
        _palette.CommandRequested += Palette_CommandRequested;
        _palette.PaletteHidden += (_, _) =>
        {
            if (_openLibraryAfterPaletteCloses)
            {
                _openLibraryAfterPaletteCloses = false;
                ShowLibrary();
            }
        };
    }

    public void ShowVideoPreflight()
    {
        if (_preflight is not null)
        {
            _preflight.Activate();
            return;
        }
        var source = App.Services.Context.SnapshotForegroundWindow();
        if (source != 0)
        {
            _lastSourceWindow = source;
        }
        // Opened straight from the tray rather than through command mode, so the
        // pointer is still on the screen the user means.
        _displayTarget ??= WindowPlacement.DisplayTargetUnderPointer();
        var target = _displayTarget.Value;
        _preflight = new VideoPreflightWindow(_lastSourceWindow, target.Bounds, target.OutputIndex);
        _preflight.RecordingRequested += async (_, options) => await StartVideoAsync(options);
        _preflight.Closed += (_, _) =>
        {
            _preflight = null;
            App.Services.Context.RestoreFocus(_lastSourceWindow);
        };
        _preflight.Activate();
    }

    public async Task ToggleAudioRecordingAsync()
    {
        try
        {
            if (App.Services.RecordingSession.IsActive && !App.Services.RecordingSession.IsVideo)
            {
                var record = await App.Services.RecordingSession.FinishAsync();
                if (record is not null)
                {
                    ShowReceipt(record, "Audio note saved");
                }
                return;
            }
            var microphones = App.Services.Recording.GetMicrophones();
            var microphone = CursorPocket.Core.Services.MediaDeviceSelector.SelectRemembered(microphones, App.Services.Settings.VideoMicrophoneName);
            App.Services.Context.RestoreFocus(_lastSourceWindow);
            await App.Services.RecordingSession.StartAudioAsync(microphone?.Id);
            RecordingHudWindow.ShowForAudio(
                microphone?.Name ?? "Default microphone",
                async discard =>
                {
                    CaptureRecord? record = null;
                    if (discard) await App.Services.RecordingSession.DiscardAsync();
                    else record = await App.Services.RecordingSession.FinishAsync();
                    if (record is not null) ShowReceipt(record, "Audio note saved");
                    else ShowError(discard ? "Audio note discarded" : "Audio note was not saved", "No file was created.");
                });
        }
        catch (Exception error)
        {
            ShowError("Audio did not start", error.Message);
        }
    }

    public async Task CaptureTextAsync()
    {
        var source = _lastSourceWindow != 0 ? _lastSourceWindow : App.Services.Context.SnapshotForegroundWindow();
        try
        {
            var text = await App.Services.Context.ReadSelectedTextAsync(source);
            if (string.IsNullOrWhiteSpace(text))
            {
                ShowError("Nothing was captured", "Highlight text in another app, then open CursorPocket and press T.");
                return;
            }
            var record = await App.Services.CaptureStore.SaveTextAsync(text);
            ShowReceipt(record, "Text snippet saved");
        }
        catch (Exception error)
        {
            ShowError("Text was not saved", error.Message);
        }
        finally
        {
            App.Services.Context.RestoreFocus(source);
        }
    }

    public async Task CaptureLinkAsync()
    {
        var source = _lastSourceWindow != 0 ? _lastSourceWindow : App.Services.Context.SnapshotForegroundWindow();
        try
        {
            var link = await App.Services.Context.ReadBrowserLinkAsync(source);
            if (string.IsNullOrWhiteSpace(link))
            {
                ShowError("No web page found", "CursorPocket saves a link only when Chrome, Edge, Firefox, Brave, Vivaldi, or Opera is active.");
                return;
            }
            var record = await App.Services.CaptureStore.SaveLinkAsync(link);
            ShowReceipt(record, "Web link saved");
        }
        catch (Exception error)
        {
            ShowError("Link was not saved", error.Message);
        }
        finally
        {
            App.Services.Context.RestoreFocus(source);
        }
    }

    private async void Palette_CommandRequested(object? sender, CaptureActionId command)
    {
        var source = sender is CommandPaletteWindow palette ? palette.SourceWindow : _lastSourceWindow;
        _lastSourceWindow = source;
        if (command == CaptureActionId.Library)
        {
            // Showing a persistent window from the palette's close callback
            // avoids Windows reactivating the source after we show Library.
            _openLibraryAfterPaletteCloses = true;
            return;
        }
        switch (command)
        {
            case CaptureActionId.Video: ShowVideoPreflight(); break;
            case CaptureActionId.RepeatVideo: await StartVideoAsync(BuildRememberedVideoOptions()); break;
            case CaptureActionId.Audio: await ToggleAudioRecordingAsync(); break;
            case CaptureActionId.Text: await CaptureTextAsync(); break;
            case CaptureActionId.Link: await CaptureLinkAsync(); break;
            case CaptureActionId.Display: await CaptureScreenshotAsync(() => App.Services.Screenshots.CaptureDisplayAsync()); break;
            case CaptureActionId.AllDisplays: await CaptureScreenshotAsync(() => App.Services.Screenshots.CaptureAllDisplaysAsync()); break;
            case CaptureActionId.Window: await CaptureScreenshotAsync(() => App.Services.Screenshots.CaptureWindowAsync(source)); break;
            case CaptureActionId.Region: SelectRegion(async bounds => await CaptureScreenshotAsync(() => App.Services.Screenshots.CaptureRegionAsync(bounds))); break;
            case CaptureActionId.PreviousRegion:
                if (_lastRegion is null) ShowError("No previous region", "Capture a region once and CursorPocket will remember it.");
                else await CaptureScreenshotAsync(() => App.Services.Screenshots.CaptureRegionAsync(_lastRegion));
                break;
        }
    }

    private async Task CaptureScreenshotAsync(Func<Task<CaptureRecord>> capture)
    {
        try
        {
            var record = await capture();
            var path = App.Services.Library.GetAbsolutePath(record);
            // Copy immediately, so the shot is pasteable the moment it is taken rather
            // than only after the annotation surface is dismissed.
            var copied = await CopyImageToClipboardAsync(path);
            OpenEditor(record, path, AnnotationOrigin.FreshCapture, cancelled: () => ShowReceipt(
                record,
                copied ? "Screenshot saved · copied" : "Screenshot saved without annotation"));
        }
        catch (Exception error)
        {
            ShowError("Screenshot failed", error.Message);
        }
    }

    /// <summary>Asks for an image on disk and opens the editor on a copy of it.</summary>
    private async Task PickImageToAnnotateAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            foreach (var extension in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" })
            {
                picker.FileTypeFilter.Add(extension);
            }

            // An unpackaged window has to be handed to the picker explicitly.
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker,
                WinRT.Interop.WindowNative.GetWindowHandle(this));

            var file = await picker.PickSingleFileAsync();
            if (file is not null)
            {
                await AnnotateFileAsync(file.Path);
            }
        }
        catch (Exception error)
        {
            ShowError("That image could not be opened", error.Message);
        }
    }

    /// <summary>
    /// Opens the editor on a capture that already exists — from the Library, from a
    /// receipt, or from a pin. Saving writes an edited copy rather than replacing it,
    /// because a capture the user kept is an artifact they chose.
    /// </summary>
    public void AnnotateExisting(CaptureRecord record)
    {
        if (record.CaptureKind != CaptureKind.Screenshot)
        {
            ShowError("Only screenshots can be marked up", "This capture is not an image.");
            return;
        }

        var path = App.Services.Library.GetAbsolutePath(record);
        if (!File.Exists(path))
        {
            ShowError("That screenshot is missing", "The file is no longer where the index expects it.");
            return;
        }

        OpenEditor(record, path, AnnotationOrigin.ExistingCapture);
    }

    /// <summary>Opens the editor on whatever image is on the clipboard.</summary>
    public async Task AnnotateClipboardAsync()
    {
        try
        {
            var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (!content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Bitmap))
            {
                ShowError("Nothing to mark up", "The clipboard has no image on it.");
                return;
            }

            var reference = await content.GetBitmapAsync();
            using var stream = await reference.OpenReadAsync();
            var reservation = App.Services.CaptureStore.Reserve(CaptureKind.Screenshot, ".png");

            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            using var software = await decoder.GetSoftwareBitmapAsync();
            using (var file = File.Create(reservation.AbsolutePath))
            {
                using var output = file.AsRandomAccessStream();
                var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(
                    Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,
                    output);
                encoder.SetSoftwareBitmap(software);
                await encoder.FlushAsync();
            }

            var record = await App.Services.CaptureStore.RegisterExistingAsync(
                CaptureKind.Screenshot,
                reservation.AbsolutePath,
                $"Screenshot · {software.PixelWidth} × {software.PixelHeight}",
                new Dictionary<string, object?>
                {
                    ["width"] = software.PixelWidth,
                    ["height"] = software.PixelHeight,
                    ["source"] = "clipboard",
                });

            OpenEditor(record, reservation.AbsolutePath, AnnotationOrigin.FreshCapture);
        }
        catch (Exception error)
        {
            ShowError("The clipboard image could not be opened", error.Message);
        }
    }

    /// <summary>
    /// Opens the editor on an image already on disk. The file is copied into the capture
    /// folder first, so marking it up never writes over something outside CursorPocket's
    /// own tree.
    /// </summary>
    public async Task AnnotateFileAsync(string sourcePath)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                ShowError("That file is missing", sourcePath);
                return;
            }

            int width;
            int height;
            using (var probe = new System.Drawing.Bitmap(sourcePath))
            {
                width = probe.Width;
                height = probe.Height;
            }

            var record = await App.Services.CaptureStore.ImportFileAsync(
                CaptureKind.Screenshot,
                sourcePath,
                $"Screenshot · {width} × {height}",
                new Dictionary<string, object?> { ["width"] = width, ["height"] = height });

            OpenEditor(record, App.Services.Library.GetAbsolutePath(record), AnnotationOrigin.FreshCapture);
        }
        catch (Exception error)
        {
            ShowError("That image could not be opened", error.Message);
        }
    }

    /// <summary>
    /// The one place an editor is constructed and wired. Activation differs by origin: a
    /// fresh capture needs ForceForeground because a transient surface has just hidden
    /// itself and the source app still owns the foreground lock, whereas an editor opened
    /// from the Library comes from a window that already has focus.
    /// </summary>
    private void OpenEditor(
        CaptureRecord record,
        string path,
        AnnotationOrigin origin,
        Action? cancelled = null)
    {
        var editor = new AnnotationWindow(record, path, origin);
        if (cancelled is not null)
        {
            editor.Cancelled += (_, _) => cancelled();
        }

        editor.CopyRequested += async (_, temporary) =>
        {
            try { await CopyImageToClipboardAsync(temporary); }
            finally { try { File.Delete(temporary); } catch (IOException) { } }
        };
        editor.SavedAsNewCapture += async (_, temporary) => await RegisterEditedCopyAsync(temporary);
        editor.Discarded += async (_, _) => await DiscardCaptureAsync(record, path);
        editor.PinExportRequested += async (_, temporary) => await RegisterEditedCopyAndPinAsync(temporary);
        editor.Saved += async (_, _) =>
        {
            var copied = await CopyImageToClipboardAsync(path);
            ShowReceipt(record, SaveTarget.Describe(AnnotationSaveMode.Overwrite, copied));
        };

        if (origin == AnnotationOrigin.FreshCapture)
        {
            editor.AppWindow.Show(true);
            WindowPlacement.ForceForeground(editor);
        }
        else
        {
            editor.Activate();
        }
    }

    /// <summary>
    /// Leaves a saved capture on screen. Only ever from an explicit action — a pin the user
    /// did not ask for is the unexplained floating widget the anti-references warn against.
    /// </summary>
    private void PinCapture(CaptureRecord record, string path)
    {
        // Deferred one turn: the editor saves on the same gesture, and the pin has to read
        // the finished file rather than the one being written over.
        App.DispatcherQueue.TryEnqueue(() =>
        {
            var pin = PinnedCaptureWindow.TryShow(record, path, _pins.Count);
            if (pin is null)
            {
                return;
            }

            pin.EditRequested += (_, pinned) => AnnotateExisting(pinned);
            pin.Closed += (_, _) => _pins.Remove(pin);
            _pins.Add(pin);
        });
    }

    /// <summary>
    /// Takes a finished PNG the editor wrote and makes it a capture of its own, leaving the
    /// one it was edited from untouched. Used when a crop, a cut, or a backdrop changed the
    /// dimensions: those delete pixels, and a save overwrites rather than deleting, so
    /// there would be no Recycle Bin copy to fall back on.
    /// </summary>
    private async Task RegisterEditedCopyAsync(string temporaryPath)
    {
        try
        {
            var reservation = App.Services.CaptureStore.Reserve(CaptureKind.Screenshot, ".png");
            File.Move(temporaryPath, reservation.AbsolutePath, true);

            int width;
            int height;
            using (var bitmap = new System.Drawing.Bitmap(reservation.AbsolutePath))
            {
                width = bitmap.Width;
                height = bitmap.Height;
            }

            var record = await App.Services.CaptureStore.RegisterExistingAsync(
                CaptureKind.Screenshot,
                reservation.AbsolutePath,
                $"Screenshot · {width} × {height}",
                new Dictionary<string, object?> { ["width"] = width, ["height"] = height });

            var copied = await CopyImageToClipboardAsync(reservation.AbsolutePath);
            ShowReceipt(record, SaveTarget.Describe(AnnotationSaveMode.NewCapture, copied));
        }
        catch (Exception error)
        {
            ShowError("The edited copy was not saved", error.Message);
        }
    }

    private async Task RegisterEditedCopyAndPinAsync(string temporaryPath)
    {
        try
        {
            var reservation = App.Services.CaptureStore.Reserve(CaptureKind.Screenshot, ".png");
            File.Move(temporaryPath, reservation.AbsolutePath, true);
            using var bitmap = new System.Drawing.Bitmap(reservation.AbsolutePath);
            var record = await App.Services.CaptureStore.RegisterExistingAsync(
                CaptureKind.Screenshot,
                reservation.AbsolutePath,
                $"Screenshot · {bitmap.Width} × {bitmap.Height}",
                new Dictionary<string, object?> { ["width"] = bitmap.Width, ["height"] = bitmap.Height });
            PinCapture(record, reservation.AbsolutePath);
            ShowReceipt(record, "Screenshot saved and pinned");
        }
        catch (Exception error)
        {
            try { File.Delete(temporaryPath); } catch (IOException) { }
            ShowError("The pin was not created", error.Message);
        }
    }

    /// <summary>
    /// Throws a capture away: to the Recycle Bin, never a hard delete, and out of the
    /// index. The file was already written before the editor opened, so discarding is the
    /// only way to undo having taken the shot at all.
    /// </summary>
    private async Task DiscardCaptureAsync(CaptureRecord record, string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                    path,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            }

            await App.Services.CaptureStore.RemoveFromIndexAsync(record.Id);
            ShowError("Screenshot discarded", "It is in the Recycle Bin if you want it back.");
        }
        catch (Exception error)
        {
            ShowError("The screenshot was not discarded", error.Message);
        }
    }

    private void SelectRegion(Func<CaptureBounds, Task> callback)
    {
        var selector = new RegionSelectorWindow();
        selector.RegionSelected += async (_, bounds) =>
        {
            _lastRegion = bounds;
            await callback(bounds);
        };
        selector.Activate();
    }

    private async Task StartVideoAsync(RecordingOptions options)
    {
        if (options.SourceKind == VideoSourceKind.Region && options.Bounds is null)
        {
            SelectRegion(async bounds => await StartVideoAsync(options with { Bounds = bounds }));
            return;
        }
        try
        {
            var rememberedSettings = App.Services.Settings with
            {
                VideoSourceKind = options.SourceKind.ToString().ToLowerInvariant(),
                VideoMicrophoneEnabled = options.IncludeMicrophone,
                VideoMicrophoneName = options.MicrophoneName,
                AudioNoiseSuppression = options.NoiseSuppression,
                AudioAutoLevel = options.AutoLevel,
                VideoCameraEnabled = options.IncludeCamera,
                VideoCameraName = options.CameraName,
                VideoCameraPosition = options.CameraPosition,
                VideoCameraWidth = options.CameraWidth,
                VideoCameraShape = options.CameraShape,
                VideoCameraBackground = options.CameraBackgroundMode,
                VideoCameraBackgroundImage = options.CameraBackgroundImagePath,
                VideoCameraTouchUp = options.CameraTouchUpLevel,
                VideoCameraBrightness = options.CameraBrightness,
                VideoCameraWarmth = options.CameraWarmth,
                VideoCameraContrast = options.CameraContrast,
                VideoFramesPerSecond = options.FramesPerSecond,
                VideoCountdownSeconds = options.CountdownSeconds,
                VideoDrawCursor = options.DrawCursor,
            };
            App.Services.Context.RestoreFocus(_lastSourceWindow);
            RecordingHudWindow.ShowForVideo(
                options,
                async discard =>
                {
                    CaptureRecord? record = null;
                    if (discard) await App.Services.RecordingSession.DiscardAsync();
                    else record = await App.Services.RecordingSession.FinishAsync();
                    if (record is not null) ShowReceipt(record, "Video saved");
                    else ShowError(discard ? "Recording discarded" : "Video was not saved", "No file was created.");
                });
            // The self-view holds the camera for the whole recording and is captured
            // off the screen, so it must be up and placed inside the recorded area
            // before FFmpeg starts writing frames. The preflight has already released
            // its own preview by this point.
            await ShowCameraSelfViewAsync(options);
            await App.Services.RecordingSession.StartVideoAsync(options);
            _ = App.Services.UpdateRecordingDefaultsAsync(rememberedSettings);
        }
        catch (Exception error)
        {
            DismissCameraSelfView();
            ShowError("Video did not start", error.Message);
        }
    }

    private async Task ShowCameraSelfViewAsync(RecordingOptions options)
    {
        DismissCameraSelfView();
        _selfView = await CameraSelfViewWindow.ShowForAsync(options, _lastSourceWindow);
    }

    private void DismissCameraSelfView()
    {
        _selfView?.Dismiss();
        _selfView = null;
    }

    /// <summary>
    /// Puts a saved screenshot on the clipboard. Flushed so it outlives CursorPocket:
    /// without that, quitting the app takes the image back off the clipboard. A failure
    /// here is reported in the receipt wording but never fails the capture — the file
    /// is already on disk, which is the part that matters.
    /// </summary>
    private static async Task<bool> CopyImageToClipboardAsync(string path)
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetBitmap(Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(file));
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
            return true;
        }
        catch (Exception)
        {
            // Another app can hold the clipboard open, and Flush throws if the
            // clipboard is locked. The screenshot is saved either way.
            return false;
        }
    }

    private void ShowReceipt(CaptureRecord record, string title)
        => _receipts.Show(new ReceiptRequest(record, title));

    private void ShowError(string title, string detail)
        => _receipts.Show(new ReceiptRequest(null, title, detail));

    private void Recording_StateChanged(object? sender, RecordingState state) => App.DispatcherQueue.TryEnqueue(() =>
    {
        _companion?.SetRecording(state is RecordingState.Starting or RecordingState.Recording or RecordingState.Finalizing);
        if (_tray is not null)
        {
            _tray.Text = state == RecordingState.Recording ? "CursorPocket · recording" : "CursorPocket · ready";
        }
        // Release the camera as soon as the recording is no longer running, so the
        // device is free for the next preflight preview.
        if (state is RecordingState.Finalizing or RecordingState.Idle or RecordingState.Failed)
        {
            DismissCameraSelfView();
        }
    });

    private RecordingOptions BuildRememberedVideoOptions()
    {
        var settings = App.Services.Settings;
        var sourceKind = settings.VideoSourceKind switch
        {
            "region" => VideoSourceKind.Region,
            "window" => VideoSourceKind.Window,
            _ => VideoSourceKind.Display,
        };
        // Repeating a setup still records the screen the user is on now, resolved
        // when command mode opened.
        var display = _displayTarget ?? WindowPlacement.DisplayTargetUnderPointer();
        return new RecordingOptions
        {
            SourceKind = sourceKind,
            Bounds = sourceKind switch
            {
                VideoSourceKind.Region => _lastRegion,
                VideoSourceKind.Display => display.Bounds,
                _ => null,
            },
            DisplayOutputIndex = sourceKind == VideoSourceKind.Display ? display.OutputIndex : null,
            WindowHandle = sourceKind == VideoSourceKind.Window ? _lastSourceWindow : null,
            IncludeMicrophone = settings.VideoMicrophoneEnabled,
            MicrophoneName = settings.VideoMicrophoneName,
            NoiseSuppression = settings.AudioNoiseSuppression,
            AutoLevel = settings.AudioAutoLevel,
            IncludeCamera = settings.VideoCameraEnabled,
            CameraName = settings.VideoCameraName,
            CameraPosition = settings.VideoCameraPosition,
            CameraWidth = settings.VideoCameraWidth,
            CameraShape = settings.VideoCameraShape,
            CameraBackgroundMode = settings.VideoCameraBackground,
            CameraBackgroundImagePath = settings.VideoCameraBackgroundImage,
            CameraTouchUpLevel = settings.VideoCameraTouchUp,
            CameraBrightness = settings.VideoCameraBrightness,
            CameraWarmth = settings.VideoCameraWarmth,
            CameraContrast = settings.VideoCameraContrast,
            FramesPerSecond = settings.VideoFramesPerSecond,
            CountdownSeconds = settings.VideoCountdownSeconds,
            DrawCursor = settings.VideoDrawCursor,
        };
    }

    private void Services_SettingsChanged(object? sender, AppSettings settings) =>
        App.DispatcherQueue.TryEnqueue(() =>
        {
            App.Theme.SetMode(settings.ThemeMode);
            SubscribeToRecordingState();
            _companion?.SetMode(settings.CursorCompanionMode);
            if (_mouseActivity is not null)
            {
                _mouseActivity.GestureEnabled = settings.MouseGestureEnabled;
            }
            if (_companionTrayItem is not null)
            {
                _companionTrayItem.Text = settings.CursorCompanionMode == "off" ? "Show cursor companion" : "Hide cursor companion";
            }
        });

    private void InitializeCompanion()
    {
        _companion = new NativeCompanionWindow(App.Services.Settings.CursorCompanionMode);
        _companion.OpenRequested += (_, _) => ShowCommandPalette();
        _mouseActivity = new MouseActivityService
        {
            GestureEnabled = App.Services.Settings.MouseGestureEnabled,
        };
        // Raised from a timer thread, so it has to marshal like the others.
        _mouseActivity.ChordHold += (_, _) =>
        {
            if (App.Services.Settings.MouseChordEnabled)
            {
                App.DispatcherQueue.TryEnqueue(() => ShowCommandPalette());
            }
        };
        // Moved is a coalesced signal: at most one dispatcher item is in flight and
        // it reads the newest pointer position, rather than queueing one closure per
        // mouse event.
        _mouseActivity.Moved += (_, _) => App.DispatcherQueue.TryEnqueue(FollowPointer);
        _mouseActivity.DoubleCircle += (_, _) => App.DispatcherQueue.TryEnqueue(() => ShowCommandPalette());
        // The hook goes live only now that every handler is attached.
        _mouseActivity.Start();
    }

    private void FollowPointer()
    {
        if (_mouseActivity?.TryConsumeLatestPosition(out var x, out var y) == true)
        {
            _companion?.Follow(x, y);
        }
    }

    private void InitializeTray()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Text = "CursorPocket · ready",
            Visible = true,
            Icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application,
        };
        var menu = _trayMenu = new System.Windows.Forms.ContextMenuStrip
        {
            ShowImageMargin = false,
            Padding = new System.Windows.Forms.Padding(6),
            Font = new Font("Segoe UI Variable Text", 10f),
        };
        menu.Items.Add("Open command mode", null, (_, _) => App.DispatcherQueue.TryEnqueue(() => ShowCommandPalette()));
        menu.Items.Add("Screenshot…", null, (_, _) => App.DispatcherQueue.TryEnqueue(() => ShowCommandPalette("screenshot")));
        menu.Items.Add("Video…", null, (_, _) => App.DispatcherQueue.TryEnqueue(ShowVideoPreflight));
        menu.Items.Add("Audio note", null, (_, _) => App.DispatcherQueue.TryEnqueue(async () => await ToggleAudioRecordingAsync()));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        // Two ways into the editor for an image CursorPocket did not take. On the tray
        // rather than in command mode, so neither costs a bare key that would then have to
        // be registered and unregistered around every capture.
        menu.Items.Add("Mark up clipboard image", null, (_, _) => App.DispatcherQueue.TryEnqueue(async () => await AnnotateClipboardAsync()));
        menu.Items.Add("Mark up an image…", null, (_, _) => App.DispatcherQueue.TryEnqueue(async () => await PickImageToAnnotateAsync()));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Library", null, (_, _) => App.DispatcherQueue.TryEnqueue(ShowLibrary));
        menu.Items.Add("Settings", null, (_, _) => App.DispatcherQueue.TryEnqueue(ShowSettings));
        menu.Items.Add("Open capture folder", null, (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(App.Services.Settings.CaptureDirectory) { UseShellExecute = true }));
        _companionTrayItem = new System.Windows.Forms.ToolStripMenuItem(App.Services.Settings.CursorCompanionMode == "off" ? "Show cursor companion" : "Hide cursor companion");
        _companionTrayItem.Click += (_, _) => App.DispatcherQueue.TryEnqueue(async () =>
        {
            var hidden = App.Services.Settings.CursorCompanionMode == "off";
            await App.Services.UpdateSettingsAsync(App.Services.Settings with { CursorCompanionMode = hidden ? "while-moving" : "off" });
        });
        menu.Items.Add(_companionTrayItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => App.DispatcherQueue.TryEnqueue(Quit));
        menu.Opening += (_, _) => ApplyTrayTheme();
        ApplyTrayTheme();
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => App.DispatcherQueue.TryEnqueue(ShowLibrary);
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs eventArgs)
    {
        if (_quitting)
        {
            return;
        }
        eventArgs.Cancel = true;
        _ = PersistGeometryAsync();
        AppWindow.Hide();
    }

    private async void Quit()
    {
        if (App.Services.RecordingSession.IsActive)
        {
            var dialog = new Microsoft.UI.Xaml.Controls.ContentDialog
            {
                Title = "A recording is still running",
                Content = "Finish the recording before CursorPocket quits, discard it, or keep the app open.",
                PrimaryButtonText = "Finish and quit",
                SecondaryButtonText = "Discard and quit",
                CloseButtonText = "Cancel",
                DefaultButton = Microsoft.UI.Xaml.Controls.ContentDialogButton.Primary,
                XamlRoot = RootFrame.XamlRoot,
            };
            var result = await dialog.ShowAsync();
            if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.None) return;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                if (result == Microsoft.UI.Xaml.Controls.ContentDialogResult.Primary)
                {
                    await App.Services.RecordingSession.FinishAsync(timeout.Token);
                }
                else
                {
                    await App.Services.RecordingSession.DiscardAsync(timeout.Token);
                }
            }
            catch (Exception error)
            {
                ShowError("CursorPocket is still recording", error.Message);
                return;
            }
        }
        _quitting = true;
        await PersistGeometryAsync();
        if (_subscribedRecording is not null)
        {
            _subscribedRecording.StateChanged -= Recording_StateChanged;
        }
        _mouseActivity?.Dispose();
        App.Theme.ThemeChanged -= Theme_ThemeChanged;
        _receipts.Dispose();
        _companion?.Close();
        _tray?.Dispose();
        Close();
        ((App)Microsoft.UI.Xaml.Application.Current).Shutdown();
    }

    public Task RepeatVideoRecordingAsync() => StartVideoAsync(BuildRememberedVideoOptions());

    private void Theme_ThemeChanged(object? sender, EventArgs eventArgs) => ApplyTrayTheme();

    private void ApplyTrayTheme()
    {
        if (_trayMenu is null) return;
        var palette = App.Theme.Palette;
        _trayMenu.Renderer = App.Theme.CreateMenuRenderer();
        _trayMenu.BackColor = palette.Background;
        _trayMenu.ForeColor = palette.Text;
        foreach (System.Windows.Forms.ToolStripItem item in _trayMenu.Items)
        {
            item.ForeColor = item.Enabled ? palette.Text : palette.Muted;
            item.BackColor = palette.Background;
        }
        _trayMenu.Invalidate();
    }

    private void SubscribeToRecordingState()
    {
        if (ReferenceEquals(_subscribedRecording, App.Services.Recording))
        {
            return;
        }
        if (_subscribedRecording is not null)
        {
            _subscribedRecording.StateChanged -= Recording_StateChanged;
        }
        _subscribedRecording = App.Services.Recording;
        _subscribedRecording.StateChanged += Recording_StateChanged;
    }

    private void RestoreGeometry()
    {
        var parts = App.Services.Settings.LibraryWindowGeometry.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4 || !int.TryParse(parts[0], out var x) || !int.TryParse(parts[1], out var y) ||
            !int.TryParse(parts[2], out var width) || !int.TryParse(parts[3], out var height) || width < 720 || height < 540)
        {
            return;
        }
        var requested = new RectInt32(x, y, width, height);
        var nearest = DisplayArea.GetFromRect(requested, DisplayAreaFallback.Nearest);
        if (nearest is null)
        {
            return;
        }
        var work = nearest.WorkArea;
        var safeWidth = Math.Min(width, work.Width);
        var safeHeight = Math.Min(height, work.Height);
        var safeX = Math.Clamp(x, work.X - safeWidth + 96, work.X + work.Width - 96);
        var safeY = Math.Clamp(y, work.Y, work.Y + work.Height - 64);
        AppWindow.MoveAndResize(new RectInt32(safeX, safeY, safeWidth, safeHeight));
    }

    private Task PersistGeometryAsync()
    {
        var position = AppWindow.Position;
        var size = AppWindow.Size;
        return App.Services.UpdateLibraryWindowGeometryAsync($"{position.X},{position.Y},{size.Width},{size.Height}");
    }
}
