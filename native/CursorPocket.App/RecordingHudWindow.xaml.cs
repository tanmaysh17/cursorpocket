using CursorPocket.Core.Models;
using CursorPocket.Core.Services;
using CursorPocket_App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace CursorPocket_App;

public sealed partial class RecordingHudWindow : Window
{
    // One fixed window size for both states. Closed, it is pushed up so only the
    // bottom strip is on screen; open, it sits flush at the top. Only the position
    // changes, never the size.
    private const int PanelWidth = 300;
    private const int PanelHeight = 96;
    private const int StripHeight = 32;

    private readonly Func<bool, Task> _stop;
    private readonly IDisposable _escapeLease;
    private readonly AudioLevelHistory _levels = new();
    private readonly List<Rectangle> _collapsedBars = [];
    private readonly List<Rectangle> _expandedBars = [];
    private readonly bool _hasAudio;
    // Polls the pointer so the drawer opens as it approaches rather than only once it
    // lands on the strip, and steps the slide, which composition cannot drive for a
    // top-level window.
    private readonly DispatcherTimer _drawer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly System.Diagnostics.Stopwatch _frameClock = System.Diagnostics.Stopwatch.StartNew();
    private int _panelLeft;
    private int _pixelWidth;
    private int _pixelHeight;
    private int _closedTop;
    private int _openTop;
    private int _appliedTop = int.MinValue;
    private double _progress;
    private double _target;
    private bool _focusWithin;
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
        _drawer.Tick += Drawer_Tick;
        _drawer.Start();
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
            _drawer.Stop();
            _drawer.Tick -= Drawer_Tick;
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
        for (var index = 0; index < AudioLevelHistory.Length; index++)
        {
            if (index >= AudioLevelHistory.Length - CollapsedBarCount)
            {
                var small = NewBar(2);
                _collapsedBars.Add(small);
                CollapsedMeter.Children.Add(small);
            }
            var bar = NewBar(3);
            _expandedBars.Add(bar);
            ExpandedMeter.Children.Add(bar);
        }
        RenderMeters();
    }

    private const int CollapsedBarCount = 10;
    private const double ExpandedMeterHeight = 24;
    private const double CollapsedMeterHeight = 14;

    /// <summary>
    /// One stem of the waveform. Centred vertically so each sample grows away from a
    /// mid-line in both directions, which reads as a waveform rather than as a bar
    /// chart, and brightest at that centre so the form has a visible spine.
    /// </summary>
    private static Rectangle NewBar(double width) => new()
    {
        Width = width,
        Height = AudioLevelHistory.MinimumBarHeight,
        RadiusX = width / 2,
        RadiusY = width / 2,
        VerticalAlignment = VerticalAlignment.Center,
        Fill = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0.5, 0),
            EndPoint = new Windows.Foundation.Point(0.5, 1),
            GradientStops =
            {
                new GradientStop { Offset = 0, Color = Windows.UI.Color.FromArgb(0x8A, 0x43, 0xE0, 0x8D) },
                new GradientStop { Offset = 0.5, Color = Windows.UI.Color.FromArgb(0xFF, 0x7C, 0xF5, 0xB4) },
                new GradientStop { Offset = 1, Color = Windows.UI.Color.FromArgb(0x8A, 0x43, 0xE0, 0x8D) },
            },
        },
    };

    private void RenderMeters()
    {
        if (!_hasAudio)
        {
            return;
        }
        for (var index = 0; index < _expandedBars.Count; index++)
        {
            Apply(_expandedBars[index], _levels[index], ExpandedMeterHeight);
        }
        var offset = AudioLevelHistory.Length - _collapsedBars.Count;
        for (var index = 0; index < _collapsedBars.Count; index++)
        {
            Apply(_collapsedBars[index], _levels[offset + index], CollapsedMeterHeight);
        }

        static void Apply(Rectangle bar, double level, double maximum)
        {
            bar.Height = AudioLevelHistory.BarHeight(level, maximum);
            // Quiet samples sit back rather than disappearing, so the trailing history
            // reads as a fading tail instead of a row of stubs.
            bar.Opacity = 0.4 + (0.6 * Math.Clamp(level, 0, 1));
        }
    }

    private void Root_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs eventArgs) => _target = 1;
    private void Root_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs eventArgs) { }
    // Keyboard users reach the actions without a pointer ever entering the pill.
    private void Root_GotFocus(object sender, RoutedEventArgs eventArgs) { _focusWithin = true; _target = 1; }
    private void Root_LostFocus(object sender, RoutedEventArgs eventArgs) => _focusWithin = false;

    private void Drawer_Tick(object? sender, object eventArgs)
    {
        var elapsed = _frameClock.Elapsed.TotalMilliseconds;
        _frameClock.Restart();

        // Open while the pointer is near, or while focus is inside the drawer so a
        // keyboard user does not have it close under them. Proximity is measured
        // against the on-screen strip, not the window, most of which is above the
        // top edge while closed.
        var (pointerX, pointerY) = WindowPlacement.PointerPosition();
        var visibleTop = _appliedTop + (_pixelHeight - (_openTop - _closedTop) - StripPixels());
        var visible = new CaptureBounds(_panelLeft, Math.Max(0, visibleTop), _panelLeft + _pixelWidth, _appliedTop + _pixelHeight);
        // Focus is tracked explicitly rather than queried: FocusManager reports the
        // last focused element even when the window is inactive, which would pin the
        // drawer open for the rest of the recording.
        _target = DrawerAnimation.IsPointerNear(visible, pointerX, pointerY) || _focusWithin ? 1 : 0;

        var next = DrawerAnimation.Advance(_progress, _target, elapsed);
        if (Math.Abs(next - _progress) < 0.0001)
        {
            return;
        }
        _progress = next;
        ApplyProgress();
    }

    /// <summary>
    /// Slides the window between closed and open. Only <c>SetWindowPos</c> runs per
    /// frame — no resize, and no window region, both of which drop the window off
    /// DWM's fast path and made the travel stutter.
    /// </summary>
    private void ApplyProgress()
    {
        var eased = DrawerAnimation.Ease(_progress);
        var top = DrawerAnimation.Lerp(_closedTop, _openTop, eased);
        if (top != _appliedTop)
        {
            _appliedTop = top;
            WindowPlacement.MoveTo(this, _panelLeft, top);
        }

        // The strip dissolves into the full panel as the drawer arrives, so the two
        // states read as one surface opening rather than a swap.
        CollapsedView.Opacity = Math.Clamp(1 - (eased * 1.6), 0, 1);
        ExpandedView.Opacity = Math.Clamp((eased - 0.25) / 0.75, 0, 1);
        ExpandedSlide.Y = (eased - 1) * 10;
        CollapsedView.Visibility = eased < 0.62 ? Visibility.Visible : Visibility.Collapsed;
        ExpandedView.Visibility = eased > 0.25 ? Visibility.Visible : Visibility.Collapsed;
        // Only accept clicks once the actions are actually readable.
        ExpandedView.IsHitTestVisible = eased > 0.6;
    }

    private int StripPixels() => WindowPlacement.ToPixels(this, StripHeight);

    private void ApplySize()
    {
        // Resolved once: the work area, scale, and the two rest positions. Recomputing
        // these per frame was part of what made the slide expensive.
        var work = WindowPlacement.MonitorUnderPointer(true);
        _pixelWidth = WindowPlacement.ToPixels(this, PanelWidth);
        _pixelHeight = WindowPlacement.ToPixels(this, PanelHeight);
        _panelLeft = work.Left + ((work.Right - work.Left - _pixelWidth) / 2);
        _openTop = work.Top;
        _closedTop = work.Top - (_pixelHeight - StripPixels());
        WindowPlacement.ResizeInDips(this, PanelWidth, PanelHeight);
        _appliedTop = int.MinValue;
        ApplyProgress();
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
