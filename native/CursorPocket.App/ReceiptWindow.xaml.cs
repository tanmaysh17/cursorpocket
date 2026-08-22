using CursorPocket.Core.Models;
using CursorPocket_App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;

namespace CursorPocket_App;

public sealed partial class ReceiptWindow : Window
{
    private const int Width = 430;
    private const int Height = 150;

    private readonly CaptureRecord? _record;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(12) };
    // A receipt never takes focus, so its actions are reachable only through global
    // keys. They are modified combinations on purpose: the receipt stays up while the
    // user carries on working, and bare keys would swallow their typing.
    private readonly PaletteHotkeyService _keys = new(
        [
            new(VirtualKey.O, Control: true, Alt: true),
            new(VirtualKey.E, Control: true, Alt: true),
            new(VirtualKey.R, Control: true, Alt: true),
            new(VirtualKey.L, Control: true, Alt: true),
            new(VirtualKey.X, Control: true, Alt: true),
        ],
        "CursorPocket.ReceiptKeys");

    public ReceiptWindow(CaptureRecord? record, string title, string? detail = null)
    {
        _record = record;
        InitializeComponent();
        WindowPlacement.ConfigureUtilityWindow(this);
        WindowPlacement.PlaceBottomRight(this, Width, Height);
        WindowPlacement.ClipToRoundedRegion(this, Width, Height, 16);
        _keys.Invoked += Keys_Invoked;
        _keys.SetEnabled(true);
        Closed += (_, _) =>
        {
            _keys.SetEnabled(false);
            _keys.Invoked -= Keys_Invoked;
            _keys.Dispose();
        };
        ReceiptTitle.Text = title;
        ReceiptDetail.Text = detail ?? record?.Preview ?? "Nothing was saved";
        OpenButton.Visibility = record is null ? Visibility.Collapsed : Visibility.Visible;
        RevealButton.Visibility = record is null ? Visibility.Collapsed : Visibility.Visible;
        // The receipt's twelve seconds are exactly when the user is asking "did that come
        // out right?", which makes this the most useful way back into the editor.
        EditButton.Visibility = record?.CaptureKind == CaptureKind.Screenshot
            ? Visibility.Visible
            : Visibility.Collapsed;
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

    private void Edit_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_record is not null)
        {
            (App.Window as MainWindow)?.AnnotateExisting(_record);
        }

        Close();
    }

    private void Keys_Invoked(object? sender, PaletteHotkeyEventArgs eventArgs) => DispatcherQueue.TryEnqueue(() =>
    {
        switch (eventArgs.Key)
        {
            case VirtualKey.O when _record is not null:
                Open_Click(this, new RoutedEventArgs());
                break;
            case VirtualKey.E when _record?.CaptureKind == CaptureKind.Screenshot:
                Edit_Click(this, new RoutedEventArgs());
                break;
            case VirtualKey.R when _record is not null:
                Reveal_Click(this, new RoutedEventArgs());
                break;
            case VirtualKey.L:
                Library_Click(this, new RoutedEventArgs());
                break;
            case VirtualKey.X:
                Dismiss_Click(this, new RoutedEventArgs());
                break;
        }
    });

    private void Library_Click(object sender, RoutedEventArgs eventArgs) { OpenLibraryRequested?.Invoke(this, EventArgs.Empty); Close(); }
    private void Dismiss_Click(object sender, RoutedEventArgs eventArgs) { _timer.Stop(); Close(); }
    private void Root_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs eventArgs) => _timer.Stop();
    private void Root_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs eventArgs) => _timer.Start();
}
