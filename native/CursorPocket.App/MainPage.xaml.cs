using CursorPocket.Core.Models;
using CursorPocket_App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Core;
using Windows.Storage.Pickers;

namespace CursorPocket_App;

public sealed partial class MainPage : Page
{
    public MainPageViewModel ViewModel { get; } = new(App.Services);
    private CancellationTokenSource? _detailLoad;
    private bool _loaded;
    private bool _libraryLoaded;
    private Services.RecordingService? _recordingStatusSource;
    private string? _editingTextId;
    private string? _textBeforeEdit;
    private bool _savingText;
    private bool _previewMaximized;
    private bool _startingTextEdit;
    private bool _textDetailLoadedFromFile;
    private bool _themeSubscribed;

    public MainPage()
    {
        InitializeComponent();
        ApplyCaptureActionCatalog();
    }

    private void ApplyCaptureActionCatalog()
    {
        foreach (var (button, id) in new[]
        {
            (ScreenshotTile, CaptureActionId.Screenshot),
            (VideoTile, CaptureActionId.Video),
            (RepeatVideoTile, CaptureActionId.RepeatVideo),
            (AudioTile, CaptureActionId.Audio),
            (TextCaptureTile, CaptureActionId.Text),
            (LinkCaptureTile, CaptureActionId.Link),
            (LibraryTile, CaptureActionId.Library),
        })
        {
            var action = CaptureActionCatalog.Get(id);
            AutomationProperties.SetName(button, $"{action.Title}. {action.Description}. Key {action.Key} in command mode.");
            ToolTipService.SetToolTip(button, $"{action.Title} · {action.Key}");
        }
    }

    public void NavigateTo(string destination)
    {
        if ((_editingTextId is not null || _startingTextEdit) && destination != "library")
        {
            SetLibraryStatus("Save or cancel the text edit before leaving the Library");
            Navigation.SelectedItem = LibraryNav;
            return;
        }
        Navigation.SelectedItem = destination switch
        {
            "capture" => CaptureNav,
            "settings" => SettingsNav,
            _ => LibraryNav,
        };
    }

    private async void Page_Loaded(object sender, RoutedEventArgs eventArgs)
    {
        if (!_themeSubscribed)
        {
            App.Theme.ThemeChanged += Theme_ThemeChanged;
            _themeSubscribed = true;
        }
        ApplyLibraryPaneGlass();
        if (_loaded)
        {
            return;
        }
        _loaded = true;
        App.Services.CaptureCompleted += CaptureStore_CaptureCompleted;
        App.Services.SettingsChanged += Services_SettingsChanged;
        SubscribeRecordingStatus();
        CompanionModeBox.SelectedIndex = ViewModel.CursorCompanionMode switch { "off" => 0, "always" => 2, _ => 1 };
        FpsBox.SelectedIndex = ViewModel.VideoFramesPerSecond == 60 ? 1 : 0;
        CountdownBox.SelectedIndex = ViewModel.VideoCountdownSeconds switch { 0 => 0, 5 => 2, _ => 1 };
        FpsBox.SelectionChanged += VideoDefaults_SelectionChanged;
        CountdownBox.SelectionChanged += VideoDefaults_SelectionChanged;
        ApplyFilterSelection(ViewModel.SelectedFilter);
        ApplyThemeModeSelection();
        ShowActivationShortcut();
        RefreshUpdateStatus();
        // A tray-only launch has nothing on screen, so reading the manifest and
        // materializing every row can wait until the window is actually revealed.
        if (!App.StartedInBackground)
        {
            await EnsureLibraryLoadedAsync();
        }
    }

