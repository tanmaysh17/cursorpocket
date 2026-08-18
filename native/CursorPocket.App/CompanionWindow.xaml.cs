using CursorPocket.Core.Models;
using CursorPocket_App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace CursorPocket_App;

public sealed partial class CompanionWindow : Window
{
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private string _mode;
    private bool _recording;

    public CompanionWindow(string mode)
    {
        InitializeComponent();
        _mode = mode;
        WindowPlacement.ConfigureUtilityWindow(this);
        AppWindow.Resize(new SizeInt32(32, 32));
        _idleTimer.Tick += (_, _) =>
        {
            if (_mode == "while-moving" && !_recording)
            {
                AppWindow.Hide();
            }
            _idleTimer.Stop();
        };
    }

    public event EventHandler? OpenRequested;

    public void SetMode(string mode)
    {
        _mode = mode;
        if (mode == "off")
        {
            AppWindow.Hide();
        }
    }

    public void SetRecording(bool recording)
    {
        _recording = recording;
        var color = recording ? Color.FromArgb(255, 255, 90, 103) : Color.FromArgb(255, 67, 224, 141);
        Dot.Fill = new SolidColorBrush(color);
        Glow.Fill = new SolidColorBrush(Color.FromArgb(90, color.R, color.G, color.B));
        if (recording)
        {
            AppWindow.Show();
        }
    }

    public void Follow(int x, int y)
    {
        if (_mode == "off")
        {
            return;
        }
        AppWindow.Move(new PointInt32(x + 7, y + 7));
        AppWindow.Show();
        if (_mode == "while-moving" && !_recording)
        {
            _idleTimer.Stop();
            _idleTimer.Start();
        }
    }

    private void CompanionButton_Click(object sender, RoutedEventArgs eventArgs) => OpenRequested?.Invoke(this, EventArgs.Empty);
}
