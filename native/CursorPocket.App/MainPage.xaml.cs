using CursorPocket.Core.Models;
using CursorPocket_App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Core;
using Windows.Storage.Pickers;

namespace CursorPocket_App;

public sealed partial class MainPage : Page
{
    public MainPageViewModel ViewModel { get; } = new(App.Services);
    private bool _loaded;

    public MainPage()
    {
        InitializeComponent();
    }

    public void NavigateTo(string destination)
    {
        Navigation.SelectedItem = destination switch
        {
            "capture" => CaptureNav,
            "settings" => SettingsNav,
            _ => LibraryNav,
        };
    }

    private async void Page_Loaded(object sender, RoutedEventArgs eventArgs)
    {
        if (_loaded)
        {
            return;
        }
        _loaded = true;
        App.Services.CaptureCompleted += CaptureStore_CaptureCompleted;
        await ViewModel.InitializeAsync();
        CompanionModeBox.SelectedIndex = ViewModel.CursorCompanionMode switch { "off" => 0, "always" => 2, _ => 1 };
        FpsBox.SelectedIndex = ViewModel.VideoFramesPerSecond == 60 ? 1 : 0;
        CountdownBox.SelectedIndex = ViewModel.VideoCountdownSeconds switch { 0 => 0, 5 => 2, _ => 1 };
        FpsBox.SelectionChanged += VideoDefaults_SelectionChanged;
        CountdownBox.SelectionChanged += VideoDefaults_SelectionChanged;
        UpdateLibraryVisibility();
        await UpdateDetailAsync();
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
        var tag = (eventArgs.SelectedItemContainer?.Tag as string) ?? "library";
        LibraryPanel.Visibility = tag == "library" ? Visibility.Visible : Visibility.Collapsed;
        CapturePanel.Visibility = tag == "capture" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CaptureStore_CaptureCompleted(object? sender, CaptureCompletedEventArgs eventArgs)
    {
        App.DispatcherQueue.TryEnqueue(async () =>
        {
            await ViewModel.CaptureAddedAsync(eventArgs.Record);
            UpdateLibraryVisibility();
            await UpdateDetailAsync();
        });
    }

    private void Filter_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { Tag: string filter })
        {
            ViewModel.SelectFilter(filter);
            foreach (var button in FilterBar.Children.OfType<Button>())
            {
                button.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources[
                    string.Equals(button.Tag as string, filter, StringComparison.Ordinal) ? "PocketGreenSoft" : "PocketRaised"];
            }
            UpdateLibraryVisibility();
        }
    }

    private async void CaptureList_SelectionChanged(object sender, SelectionChangedEventArgs eventArgs) => await UpdateDetailAsync();

    private async Task UpdateDetailAsync()
    {
        DetailPlayer.Source = null;
        DetailPlayer.Visibility = Visibility.Collapsed;
        DetailPlayer.Height = double.NaN;
        DetailPlayer.VerticalAlignment = VerticalAlignment.Stretch;
        DetailImage.Source = null;
        DetailImage.Visibility = Visibility.Collapsed;
        DetailTextPanel.Visibility = Visibility.Visible;

        var item = ViewModel.SelectedItem;
        if (item is null)
        {
            DetailKind.Text = "Capture preview";
            DetailTitle.Text = "Choose something from your library";
            DetailIcon.Glyph = "\uE7C3";
            DetailText.Text = "Screenshots, waveforms, and video posters appear here.";
            return;
        }

        DetailKind.Text = $"{item.KindLabel} · {item.CreatedLabel}";
        DetailTitle.Text = item.Preview;
        DetailIcon.Glyph = item.IconGlyph;
        DetailText.Text = item.AbsolutePath;
        if (!File.Exists(item.AbsolutePath))
        {
            DetailText.Text = "This file is no longer in the capture folder.";
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
            var waveform = await App.Services.Previews.GetPreviewAsync(item.Record);
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

        var preview = await App.Services.Previews.GetPreviewAsync(item.Record);
        if (preview is not null)
        {
            DetailTextPanel.Visibility = Visibility.Collapsed;
            DetailImage.Source = new BitmapImage(new Uri(preview));
            DetailImage.Visibility = Visibility.Visible;
            return;
        }

        if (item.Record.CaptureKind == CaptureKind.Text)
        {
            try
            {
                DetailText.Text = await File.ReadAllTextAsync(item.AbsolutePath);
            }
            catch (IOException)
            {
                DetailText.Text = item.Preview;
            }
        }
    }

    private void UpdateLibraryVisibility()
    {
        EmptyLibrary.Visibility = ViewModel.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CaptureList.Visibility = ViewModel.Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OpenCommand_Click(object sender, RoutedEventArgs eventArgs) => (App.Window as MainWindow)?.ShowCommandPalette();
    private void ScreenshotTile_Click(object sender, RoutedEventArgs eventArgs) => (App.Window as MainWindow)?.ShowCommandPalette("screenshot");
    private void VideoTile_Click(object sender, RoutedEventArgs eventArgs) => (App.Window as MainWindow)?.ShowVideoPreflight();
    private async void AudioTile_Click(object sender, RoutedEventArgs eventArgs) => await (App.Window as MainWindow)!.ToggleAudioRecordingAsync();
    private async void TextTile_Click(object sender, RoutedEventArgs eventArgs) => await (App.Window as MainWindow)!.CaptureTextAsync();
    private async void LinkTile_Click(object sender, RoutedEventArgs eventArgs) => await (App.Window as MainWindow)!.CaptureLinkAsync();

    private void OpenFolder_Click(object sender, RoutedEventArgs eventArgs) => ViewModel.OpenCaptureFolderCommand.Execute(null);

    private async void ChooseCaptureFolder_Click(object sender, RoutedEventArgs eventArgs)
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            ViewModel.CaptureDirectory = folder.Path;
        }
    }

    private void CopyPath_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (ViewModel.SelectedItem is null)
        {
            return;
        }
        var package = new DataPackage();
        package.SetText(ViewModel.SelectedItem.AbsolutePath);
        Clipboard.SetContent(package);
        ViewModel.StatusMessage = "File path copied";
    }
}
