using CursorPocket.Core.Models;
using CursorPocket.Core.Services;
using CursorPocket_App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace CursorPocket_App;

public sealed partial class RecordingHudWindow : Window
{
    private const int CollapsedWidth = 178;
    private const int CollapsedHeight = 30;
    private const int ExpandedWidth = 452;
    private const int ExpandedHeight = 92;
    private const int TopMargin = 0;

    private readonly Func<bool, Task> _stop;
    private readonly IDisposable _escapeLease;
    private readonly AudioLevelHistory _levels = new();
    private readonly List<Rectangle> _collapsedBars = [];
    private readonly List<Rectangle> _expandedBars = [];
    private readonly bool _hasAudio;
    private bool _expanded;
    private bool _stopping;

    private RecordingHudWindow(string mode, string device, bool hasAudio, Func<bool, Task> stop)
    {
        _stop = stop;
        _hasAudio = hasAudio;
        InitializeComponent();
        ModeText.Text = mode;
        DeviceText.Text = device;
        WindowPlacement.ConfigureUtilityWindow(this);
        BuildMeters();
        ApplySize();
        _escapeLease = App.Services.EscapeHotkey.Capture(() =>
            DispatcherQueue.TryEnqueue(async () => await StopAsync(false)));
        var ready = App.Services.Recording.State == RecordingState.Recording;
        ModeText.Text = ready ? mode : "Starting…";
        StopButton.IsEnabled = ready;
        DiscardButton.IsEnabled = ready;
        SyncCollapsedState(App.Services.Recording.State);
        App.Services.Recording.ElapsedChanged += Recording_ElapsedChanged;
        App.Services.Recording.AudioLevelChanged += Recording_AudioLevelChanged;
        App.Services.Recording.StateChanged += Recording_StateChanged;
        Closed += (_, _) =>
        {
            _escapeLease.Dispose();
            Unsubscribe();
        };
    }

    public static void ShowForAudio(string device, Func<bool, Task> stop)
    {
        var window = new RecordingHudWindow("Audio note", device, hasAudio: true, stop);
        window.AppWindow.Show(false);
    }

    public static void ShowForVideo(RecordingOptions options, Func<bool, Task> stop)
    {
        var detail = options.IncludeCamera
            ? $"Screen · mic {(options.IncludeMicrophone ? "on" : "off")} · camera on"
            : $"Screen · mic {(options.IncludeMicrophone ? "on" : "off")} · camera off";
        var window = new RecordingHudWindow("Screen recording", detail, options.IncludeMicrophone, stop);
        window.AppWindow.Show(false);
    }

    /// <summary>
    /// Builds the level meter as a row of bars fed from a rolling history, so the
    /// audio reads as a moving waveform rather than one bar sliding left and right.
    /// </summary>
    private void BuildMeters()
    {
        CollapsedMeter.Visibility = _hasAudio ? Visibility.Visible : Visibility.Collapsed;
        ExpandedMeter.Visibility = _hasAudio ? Visibility.Visible : Visibility.Collapsed;
        if (!_hasAudio)
        {
            return;
        }
        var green = (Brush)Application.Current.Resources["PocketGreen"];
        for (var index = 0; index < AudioLevelHistory.Length; index++)
        {
            if (index >= AudioLevelHistory.Length - CollapsedBarCount)
            {
                var small = NewBar(green, 2.5);
                _collapsedBars.Add(small);
                CollapsedMeter.Children.Add(small);
            }
            var bar = NewBar(green, 3);
            _expandedBars.Add(bar);
            ExpandedMeter.Children.Add(bar);
        }
        RenderMeters();
    }

    private const int CollapsedBarCount = 10;

    private static Rectangle NewBar(Brush fill, double width) => new()
    {
        Width = width,
        Height = 2,
        RadiusX = width / 2,
        RadiusY = width / 2,
        Fill = fill,
        VerticalAlignment = VerticalAlignment.Bottom,
    };

