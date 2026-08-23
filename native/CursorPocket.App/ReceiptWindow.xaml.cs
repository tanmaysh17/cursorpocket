using CursorPocket.Core.Models;
using CursorPocket_App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.System;

namespace CursorPocket_App;

public sealed partial class ReceiptWindow : Window
{
    private const int Width = 500;
    private const int Height = 136;
    private readonly CaptureRecord? _record;
    private readonly DispatcherTimer _timer;
    private TimeSpan _remaining;
    private DateTimeOffset _timerStartedAt;
    private bool _pointerInside;
    private bool _focused;

    public ReceiptWindow(CaptureRecord? record, string title, string? detail, TimeSpan lifetime)
    {
        _record = record;
        _remaining = lifetime;
        _timer = new DispatcherTimer { Interval = lifetime };
        InitializeComponent();
        App.Theme.Register(this, Root, SurfaceRole.Receipt);
        App.Theme.ThemeChanged += Theme_ThemeChanged;
        WindowPlacement.ConfigureUtilityWindow(this);
        WindowPlacement.PlaceBottomRight(this, Width, Height);
        Activated += (_, eventArgs) =>
        {
            _focused = eventArgs.WindowActivationState != WindowActivationState.Deactivated;
            UpdateTimer();
        };
        ReceiptTitle.Text = title;
        ReceiptDetail.Text = detail ?? record?.Preview ?? "Nothing was saved";
        OpenButton.Visibility = record is null ? Visibility.Collapsed : Visibility.Visible;
        RevealButton.Visibility = record is null ? Visibility.Collapsed : Visibility.Visible;
        EditButton.Visibility = record?.CaptureKind == CaptureKind.Screenshot ? Visibility.Visible : Visibility.Collapsed;
        if (record?.CaptureKind is CaptureKind.Video or CaptureKind.Audio) OpenButton.Content = "Play";
        ReceiptIcon.Glyph = record?.CaptureKind switch
        {
            CaptureKind.Screenshot => "\uE91B",
            CaptureKind.Video => "\uE714",
            CaptureKind.Audio => "\uE720",
            CaptureKind.Text => "\uE8C1",
            CaptureKind.Link => "\uE71B",
            _ => "\uEA39",
        };
        ReceiptIcon.Foreground = record is null
            ? App.Theme.Brush("PocketRed")
            : App.Theme.Brush("PocketGreen");
        Closed += (_, _) => App.Theme.ThemeChanged -= Theme_ThemeChanged;
        _timer.Tick += (_, _) => Close();
        DispatcherQueue.TryEnqueue(async () =>
        {
            await LoadPreviewAsync();
            UpdateTimer();
        });
    }

    public event EventHandler? OpenLibraryRequested;

    private void Theme_ThemeChanged(object? sender, EventArgs eventArgs) => DispatcherQueue.TryEnqueue(() =>
        ReceiptIcon.Foreground = _record is null ? App.Theme.Brush("PocketRed") : App.Theme.Brush("PocketGreen"));

    private async Task LoadPreviewAsync()
    {
        if (_record is null) return;
        var preview = await App.Services.Previews.GetPreviewAsync(_record);
        if (preview is null) return;
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
        if (_record is not null) (App.Window as MainWindow)?.AnnotateExisting(_record);
        Close();
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs eventArgs)
    {
        if (eventArgs.Key != VirtualKey.Escape) return;
        eventArgs.Handled = true;
        Close();
    }

    private void Dismiss_Click(object sender, RoutedEventArgs eventArgs) => Close();
    private void Root_PointerEntered(object sender, PointerRoutedEventArgs eventArgs) { _pointerInside = true; UpdateTimer(); }
    private void Root_PointerExited(object sender, PointerRoutedEventArgs eventArgs) { _pointerInside = false; UpdateTimer(); }
    private void UpdateTimer()
    {
        if (_timer.IsEnabled)
        {
            _remaining -= DateTimeOffset.UtcNow - _timerStartedAt;
            _timer.Stop();
        }
        if (_pointerInside || _focused) return;
        if (_remaining <= TimeSpan.Zero)
        {
            Close();
            return;
        }
        _timer.Interval = _remaining;
        _timerStartedAt = DateTimeOffset.UtcNow;
        _timer.Start();
    }
}

