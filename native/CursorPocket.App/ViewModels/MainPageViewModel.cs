using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CursorPocket.Core.Models;
using CursorPocket_App.Services;
using System.ComponentModel;
using Microsoft.UI.Xaml;

namespace CursorPocket_App.ViewModels;

public partial class MainPageViewModel(AppServices services) : ObservableObject
{
    private static readonly HashSet<string> SettingsProperties =
    [
        nameof(CaptureDirectory), nameof(ThemeModeIndex), nameof(StartWithWindows), nameof(MouseGestureEnabled),
        nameof(MouseChordEnabled), nameof(CursorCompanionMode), nameof(ActivationShortcut),
        nameof(VideoMicrophoneEnabled), nameof(VideoCameraEnabled), nameof(VideoFramesPerSecond),
        nameof(VideoCountdownSeconds), nameof(AudioNoiseSuppression), nameof(AudioAutoLevel),
        nameof(AutomaticallyCheckForUpdates),
    ];
    private CancellationTokenSource? _settingsSaveDebounce;
    private bool _applyingSettings;
    private readonly List<CaptureItemViewModel> _allItems = [];

    public BulkObservableCollection<CaptureItemViewModel> Items { get; } = [];
    public IReadOnlyList<string> Filters { get; } = ["All", "Screenshots", "Video", "Audio", "Text", "Links"];

    [ObservableProperty] private CaptureItemViewModel? _selectedItem;
    [ObservableProperty] private string _selectedFilter = "All";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _libraryErrorMessage;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private Visibility _settingsRetryVisibility = Visibility.Collapsed;
    [ObservableProperty] private string _captureDirectory = services.Settings.CaptureDirectory;
    [ObservableProperty] private int _themeModeIndex = services.Settings.ThemeMode switch
    {
        AppThemeMode.Light => 1,
        AppThemeMode.Dark => 2,
        _ => 0,
    };
    [ObservableProperty] private bool _startWithWindows = services.Settings.StartWithWindows;
    [ObservableProperty] private bool _mouseGestureEnabled = services.Settings.MouseGestureEnabled;
    [ObservableProperty] private bool _mouseChordEnabled = services.Settings.MouseChordEnabled;
    [ObservableProperty] private string _cursorCompanionMode = services.Settings.CursorCompanionMode;
    [ObservableProperty] private string _activationShortcut = services.Hotkey.RegisteredShortcut ?? "Shortcut unavailable";
    [ObservableProperty] private bool _videoMicrophoneEnabled = services.Settings.VideoMicrophoneEnabled;
    [ObservableProperty] private bool _videoCameraEnabled = services.Settings.VideoCameraEnabled;
    [ObservableProperty] private int _videoFramesPerSecond = services.Settings.VideoFramesPerSecond;
    [ObservableProperty] private int _videoCountdownSeconds = services.Settings.VideoCountdownSeconds;
    [ObservableProperty] private bool _audioNoiseSuppression = services.Settings.AudioNoiseSuppression;
    [ObservableProperty] private bool _audioAutoLevel = services.Settings.AudioAutoLevel;
    [ObservableProperty] private bool _automaticallyCheckForUpdates = services.Settings.AutomaticallyCheckForUpdates;

    public string CaptureCountLabel => _allItems.Count switch
    {
        0 => "Your next capture will land here",
        1 => "1 capture",
        _ => $"{_allItems.Count} captures",
    };

