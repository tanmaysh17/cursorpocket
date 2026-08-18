using CursorPocket.Core.Models;
using CursorPocket_App.Services;
using Microsoft.UI.Xaml;

namespace CursorPocket_App;

public sealed partial class RecordingHudWindow : Window
{
    private readonly Func<bool, Task> _stop;
    private bool _stopping;

    private RecordingHudWindow(string mode, string device, Func<bool, Task> stop)
    {
        _stop = stop;
        InitializeComponent();
        ModeText.Text = mode;
        DeviceText.Text = device;
        WindowPlacement.ConfigureUtilityWindow(this);
        WindowPlacement.PlaceTopCenter(this, 680, 108);
        App.Services.Recording.ElapsedChanged += Recording_ElapsedChanged;
        App.Services.Recording.AudioLevelChanged += Recording_AudioLevelChanged;
        App.Services.Recording.StateChanged += Recording_StateChanged;
        Closed += (_, _) => Unsubscribe();
    }

    public static void ShowForAudio(string device, Func<bool, Task> stop)
    {
        var window = new RecordingHudWindow("Audio note", device, stop);
        window.AppWindow.Show(false);
    }

    public static void ShowForVideo(RecordingOptions options, Func<bool, Task> stop)
    {
        var detail = options.IncludeCamera
            ? $"Screen · mic {(options.IncludeMicrophone ? "on" : "off")} · camera on"
            : $"Screen · mic {(options.IncludeMicrophone ? "on" : "off")} · camera off";
        var window = new RecordingHudWindow("Screen recording", detail, stop);
        window.AppWindow.Show(false);
    }

    private void Recording_ElapsedChanged(object? sender, TimeSpan elapsed) => DispatcherQueue.TryEnqueue(() => TimerText.Text = $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}");
    private void Recording_AudioLevelChanged(object? sender, double level) => DispatcherQueue.TryEnqueue(() => LevelBar.Value = level);

    private void Recording_StateChanged(object? sender, RecordingState state) => DispatcherQueue.TryEnqueue(() =>
    {
        if (state == RecordingState.Finalizing)
        {
            ModeText.Text = "Finalizing…";
        }
        else if (state is RecordingState.Idle or RecordingState.Failed)
        {
            Close();
        }
    });

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
