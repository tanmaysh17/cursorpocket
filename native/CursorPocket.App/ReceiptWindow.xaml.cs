using CursorPocket.Core.Models;
using CursorPocket_App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CursorPocket_App;

public sealed partial class ReceiptWindow : Window
{
    private readonly CaptureRecord? _record;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(12) };

    public ReceiptWindow(CaptureRecord? record, string title, string? detail = null)
    {
        _record = record;
        InitializeComponent();
        WindowPlacement.ConfigureUtilityWindow(this);
        WindowPlacement.PlaceBottomRight(this, 430, 128);
        WindowPlacement.ClipToRoundedRegion(this, 430, 128, 16);
        ReceiptTitle.Text = title;
        ReceiptDetail.Text = detail ?? record?.Preview ?? "Nothing was saved";
        OpenButton.Visibility = record is null ? Visibility.Collapsed : Visibility.Visible;
        RevealButton.Visibility = record is null ? Visibility.Collapsed : Visibility.Visible;
        if (record?.CaptureKind is CaptureKind.Video or CaptureKind.Audio)
        {
            OpenButton.Content = "Play";
        }
        ReceiptIcon.Glyph = record?.CaptureKind switch
        {
            CaptureKind.Screenshot => "\uE91B",
            CaptureKind.Video => "\uE714",
            CaptureKind.Audio => "\uE720",
            CaptureKind.Text => "\uE8C1",
            CaptureKind.Link => "\uE71B",
            _ => "\uEA39",
        };
        ReceiptIcon.Foreground = record is null ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PocketRed"] : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["PocketGreen"];
        _timer.Tick += (_, _) => Close();
        DispatcherQueue.TryEnqueue(async () =>
        {
            await LoadPreviewAsync();
            _timer.Start();
        });
    }

    public event EventHandler? OpenLibraryRequested;

    private async Task LoadPreviewAsync()
    {
        if (_record is null)
        {
            return;
        }
        var preview = await App.Services.Previews.GetPreviewAsync(_record);
        if (preview is null)
        {
            return;
        }
        PreviewImage.Source = new BitmapImage(new Uri(preview));
        PreviewImage.Visibility = Visibility.Visible;
        ReceiptIcon.Visibility = Visibility.Collapsed;
    }

    private void Open_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_record?.CaptureKind is CaptureKind.Video or CaptureKind.Audio)
        {
            OpenLibraryRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (_record is not null)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(App.Services.Library.GetAbsolutePath(_record)) { UseShellExecute = true });
        }
        Close();
    }

    private void Reveal_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_record is not null)
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{App.Services.Library.GetAbsolutePath(_record)}\"");
        }
        Close();
    }

    private void Library_Click(object sender, RoutedEventArgs eventArgs) { OpenLibraryRequested?.Invoke(this, EventArgs.Empty); Close(); }
    private void Dismiss_Click(object sender, RoutedEventArgs eventArgs) { _timer.Stop(); Close(); }
    private void Root_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs eventArgs) => _timer.Stop();
    private void Root_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs eventArgs) => _timer.Start();
}