    /// <summary>Count and disk use on one mono line under the library heading.</summary>
    public string LibrarySummary
    {
        get
        {
            if (_allItems.Count == 0)
            {
                return "Your next capture will land here";
            }
            var bytes = 0L;
            foreach (var item in _allItems)
            {
                try
                {
                    var file = new FileInfo(item.AbsolutePath);
                    if (file.Exists)
                    {
                        bytes += file.Length;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    // A capture that moved or vanished simply does not count toward the total.
                }
            }
            return $"{CaptureCountLabel} · {CaptureItemViewModel.FormatBytes(bytes)}";
        }
    }

    public int AllCount => _allItems.Count;
    public int ScreenshotCount => CountOf(CaptureKind.Screenshot);
    public int VideoCount => CountOf(CaptureKind.Video);
    public int AudioCount => CountOf(CaptureKind.Audio);
    public int TextCount => CountOf(CaptureKind.Text);
    public int LinkCount => CountOf(CaptureKind.Link);

    private int CountOf(CaptureKind kind) => _allItems.Count(item => item.Record.CaptureKind == kind);

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(CaptureCountLabel));
        OnPropertyChanged(nameof(LibrarySummary));
        OnPropertyChanged(nameof(AllCount));
        OnPropertyChanged(nameof(ScreenshotCount));
        OnPropertyChanged(nameof(VideoCount));
        OnPropertyChanged(nameof(AudioCount));
        OnPropertyChanged(nameof(TextCount));
        OnPropertyChanged(nameof(LinkCount));
    }

    public string ActivationHint => ActivationShortcut == "Shortcut unavailable"
        ? "Choose a working activation shortcut in Settings, then make your first capture."
        : $"Press {ActivationShortcut}, then choose a capture. Audio, video, screenshots, text, and links all appear here.";

    public async Task InitializeAsync()
    {
        IsBusy = true;
        LibraryErrorMessage = null;
        try
        {
            var records = await services.Library.GetRecentAsync();
            _allItems.Clear();
            _allItems.AddRange(records.Select(record => new CaptureItemViewModel(record, services.Library.GetAbsolutePath(record))));
            ApplyFilter();
            RaiseCounts();
            StatusMessage = services.Hotkey.RegisteredShortcut is null
                ? "Choose an available activation shortcut in Settings"
                : $"Press {services.Hotkey.RegisteredShortcut} anywhere to capture";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            LibraryErrorMessage = "CursorPocket could not read the capture index. Your capture files have not been changed.";
            StatusMessage = "Library unavailable";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void SelectFilter(string value)
    {
        SelectedFilter = value;
        ApplyFilter();
    }

    public Task CaptureAddedAsync(CaptureRecord record)
    {
        var item = new CaptureItemViewModel(record, services.Library.GetAbsolutePath(record));
        _allItems.Insert(0, item);
        // Inserting the one new row leaves every existing container in place. A full
        // rebuild here made the list flicker on each completed capture.
        if (Matches(item))
        {
            Items.Insert(0, item);
        }
        SelectedItem = item;
        RaiseCounts();
        StatusMessage = $"Saved {item.KindLabel.ToLowerInvariant()}";
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync() =>
        await DeleteAsync(SelectedItem is null ? [] : [SelectedItem]);

    /// <summary>
    /// Moves every given capture to the Recycle Bin. One capture that refuses to
    /// delete does not abandon the rest, and the count is reported so a multiple
    /// selection does not silently drop items.
    /// </summary>
    public async Task DeleteAsync(IReadOnlyList<CaptureItemViewModel> items)
    {
        if (items.Count == 0)
        {
            return;
        }
        var deleted = 0;
        var failed = 0;
        foreach (var item in items)
        {
            try
            {
                await services.Library.DeleteAsync(item.Record);
                _allItems.Remove(item);
                // Dropping the deleted rows keeps the surviving containers alive; a
                // full rebuild here regenerated every visible row.
                Items.Remove(item);
                deleted++;
            }
            catch (Exception)
            {
                failed++;
            }
        }
        SelectedItem = Items.FirstOrDefault();
        StatusMessage = failed == 0
            ? deleted == 1
                ? "Moved capture to the Recycle Bin"
                : $"Moved {deleted} captures to the Recycle Bin"
            : $"Moved {deleted} to the Recycle Bin · {failed} could not be removed";
        RaiseCounts();
    }

    [RelayCommand]
    private void OpenSelected()
    {
        if (SelectedItem is not null && File.Exists(SelectedItem.AbsolutePath))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(SelectedItem.AbsolutePath) { UseShellExecute = true });
        }
    }

    [RelayCommand]
    private void RevealSelected()
    {
        if (SelectedItem is not null)
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{SelectedItem.AbsolutePath}\"");
        }
    }

    [RelayCommand]
    private void OpenCaptureFolder()
    {
        Directory.CreateDirectory(CaptureDirectory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(CaptureDirectory) { UseShellExecute = true });
    }

    [RelayCommand]
    private Task SaveSettingsAsync() => SaveSettingsCoreAsync();

    protected override void OnPropertyChanged(PropertyChangedEventArgs eventArgs)
    {
        base.OnPropertyChanged(eventArgs);
        if (!_applyingSettings && eventArgs.PropertyName is { } name && SettingsProperties.Contains(name))
        {
            if (name == nameof(ThemeModeIndex))
            {
                App.Theme.SetMode(ThemeModeIndex switch
                {
                    1 => AppThemeMode.Light,
                    2 => AppThemeMode.Dark,
                    _ => AppThemeMode.System,
                });
            }
            QueueSettingsSave();
        }
    }

    private void QueueSettingsSave()
    {
        _settingsSaveDebounce?.Cancel();
        _settingsSaveDebounce?.Dispose();
        var cancellation = _settingsSaveDebounce = new CancellationTokenSource();
        StatusMessage = "Saving…";
        SettingsRetryVisibility = Visibility.Collapsed;
        _ = SaveAfterDelayAsync(cancellation);
    }

    private async Task SaveAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(350, cancellation.Token);
            await SaveSettingsCoreAsync(cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // A newer change owns the save status.
        }
    }

    private async Task SaveSettingsCoreAsync(CancellationToken cancellationToken = default)
    {
        var requestedShortcut = ActivationShortcut;
        var folderChanged = !string.Equals(services.Settings.CaptureDirectory, CaptureDirectory, StringComparison.OrdinalIgnoreCase);
        var updated = services.Settings with
        {
            ThemeMode = ThemeModeIndex switch
            {
                1 => AppThemeMode.Light,
                2 => AppThemeMode.Dark,
                _ => AppThemeMode.System,
            },
            CaptureDirectory = CaptureDirectory,
            StartWithWindows = StartWithWindows,
            MouseGestureEnabled = MouseGestureEnabled,
            MouseChordEnabled = MouseChordEnabled,
            CursorCompanionMode = CursorCompanionMode,
            ActivationShortcut = ActivationShortcut,
            VideoMicrophoneEnabled = VideoMicrophoneEnabled,
            VideoCameraEnabled = VideoCameraEnabled,
            VideoFramesPerSecond = VideoFramesPerSecond,
            VideoCountdownSeconds = VideoCountdownSeconds,
            AudioNoiseSuppression = AudioNoiseSuppression,
            AudioAutoLevel = AudioAutoLevel,
            AutomaticallyCheckForUpdates = AutomaticallyCheckForUpdates,
        };
        try
        {
            await services.UpdateSettingsAsync(updated, cancellationToken);
            _applyingSettings = true;
            ActivationShortcut = services.Hotkey.RegisteredShortcut ?? "Shortcut unavailable";
            _applyingSettings = false;
            OnPropertyChanged(nameof(ActivationHint));
            if (folderChanged) await InitializeAsync();
            StatusMessage = string.Equals(requestedShortcut, ActivationShortcut, StringComparison.OrdinalIgnoreCase)
                ? "Saved"
                : $"{requestedShortcut} was unavailable · using {ActivationShortcut}";
            SettingsRetryVisibility = Visibility.Collapsed;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _applyingSettings = false;
            StatusMessage = error.Message;
            SettingsRetryVisibility = Visibility.Visible;
        }
    }

    private void ApplyFilter()
    {
        Items.ReplaceAll(_allItems.Where(Matches));
        SelectedItem = Items.FirstOrDefault();
    }

    private bool Matches(CaptureItemViewModel item) => SelectedFilter switch
    {
        "Screenshots" => item.Record.CaptureKind == CaptureKind.Screenshot,
        "Video" => item.Record.CaptureKind == CaptureKind.Video,
        "Audio" => item.Record.CaptureKind == CaptureKind.Audio,
        "Text" => item.Record.CaptureKind == CaptureKind.Text,
        "Links" => item.Record.CaptureKind == CaptureKind.Link,
        _ => true,
    };
}
