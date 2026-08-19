using System.Drawing;
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
    private System.Windows.Forms.ToolStripMenuItem? _companionTrayItem;
    private long _lastSourceWindow;
    private CaptureBounds? _lastRegion;
    private bool _openLibraryAfterPaletteCloses;
    private bool _quitting;

    public MainWindow()
    {
        InitializeComponent();
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
        SubscribeToRecordingState();
        App.Services.SettingsChanged += Services_SettingsChanged;
    }

    public void ShowLibrary()
    {
        AppWindow.Show(true);
        (RootFrame.Content as MainPage)?.NavigateTo("library");
        ActivateMainWindow();
    }

    public void ShowSettings()
    {
        AppWindow.Show(true);
        (RootFrame.Content as MainPage)?.NavigateTo("settings");
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
        _palette!.Show(_lastSourceWindow, initialMode);
    }

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
        _preflight = new VideoPreflightWindow(_lastSourceWindow);
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
            if (App.Services.Recording.State == RecordingState.Recording && !App.Services.Recording.IsVideo)
            {
                var record = await App.Services.Recording.StopAudioAsync();
                if (record is not null)
                {
                    ShowReceipt(record, "Audio note saved");
                }
                return;
            }
            var microphones = App.Services.Recording.GetMicrophones();
            var microphone = CursorPocket.Core.Services.MediaDeviceSelector.SelectRemembered(microphones, App.Services.Settings.VideoMicrophoneName);
            App.Services.Context.RestoreFocus(_lastSourceWindow);
            await App.Services.Recording.StartAudioAsync(microphone?.Id);
            RecordingHudWindow.ShowForAudio(
                microphone?.Name ?? "Default microphone",
                async discard =>
                {
                    var record = await App.Services.Recording.StopAudioAsync(discard);
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

    private async void Palette_CommandRequested(object? sender, string command)
    {
        var source = sender is CommandPaletteWindow palette ? palette.SourceWindow : _lastSourceWindow;
        _lastSourceWindow = source;
        if (command == "library")
        {
            // Showing a persistent window from the palette's close callback
            // avoids Windows reactivating the source after we show Library.
            _openLibraryAfterPaletteCloses = true;
            return;
        }
        switch (command)
        {
            case "video": ShowVideoPreflight(); break;
            case "repeat-video": await StartVideoAsync(BuildRememberedVideoOptions()); break;
            case "audio": await ToggleAudioRecordingAsync(); break;
            case "text": await CaptureTextAsync(); break;
            case "link": await CaptureLinkAsync(); break;
            case "display": await CaptureScreenshotAsync(() => App.Services.Screenshots.CaptureDisplayAsync()); break;
            case "all-displays": await CaptureScreenshotAsync(() => App.Services.Screenshots.CaptureAllDisplaysAsync()); break;
            case "window": await CaptureScreenshotAsync(() => App.Services.Screenshots.CaptureWindowAsync(source)); break;
            case "region": SelectRegion(async bounds => await CaptureScreenshotAsync(() => App.Services.Screenshots.CaptureRegionAsync(bounds))); break;
            case "previous-region":
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
            var editor = new AnnotationWindow(record, App.Services.Library.GetAbsolutePath(record));
            editor.Saved += (_, _) => ShowReceipt(record, "Screenshot saved");
            editor.Cancelled += (_, _) => ShowReceipt(record, "Screenshot saved without annotation");
            // Command mode has just hidden itself, so the source app already owns the
            // foreground. Activate() alone loses that race and leaves the annotation
            // window behind or minimized.
            editor.AppWindow.Show(true);
            WindowPlacement.ForceForeground(editor);
        }
        catch (Exception error)
        {
            ShowError("Screenshot failed", error.Message);
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
                VideoCameraEnabled = options.IncludeCamera,
                VideoCameraName = options.CameraName,
                VideoCameraPosition = options.CameraPosition,
                VideoCameraWidth = options.CameraWidth,
                VideoFramesPerSecond = options.FramesPerSecond,
                VideoCountdownSeconds = options.CountdownSeconds,
                VideoDrawCursor = options.DrawCursor,
            };
            App.Services.Context.RestoreFocus(_lastSourceWindow);
            RecordingHudWindow.ShowForVideo(
                options,
                async discard =>
                {
                    var record = await App.Services.Recording.StopVideoAsync(discard);
                    if (record is not null) ShowReceipt(record, "Video saved");
                    else ShowError(discard ? "Recording discarded" : "Video was not saved", "No file was created.");
                });
            // The self-view holds the camera for the whole recording and is captured
            // off the screen, so it must be up and placed inside the recorded area
            // before FFmpeg starts writing frames. The preflight has already released
            // its own preview by this point.
            await ShowCameraSelfViewAsync(options);
            await App.Services.Recording.StartVideoAsync(options);
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

    private void ShowReceipt(CaptureRecord record, string title)
    {
        var receipt = new ReceiptWindow(record, title);
        receipt.OpenLibraryRequested += (_, _) => ShowLibrary();
        receipt.AppWindow.Show(false);
    }

    private void ShowError(string title, string detail)
    {
        var receipt = new ReceiptWindow(null, title, detail);
        receipt.OpenLibraryRequested += (_, _) => ShowLibrary();
        receipt.AppWindow.Show(false);
    }

    private void Recording_StateChanged(object? sender, RecordingState state) => App.DispatcherQueue.TryEnqueue(() =>
    {
        _companion?.SetRecording(state is RecordingState.Starting or RecordingState.Recording or RecordingState.Finalizing);
        if (_tray is not null)
        {
            _tray.Text = state == RecordingState.Recording ? "CursorPocket · recording" : "CursorPocket · ready";
        }
        // Release the camera as soon as the recording is no longer running, so the
        // device is free for the next preflight preview.
        if (state is RecordingState.Idle or RecordingState.Failed)
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
        return new RecordingOptions
        {
            SourceKind = sourceKind,
            Bounds = sourceKind == VideoSourceKind.Region ? _lastRegion : null,
            WindowHandle = sourceKind == VideoSourceKind.Window ? _lastSourceWindow : null,
            IncludeMicrophone = settings.VideoMicrophoneEnabled,
            MicrophoneName = settings.VideoMicrophoneName,
            IncludeCamera = settings.VideoCameraEnabled,
            CameraName = settings.VideoCameraName,
            CameraPosition = settings.VideoCameraPosition,
            CameraWidth = settings.VideoCameraWidth,
            FramesPerSecond = settings.VideoFramesPerSecond,
            CountdownSeconds = settings.VideoCountdownSeconds,
            DrawCursor = settings.VideoDrawCursor,
        };
    }

    private void Services_SettingsChanged(object? sender, AppSettings settings) =>
        App.DispatcherQueue.TryEnqueue(() =>
        {
            SubscribeToRecordingState();
            _companion?.SetMode(settings.CursorCompanionMode);
            if (_companionTrayItem is not null)
            {
                _companionTrayItem.Text = settings.CursorCompanionMode == "off" ? "Show cursor companion" : "Hide cursor companion";
            }
        });

    private void InitializeCompanion()
    {
        _companion = new NativeCompanionWindow(App.Services.Settings.CursorCompanionMode);
        _companion.OpenRequested += (_, _) => ShowCommandPalette();
        _mouseActivity = new MouseActivityService();
        _mouseActivity.Moved += (_, point) => App.DispatcherQueue.TryEnqueue(() => _companion?.Follow(point.X, point.Y));
        _mouseActivity.DoubleCircle += (_, _) =>
        {
            if (App.Services.Settings.MouseGestureEnabled)
            {
                App.DispatcherQueue.TryEnqueue(() => ShowCommandPalette());
            }
        };
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
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open command mode", null, (_, _) => App.DispatcherQueue.TryEnqueue(() => ShowCommandPalette()));
        menu.Items.Add("Screenshot…", null, (_, _) => App.DispatcherQueue.TryEnqueue(() => ShowCommandPalette("screenshot")));
        menu.Items.Add("Video…", null, (_, _) => App.DispatcherQueue.TryEnqueue(ShowVideoPreflight));
        menu.Items.Add("Audio note", null, (_, _) => App.DispatcherQueue.TryEnqueue(async () => await ToggleAudioRecordingAsync()));
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
        _quitting = true;
        await PersistGeometryAsync();
        if (_subscribedRecording is not null)
        {
            _subscribedRecording.StateChanged -= Recording_StateChanged;
        }
        _mouseActivity?.Dispose();
        _companion?.Close();
        _tray?.Dispose();
        Close();
        ((App)Microsoft.UI.Xaml.Application.Current).Shutdown();
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
