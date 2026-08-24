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
    private readonly ReceiptRequest _request;
    private readonly ReceiptAction?[] _customActions = new ReceiptAction?[3];
    private readonly DispatcherTimer _timer;
    private TimeSpan _remaining;
    private DateTimeOffset _timerStartedAt;
    private bool _pointerInside;
    private bool _focused;

    public ReceiptWindow(CaptureRecord? record, string title, string? detail, TimeSpan lifetime)
        : this(new ReceiptRequest(record, title, detail, LifetimeOverride: lifetime))
    {
    }

    public ReceiptWindow(ReceiptRequest request)
    {
        _request = request;
        _record = request.Record;
        _remaining = request.Lifetime;
        _timer = new DispatcherTimer { Interval = request.Lifetime };
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
        ReceiptTitle.Text = request.Title;
        ReceiptDetail.Text = request.Detail ?? request.Record?.Preview ?? "Nothing was saved";
        OpenButton.Visibility = request.Record is null ? Visibility.Collapsed : Visibility.Visible;
        RevealButton.Visibility = request.Record is null ? Visibility.Collapsed : Visibility.Visible;
        EditButton.Visibility = request.Record?.CaptureKind == CaptureKind.Screenshot ? Visibility.Visible : Visibility.Collapsed;
        if (request.Record?.CaptureKind is CaptureKind.Video or CaptureKind.Audio) OpenButton.Content = "Play";
        ReceiptIcon.Glyph = request.VisualKind == ReceiptVisualKind.Update
            ? "\uE895"
            : request.VisualKind == ReceiptVisualKind.Information
                ? "\uE946"
                : request.Record?.CaptureKind switch
        {
            CaptureKind.Screenshot => "\uE91B",
            CaptureKind.Video => "\uE714",
            CaptureKind.Audio => "\uE720",
            CaptureKind.Text => "\uE8C1",
            CaptureKind.Link => "\uE71B",
            _ => "\uEA39",
        };
        ReceiptIcon.Foreground = request.VisualKind == ReceiptVisualKind.Error ||
            request.Record is null && request.VisualKind == ReceiptVisualKind.Capture
            ? App.Theme.Brush("PocketRed")
            : App.Theme.Brush("PocketGreen");
        ConfigureCustomActions(request.Actions);
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
        ReceiptIcon.Foreground = _request.VisualKind == ReceiptVisualKind.Error ||
            _record is null && _request.VisualKind == ReceiptVisualKind.Capture
                ? App.Theme.Brush("PocketRed")
                : App.Theme.Brush("PocketGreen"));

    private void ConfigureCustomActions(IReadOnlyList<ReceiptAction>? actions)
    {
        if (actions is null) return;
        var buttons = new[] { OpenButton, EditButton, RevealButton };
        for (var index = 0; index < buttons.Length; index++)
        {
            var action = index < actions.Count ? actions[index] : null;
            _customActions[index] = action;
            buttons[index].Visibility = action is null ? Visibility.Collapsed : Visibility.Visible;
            if (action is not null) buttons[index].Content = action.Label;
        }
    }

    private async Task LoadPreviewAsync()
    {
        if (_record is null) return;
        var preview = await App.Services.Previews.GetPreviewAsync(_record);
        if (preview is null) return;
        PreviewImage.Source = new BitmapImage(new Uri(preview));
        PreviewImage.Visibility = Visibility.Visible;
        ReceiptIcon.Visibility = Visibility.Collapsed;
    }

    private async void Open_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (await InvokeCustomActionAsync(0)) return;
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

    private async void Reveal_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (await InvokeCustomActionAsync(2)) return;
        if (_record is not null)
        {
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{App.Services.Library.GetAbsolutePath(_record)}\"");
        }
        Close();
    }

    private async void Edit_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (await InvokeCustomActionAsync(1)) return;
        if (_record is not null) (App.Window as MainWindow)?.AnnotateExisting(_record);
        Close();
    }

    private async Task<bool> InvokeCustomActionAsync(int index)
    {
        var action = _customActions[index];
        if (action is null) return false;
        Close();
        await action.InvokeAsync();
        return true;
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