    private void RenderMeters()
    {
        if (!_hasAudio)
        {
            return;
        }
        for (var index = 0; index < _expandedBars.Count; index++)
        {
            _expandedBars[index].Height = AudioLevelHistory.BarHeight(_levels[index], 24);
        }
        var offset = AudioLevelHistory.Length - _collapsedBars.Count;
        for (var index = 0; index < _collapsedBars.Count; index++)
        {
            _collapsedBars[index].Height = AudioLevelHistory.BarHeight(_levels[offset + index], 14);
        }
    }

    private void Root_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs eventArgs) => SetExpanded(true);
    private void Root_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs eventArgs) => SetExpanded(false);
    // Keyboard users reach the actions without a pointer ever entering the pill.
    private void Root_GotFocus(object sender, RoutedEventArgs eventArgs) => SetExpanded(true);

    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded)
        {
            return;
        }
        _expanded = expanded;
        CollapsedView.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
        ExpandedView.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        ApplySize();
        RenderMeters();
    }

    private void ApplySize()
    {
        var width = _expanded ? ExpandedWidth : CollapsedWidth;
        var height = _expanded ? ExpandedHeight : CollapsedHeight;
        WindowPlacement.PlaceTopCenter(this, width, height, TopMargin);
        WindowPlacement.ClipToRoundedRegion(this, width, height, _expanded ? 16 : CollapsedHeight / 2);
    }

    private void Recording_ElapsedChanged(object? sender, TimeSpan elapsed) => DispatcherQueue.TryEnqueue(() =>
    {
        var text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}";
        TimerText.Text = text;
        CollapsedTimerText.Text = text;
    });

    private void Recording_AudioLevelChanged(object? sender, double level) => DispatcherQueue.TryEnqueue(() =>
    {
        _levels.Push(level);
        RenderMeters();
    });

    private void Recording_StateChanged(object? sender, RecordingState state) => DispatcherQueue.TryEnqueue(() =>
    {
        if (state == RecordingState.Starting)
        {
            ModeText.Text = "Starting…";
            StopButton.IsEnabled = false;
            DiscardButton.IsEnabled = false;
        }
        else if (state == RecordingState.Recording)
        {
            ModeText.Text = ModeText.Text == "Starting…" ? "Recording" : ModeText.Text;
            StopButton.IsEnabled = true;
            DiscardButton.IsEnabled = true;
        }
        else if (state == RecordingState.Finalizing)
        {
            ModeText.Text = "Finalizing…";
            StopButton.IsEnabled = false;
            DiscardButton.IsEnabled = false;
        }
        else if (state is RecordingState.Idle or RecordingState.Failed)
        {
            Close();
        }
        SyncCollapsedState(state);
    });

    /// <summary>
    /// The collapsed pill has no room for the mode line, so it carries state as a
    /// live/idle mark plus a tooltip. The running timer stays the non-colour cue that
    /// a recording is in progress.
    /// </summary>
    private void SyncCollapsedState(RecordingState state)
    {
        var live = state == RecordingState.Recording;
        CollapsedDot.Opacity = live ? 1 : 0.45;
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(CollapsedView, live
            ? $"{ModeText.Text} · hover to stop or discard"
            : ModeText.Text);
    }

    private async void Stop_Click(object sender, RoutedEventArgs eventArgs) => await StopAsync(false);
    private async void Discard_Click(object sender, RoutedEventArgs eventArgs) => await StopAsync(true);

    private async Task StopAsync(bool discard)
    {
        if (_stopping)
        {
            return;
        }
        _stopping = true;
        ModeText.Text = discard ? "Discarding…" : "Finalizing…";
        try
        {
            await _stop(discard);
            if (App.Services.Recording.State is RecordingState.Idle or RecordingState.Failed)
            {
                Close();
            }
        }
        catch (Exception error)
        {
            ModeText.Text = "Could not finish recording";
            DeviceText.Text = error.Message;
            _stopping = false;
        }
    }

    private void Unsubscribe()
    {
        App.Services.Recording.ElapsedChanged -= Recording_ElapsedChanged;
        App.Services.Recording.AudioLevelChanged -= Recording_AudioLevelChanged;
        App.Services.Recording.StateChanged -= Recording_StateChanged;
    }
}