    private void Services_SettingsChanged(object? sender, AppSettings settings) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            SubscribeRecordingStatus();
            RefreshUpdateStatus();
        });

    private void SubscribeRecordingStatus()
    {
        if (!ReferenceEquals(_recordingStatusSource, App.Services.Recording))
        {
            if (_recordingStatusSource is not null) _recordingStatusSource.StateChanged -= Recording_StateChanged;
            _recordingStatusSource = App.Services.Recording;
            _recordingStatusSource.StateChanged += Recording_StateChanged;
        }
        ChooseCaptureFolderButton.IsEnabled = !App.Services.RecordingSession.IsActive;
    }

    private void Recording_StateChanged(object? sender, RecordingState state) => DispatcherQueue.TryEnqueue(() =>
        ChooseCaptureFolderButton.IsEnabled = state is RecordingState.Idle or RecordingState.Failed);

    /// <summary>Reads the library once, on the first reveal that needs it.</summary>
    public async Task EnsureLibraryLoadedAsync()
    {
        if (_libraryLoaded)
        {
            return;
        }
        _libraryLoaded = true;
        var load = ViewModel.InitializeAsync();
        UpdateLibraryVisibility();
        await load;
        UpdateLibraryVisibility();
        SyncDeleteAffordance();
        await UpdateDetailAsync();
        // Focus the list so arrow keys work without clicking into it first.
        if (ViewModel.Items.Count > 0)
        {
            CaptureList.Focus(FocusState.Programmatic);
        }
        // Fire and forget: the list is already usable, thumbnails fill in behind it.
        _ = LoadThumbnailsAsync();
    }

    /// <summary>Teach the activation shortcut where it is used, not only in Settings.</summary>
    private void ShowActivationShortcut()
    {
        var shortcut = App.Services.Hotkey.RegisteredShortcut;
        NewCaptureShortcut.Text = shortcut ?? string.Empty;
        ActivationShortcutHint.Text = shortcut is null
            ? "Choose a working shortcut in Settings"
            : $"{shortcut} over whatever is on screen";
    }

    private void VideoDefaults_SelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        if (sender is not ComboBox box)
        {
            return;
        }
        if (box == FpsBox)
        {
            ViewModel.VideoFramesPerSecond = FpsBox.SelectedIndex == 1 ? 60 : 30;
        }
        else if (box == CountdownBox)
        {
            ViewModel.VideoCountdownSeconds = CountdownBox.SelectedIndex switch { 0 => 0, 2 => 5, _ => 3 };
        }
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs eventArgs)
    {
        // SelectionChanged can fire while InitializeComponent is still constructing
        // the page, before the three named panels have been assigned.
        if (LibraryPanel is null || CapturePanel is null || SettingsPanel is null)
        {
            return;
        }
        var tag = (eventArgs.SelectedItemContainer?.Tag as string) ?? "library";
        if ((_editingTextId is not null || _startingTextEdit) && tag != "library")
        {
            Navigation.SelectedItem = LibraryNav;
            SetLibraryStatus("Save or cancel the text edit before leaving the Library");
            return;
        }
        LibraryPanel.Visibility = tag == "library" ? Visibility.Visible : Visibility.Collapsed;
        CapturePanel.Visibility = tag == "capture" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CaptureStore_CaptureCompleted(object? sender, CaptureCompletedEventArgs eventArgs)
    {
        App.DispatcherQueue.TryEnqueue(async () =>
        {
            if (!_libraryLoaded)
            {
                // Nothing is displayed yet; the first reveal will read this capture
                // from the manifest along with the rest.
                return;
            }
            var editingId = _editingTextId;
            await ViewModel.CaptureAddedAsync(eventArgs.Record);
            if (editingId is not null)
            {
                var editingItem = ViewModel.Items.FirstOrDefault(item => item.Id == editingId);
                if (editingItem is not null)
                {
                    ViewModel.SelectedItem = editingItem;
                    CaptureList.SelectedItem = editingItem;
                }
            }
            UpdateLibraryVisibility();
            SyncDeleteAffordance();
            if (editingId is null)
            {
                await UpdateDetailAsync();
            }
            await LoadThumbnailsAsync();
        });
    }

    private void Filter_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: string filter })
        {
            ViewModel.SelectFilter(filter);
            ApplyFilterSelection(filter);
            UpdateLibraryVisibility();
        }
    }

    /// <summary>
    /// Library shortcuts stand down while a text box has focus — the capture folder
    /// box would otherwise lose Space and Ctrl+A — and while another panel is showing.
    /// </summary>
    private bool LibraryKeysActive() =>
        LibraryPanel.Visibility == Visibility.Visible &&
        _editingTextId is null &&
        FocusManager.GetFocusedElement(XamlRoot) is not TextBox;

    private async void OpenAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs eventArgs)
    {
        if (!LibraryKeysActive() || ViewModel.SelectedItem is null)
        {
            return;
        }
        eventArgs.Handled = true;
        await OpenSelectedAsync();
    }

    private void EditAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs eventArgs)
    {
        var focused = FocusManager.GetFocusedElement(XamlRoot);
        if (LibraryPanel.Visibility != Visibility.Visible ||
            ViewModel.SelectedItem is null ||
            _editingTextId is not null ||
            (focused is TextBox && !ReferenceEquals(focused, DetailTextEditor)))
        {
            return;
        }
        eventArgs.Handled = true;
        Edit_Click(this, new RoutedEventArgs());
    }

    private void SaveTextAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs eventArgs)
    {
        if (_editingTextId is null)
        {
            return;
        }
        eventArgs.Handled = true;
        SaveText_Click(this, new RoutedEventArgs());
    }

    private void CancelTextEditAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs eventArgs)
    {
        if (_editingTextId is null)
        {
            return;
        }
        eventArgs.Handled = true;
        CancelTextEdit();
    }

    private void RevealAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs eventArgs)
    {
        if (!LibraryKeysActive() || ViewModel.SelectedItem is null)
        {
            return;
        }
        eventArgs.Handled = true;
        ViewModel.RevealSelectedCommand.Execute(null);
    }

    private void CopyCaptureAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs eventArgs)
    {
        if (!LibraryKeysActive() || ViewModel.SelectedItem is null)
        {
            return;
        }
        eventArgs.Handled = true;
        CopyCapture_Click(this, new RoutedEventArgs());
    }

    private void DeleteAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs eventArgs)
    {
        if (!LibraryKeysActive() || CaptureList.SelectedItems.Count == 0)
        {
            return;
        }
        eventArgs.Handled = true;
        DeleteSelected_Click(this, new RoutedEventArgs());
    }

    /// <summary>Play or pause whatever is loaded, so a recording can be reviewed without the mouse.</summary>
    private void PlayAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs eventArgs)
    {
        if (!LibraryKeysActive() || DetailPlayer.MediaPlayer is null || DetailPlayer.Visibility != Visibility.Visible)
        {
            return;
        }
        eventArgs.Handled = true;
        var player = DetailPlayer.MediaPlayer;
        if (player.PlaybackSession.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing)
        {
            player.Pause();
        }
        else
        {
            player.Play();
        }
    }

    private void MaximizeAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs eventArgs)
    {
        if (!LibraryKeysActive())
        {
            return;
        }
        eventArgs.Handled = true;
        MaximizePreview_Click(this, new RoutedEventArgs());
    }

    private void SelectAllAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs eventArgs)
    {
        if (!LibraryKeysActive())
        {
            return;
        }
        eventArgs.Handled = true;
        CaptureList.SelectAll();
    }

    private void FilterAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs eventArgs)
    {
        if (!LibraryKeysActive())
        {
            return;
        }
        eventArgs.Handled = true;
        var index = sender.Key - Windows.System.VirtualKey.Number1;
        if (index >= 0 && index < FilterBar.Children.Count && FilterBar.Children[index] is Button filter)
        {
            Filter_Click(filter, new RoutedEventArgs());
        }
    }

    private void CaptureList_SelectionChanged(object sender, SelectionChangedEventArgs eventArgs)
    {
        // The delete affordance is cheap and must track the selection immediately.
        SyncDeleteAffordance();
        if (_editingTextId is not null)
        {
            return;
        }
        // Loading the detail is not cheap: arrow-keying down the list would open a
        // media pipeline and generate a waveform for every row passed through.
        // The superseded source is cancelled but deliberately not disposed — the
        // preview path still registers callbacks on its token, which would throw
        // ObjectDisposedException against a disposed source.
        _detailLoad?.Cancel();
        var pending = new CancellationTokenSource();
        _detailLoad = pending;
        _ = DebounceDetailAsync(pending.Token);
    }

    /// <summary>
    /// Delete acts on the whole selection, so the button has to say how many captures
    /// are about to move to the Recycle Bin.
    /// </summary>
    private void SyncDeleteAffordance()
    {
        var count = CaptureList.SelectedItems.Count;
        DeleteButton.IsEnabled = count > 0;
        DeleteCountText.Text = count.ToString();
        DeleteCountText.Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void DeleteSelected_Click(object sender, RoutedEventArgs eventArgs)
    {
        var selected = CaptureList.SelectedItems.OfType<CaptureItemViewModel>().ToList();
        if (selected.Count == 0)
        {
            return;
        }
        // Deletion goes to the Recycle Bin, so this stays recoverable without a prompt.
        await ViewModel.DeleteAsync(selected);
        SyncDeleteAffordance();
        await UpdateDetailAsync();
    }

    /// <summary>
    /// Hides the list so the preview gets the whole window. The Library window itself
    /// is resizable and remembers its size, so this is about the split, not the window.
    /// </summary>
    private void MaximizePreview_Click(object sender, RoutedEventArgs eventArgs)
    {
        SetPreviewMaximized(!_previewMaximized);
    }

    /// <summary>
    /// Spans the detail surface across both columns instead of only zeroing the list
    /// column. The narrow visual state collapses DetailColumn, so changing widths alone
    /// would leave Enter or a double-tap looking like it opened an empty Library.
    /// </summary>
    private void SetPreviewMaximized(bool maximized)
    {
        _previewMaximized = maximized;
        FilterHost.Visibility = maximized ? Visibility.Collapsed : Visibility.Visible;
        if (maximized)
        {
            ListPane.Visibility = Visibility.Collapsed;
            Grid.SetColumn(DetailPane, 0);
            Grid.SetColumnSpan(DetailPane, 2);
            DetailPane.Visibility = Visibility.Visible;
        }
        else
        {
            ListPane.Visibility = Visibility.Visible;
            Grid.SetColumn(DetailPane, 1);
            Grid.SetColumnSpan(DetailPane, 1);
            // Let LibraryNarrow/LibraryWide own this property again after the
            // explicit preview-only override is removed.
            DetailPane.ClearValue(VisibilityProperty);
        }
        MaximizePreviewIcon.Glyph = maximized ? "" : "";
        var maximizeLabel = maximized ? "Show the capture list" : "Fill the window with the preview";
        ToolTipService.SetToolTip(MaximizePreviewButton, maximizeLabel);
        AutomationProperties.SetName(MaximizePreviewButton, maximizeLabel);
    }

    /// <summary>
    /// Fills in the real screenshot, video frame, or waveform for each row. Runs after
    /// the list is already on screen and one at a time, so a folder full of large
    /// recordings never delays the Library appearing.
    /// </summary>
    private async Task LoadThumbnailsAsync()
    {
        foreach (var item in ViewModel.Items.ToList())
        {
            if (item.Thumbnail is not null)
            {
                continue;
            }
            try
            {
                var preview = await App.Services.Previews.GetPreviewAsync(item.Record);
                if (preview is not null)
                {
                    item.Thumbnail = new BitmapImage(new Uri(preview)) { DecodePixelWidth = 104 };
                }
            }
            catch (Exception)
            {
                // A capture whose preview cannot be produced keeps its kind icon.
            }
        }
    }
    private void ApplyFilterSelection(string filter)
    {
        foreach (var button in FilterBar.Children.OfType<Button>())
        {
            var selected = string.Equals(button.Tag as string, filter, StringComparison.Ordinal);
            button.Background = App.Theme.Brush(selected ? "PocketRaised" : "PocketTransparent");
            button.BorderBrush = App.Theme.Brush(selected ? "PocketLine" : "PocketTransparent");
            button.Foreground = App.Theme.Brush(selected ? "PocketInk" : "PocketMuted");
        }
    }

    /// <summary>Opening from the row keeps the primary action reachable when the
    /// detail pane has given way to the list at narrow widths.</summary>
    private async void CaptureList_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs eventArgs)
        => await OpenSelectedAsync();

    private async Task DebounceDetailAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(120, cancellationToken);
            await UpdateDetailAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // A newer selection is already loading.
        }
    }

    private async Task UpdateDetailAsync(CancellationToken cancellationToken = default)
    {
        if (_editingTextId is not null)
        {
            return;
        }
        DetailPlayer.Source = null;
        DetailPlayer.Visibility = Visibility.Collapsed;
        DetailPlayer.Height = double.NaN;
        DetailPlayer.VerticalAlignment = VerticalAlignment.Stretch;
        DetailImage.Source = null;
        DetailImage.Visibility = Visibility.Collapsed;
        DetailTextEditor.Visibility = Visibility.Collapsed;
        DetailTextEditor.IsReadOnly = true;
        _textDetailLoadedFromFile = false;
        DetailTextPanel.Visibility = Visibility.Visible;

        var item = ViewModel.SelectedItem;
        if (item is null)
        {
            DetailKind.Text = "Capture preview";
            DetailTitle.Text = "Choose something from your library";
            DetailIcon.Glyph = "\uE7C3";
            DetailText.Text = "Screenshots, waveforms, and video posters appear here.";
            DetailFacts.Visibility = Visibility.Collapsed;
            return;
        }

        DetailFacts.Visibility = Visibility.Visible;
        DetailKind.Text = item.KindLabel;
        DetailTitle.Text = item.Preview;
        FactKind.Text = item.KindLabel;
        FactSize.Text = item.SizeLabel;
        FactSaved.Text = item.SavedLabel;
        FactFile.Text = item.FileName;
        DetailIcon.Glyph = item.IconGlyph;
        DetailText.Text = item.AbsolutePath;
        var editable = item.Record.CaptureKind is CaptureKind.Screenshot or CaptureKind.Text;
        EditButton.Visibility = editable ? Visibility.Visible : Visibility.Collapsed;
        EditButton.Content = item.Record.CaptureKind == CaptureKind.Text ? "Edit" : "Mark up";
        ToolTipService.SetToolTip(
            EditButton,
            item.Record.CaptureKind == CaptureKind.Text
                ? "Edit this text inside the Library · Ctrl+E"
                : "Ctrl+E — saves an edited copy and leaves this one alone");
        OpenButton.Content = item.Record.CaptureKind == CaptureKind.Text ? "Open here" : "Open";
        if (!File.Exists(item.AbsolutePath))
        {
            DetailText.Text = "This file is no longer in the capture folder.";
            return;
        }

        if (item.Record.CaptureKind == CaptureKind.Text)
        {
            DetailTextPanel.Visibility = Visibility.Collapsed;
            try
            {
                DetailTextEditor.Text = await File.ReadAllTextAsync(item.AbsolutePath, cancellationToken);
                _textDetailLoadedFromFile = true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                DetailTextEditor.Text = item.Preview;
            }
            DetailTextEditor.Visibility = Visibility.Visible;
            return;
        }

        if (item.Record.CaptureKind == CaptureKind.Video)
        {
            DetailTextPanel.Visibility = Visibility.Collapsed;
            DetailPlayer.Source = MediaSource.CreateFromUri(new Uri(item.AbsolutePath));
            DetailPlayer.Visibility = Visibility.Visible;
            return;
        }

        if (item.Record.CaptureKind == CaptureKind.Audio)
        {
            DetailTextPanel.Visibility = Visibility.Collapsed;
            var waveform = await App.Services.Previews.GetPreviewAsync(item.Record, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (waveform is not null)
            {
                DetailImage.Source = new BitmapImage(new Uri(waveform));
                DetailImage.Visibility = Visibility.Visible;
            }
            DetailPlayer.Source = MediaSource.CreateFromUri(new Uri(item.AbsolutePath));
            DetailPlayer.Height = 92;
            DetailPlayer.VerticalAlignment = VerticalAlignment.Bottom;
            DetailPlayer.Visibility = Visibility.Visible;
            return;
        }

        var preview = await App.Services.Previews.GetPreviewAsync(item.Record, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (preview is not null)
        {
            DetailTextPanel.Visibility = Visibility.Collapsed;
            DetailImage.Source = new BitmapImage(new Uri(preview));
            DetailImage.Visibility = Visibility.Visible;
            return;
        }

    }

    private void UpdateLibraryVisibility()
    {
        var failed = !string.IsNullOrWhiteSpace(ViewModel.LibraryErrorMessage);
        LoadingLibrary.Visibility = ViewModel.IsBusy ? Visibility.Visible : Visibility.Collapsed;
        LibraryError.Visibility = !ViewModel.IsBusy && failed ? Visibility.Visible : Visibility.Collapsed;
        EmptyLibrary.Visibility = !ViewModel.IsBusy && !failed && ViewModel.Items.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        CaptureList.Visibility = !ViewModel.IsBusy && !failed && ViewModel.Items.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void RetryLibrary_Click(object sender, RoutedEventArgs eventArgs)
    {
        _libraryLoaded = false;
        await EnsureLibraryLoadedAsync();
    }

    private void OpenCommand_Click(object sender, RoutedEventArgs eventArgs) => (App.Window as MainWindow)?.ShowCommandPalette();
    private void LibraryTile_Click(object sender, RoutedEventArgs eventArgs) => NavigateTo("library");
    private void ScreenshotTile_Click(object sender, RoutedEventArgs eventArgs) => (App.Window as MainWindow)?.ShowCommandPalette("screenshot");
    private void VideoTile_Click(object sender, RoutedEventArgs eventArgs) => (App.Window as MainWindow)?.ShowVideoPreflight();
    private async void RepeatVideoTile_Click(object sender, RoutedEventArgs eventArgs) => await (App.Window as MainWindow)!.RepeatVideoRecordingAsync();
    private async void AudioTile_Click(object sender, RoutedEventArgs eventArgs) => await (App.Window as MainWindow)!.ToggleAudioRecordingAsync();
    private async void TextTile_Click(object sender, RoutedEventArgs eventArgs) => await (App.Window as MainWindow)!.CaptureTextAsync();
    private async void LinkTile_Click(object sender, RoutedEventArgs eventArgs) => await (App.Window as MainWindow)!.CaptureLinkAsync();

    private void OpenFolder_Click(object sender, RoutedEventArgs eventArgs) => ViewModel.OpenCaptureFolderCommand.Execute(null);

    private void RunWelcomeTour_Click(object sender, RoutedEventArgs eventArgs) =>
        (App.Window as MainWindow)?.ShowOnboarding();

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs eventArgs)
    {
        CheckForUpdatesButton.IsEnabled = false;
        UpdateStatusText.Text = "Checking for updates…";
        try
        {
            await (App.Window as MainWindow)!.CheckForUpdatesAsync();
        }
        finally
        {
            RefreshUpdateStatus();
        }
    }

    public void RefreshUpdateStatus()
    {
        if (InstalledVersionText is null || UpdateStatusText is null || CheckForUpdatesButton is null) return;
        InstalledVersionText.Text = $"CursorPocket {App.Services.Updates.CurrentVersion}";
        UpdateStatusText.Text = App.Services.Updates.StatusMessage;
        CheckForUpdatesButton.IsEnabled = !App.Services.Updates.IsChecking && !App.Services.Updates.IsDownloading;
    }

    private void ThemeMode_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: string value } && int.TryParse(value, out var index))
        {
            ViewModel.ThemeModeIndex = index;
            ApplyThemeModeSelection();
        }
    }

    private void ApplyThemeModeSelection()
    {
        foreach (var button in new[] { ThemeSystemButton, ThemeLightButton, ThemeDarkButton })
        {
            var selected = int.TryParse(button.Tag as string, out var index) && index == ViewModel.ThemeModeIndex;
            button.Background = App.Theme.Brush(selected ? "PocketGreenSoft" : "PocketRaised");
            button.BorderBrush = App.Theme.Brush(selected ? "PocketGreen" : "PocketLine");
        }
    }

    private async void ChooseCaptureFolder_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (App.Services.RecordingSession.IsActive)
        {
            ViewModel.StatusMessage = "Finish the current recording before changing the capture folder.";
            return;
        }
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            ViewModel.CaptureDirectory = folder.Path;
        }
    }

    /// <summary>
    /// Opens the annotation editor for a screenshot, or turns the Library's own text
    /// viewer into an editor. Text never needs a second application.
    /// </summary>
    private async void Edit_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (ViewModel.SelectedItem is not { } item)
        {
            return;
        }
        if (item.Record.CaptureKind == CaptureKind.Text)
        {
            await BeginTextEditAsync(item);
        }
        else if (item.Record.CaptureKind == CaptureKind.Screenshot)
        {
            (App.Window as MainWindow)?.AnnotateExisting(item.Record);
        }
    }

    private async void Open_Click(object sender, RoutedEventArgs eventArgs) => await OpenSelectedAsync();

    private async Task OpenSelectedAsync()
    {
        var item = ViewModel.SelectedItem;
        if (item?.Record.CaptureKind != CaptureKind.Text)
        {
            ViewModel.OpenSelectedCommand.Execute(null);
            return;
        }
        if (!await EnsureTextDetailReadyAsync(item))
        {
            return;
        }
        DetailTextEditor.Focus(FocusState.Programmatic);
        SetLibraryStatus(
            _textDetailLoadedFromFile
                ? "Text opened in Library · Ctrl+E to edit"
                : "Full text could not be loaded · showing the Library preview",
            isError: !_textDetailLoadedFromFile);
    }

    private async Task BeginTextEditAsync(CaptureItemViewModel item)
    {
        if (_editingTextId is not null || _startingTextEdit)
        {
            return;
        }
        _startingTextEdit = true;
        _detailLoad?.Cancel();
        SetTextEditSurroundingsEnabled(false);
        try
        {
            if (!await EnsureTextDetailReadyAsync(item, requireFullText: true) || ViewModel.SelectedItem?.Id != item.Id)
            {
                return;
            }

            _editingTextId = item.Id;
            _textBeforeEdit = DetailTextEditor.Text;
            DetailTextEditor.IsReadOnly = false;
            DefaultActions.Visibility = Visibility.Collapsed;
            TextEditActions.Visibility = Visibility.Visible;
            DeleteButton.Visibility = Visibility.Collapsed;
            DetailKind.Text = "Editing text";
            DetailTextEditor.Focus(FocusState.Programmatic);
            DetailTextEditor.SelectionStart = DetailTextEditor.Text.Length;
            SetLibraryStatus("Editing text in Library · Ctrl+S to save · Escape to cancel");
        }
        finally
        {
            _startingTextEdit = false;
            if (_editingTextId is null)
            {
                SetTextEditSurroundingsEnabled(true);
            }
        }
    }

    private async Task<bool> EnsureTextDetailReadyAsync(CaptureItemViewModel item, bool requireFullText = false)
    {
        if (!File.Exists(item.AbsolutePath))
        {
            SetLibraryStatus("This text capture is missing", isError: true);
            return false;
        }
        if (DetailTextEditor.Visibility != Visibility.Visible)
        {
            await UpdateDetailAsync();
        }
        if (ViewModel.SelectedItem?.Id != item.Id)
        {
            return false;
        }
        if (!_previewMaximized)
        {
            SetPreviewMaximized(true);
        }
        if (requireFullText && !_textDetailLoadedFromFile)
        {
            SetLibraryStatus("Full text could not be loaded · retry before editing", isError: true);
            return false;
        }
        return DetailTextEditor.Visibility == Visibility.Visible;
    }

    private async void SaveText_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_savingText)
        {
            return;
        }
        var item = ViewModel.Items.FirstOrDefault(candidate => candidate.Id == _editingTextId);
        if (item is null)
        {
            CancelTextEdit();
            return;
        }

        try
        {
            _savingText = true;
            SaveTextButton.IsEnabled = false;
            await App.Services.CaptureStore.UpdateTextAsync(item.Record, DetailTextEditor.Text);
            EndTextEdit();
            await ViewModel.InitializeAsync();
            var refreshed = ViewModel.Items.FirstOrDefault(candidate => candidate.Id == item.Id);
            if (refreshed is not null)
            {
                ViewModel.SelectedItem = refreshed;
                CaptureList.SelectedItem = refreshed;
            }
            SyncDeleteAffordance();
            await UpdateDetailAsync();
            SetLibraryStatus("Text changes saved");
        }
        catch (ArgumentException)
        {
            SetLibraryStatus("Text cannot be empty", isError: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SetLibraryStatus($"Text could not be saved · {exception.Message}", isError: true);
        }
        catch (Exception exception)
        {
            SetLibraryStatus(
                $"Text could not be saved safely · {exception.Message} · keep this editor open and inspect the capture folder",
                isError: true);
        }
        finally
        {
            _savingText = false;
            SaveTextButton.IsEnabled = true;
        }
    }

    private void CancelTextEdit_Click(object sender, RoutedEventArgs eventArgs) => CancelTextEdit();

    private void CancelTextEdit()
    {
        if (_savingText)
        {
            return;
        }
        if (_textBeforeEdit is not null)
        {
            DetailTextEditor.Text = _textBeforeEdit;
        }
        EndTextEdit();
        if (ViewModel.SelectedItem is { } item)
        {
            DetailKind.Text = item.KindLabel;
        }
        SetLibraryStatus("Text changes discarded");
    }

    private void EndTextEdit()
    {
        _editingTextId = null;
        _textBeforeEdit = null;
        DetailTextEditor.IsReadOnly = true;
        DefaultActions.Visibility = Visibility.Visible;
        TextEditActions.Visibility = Visibility.Collapsed;
        DeleteButton.Visibility = Visibility.Visible;
        SetTextEditSurroundingsEnabled(true);
    }

    private void SetTextEditSurroundingsEnabled(bool enabled)
    {
        foreach (var button in FilterBar.Children.OfType<Button>())
        {
            button.IsEnabled = enabled;
        }
        CaptureList.IsEnabled = enabled;
        CaptureNav.IsEnabled = enabled;
        SettingsNav.IsEnabled = enabled;
        MaximizePreviewButton.IsEnabled = enabled;
    }

    private void SetLibraryStatus(string message, bool isError = false)
    {
        ViewModel.StatusMessage = message;
        LibraryStatusDot.Visibility = isError ? Visibility.Collapsed : Visibility.Visible;
        LibraryStatusText.Foreground = App.Theme.Brush(isError ? "PocketRed" : "PocketMuted");
        AutomationProperties.SetLiveSetting(
            LibraryStatusText,
            isError ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);
    }

    private async void CopyCapture_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (ViewModel.SelectedItem is not { } item)
        {
            return;
        }

        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(item.AbsolutePath);
            var package = new DataPackage();
            package.SetStorageItems([file]);
            if (item.Record.CaptureKind == CaptureKind.Screenshot)
            {
                package.SetBitmap(Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(file));
            }
            Clipboard.SetContent(package);
            Clipboard.Flush();
            SetLibraryStatus("Capture copied");
        }
        catch (Exception)
        {
            SetLibraryStatus("Capture could not be copied", isError: true);
        }
    }

    private void Page_Unloaded(object sender, RoutedEventArgs eventArgs)
    {
        if (!_themeSubscribed) return;
        App.Theme.ThemeChanged -= Theme_ThemeChanged;
        _themeSubscribed = false;
    }

    private void Theme_ThemeChanged(object? sender, EventArgs eventArgs)
    {
        ApplyLibraryPaneGlass();
        ApplyFilterSelection(ViewModel.SelectedFilter);
        ApplyThemeModeSelection();
    }

    private void ApplyLibraryPaneGlass()
    {
        // These are the two dominant Library planes. Give each a fresh material so
        // a live setting change cannot keep a brush resolved under the previous theme.
        LibraryListPane.Background = App.Theme.GlassBrush();
        DetailPane.Background = App.Theme.GlassBrush();
    }
}
