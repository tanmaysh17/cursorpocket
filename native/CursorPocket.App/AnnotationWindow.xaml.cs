using System.Collections.ObjectModel;
using CursorPocket.Core.Models;
using CursorPocket_App.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Point = Windows.Foundation.Point;

namespace CursorPocket_App;

public sealed partial class AnnotationWindow : Window
{
    private readonly CaptureRecord _record;
    private readonly string _path;
    private readonly List<AnnotationOperation> _operations = [];
    private string _tool = "pen";
    private Windows.UI.Color _color = Windows.UI.Color.FromArgb(255, 67, 224, 141);
    private AnnotationOperation? _active;
    private Point _start;
    private bool _finished;
    private readonly IDisposable _escapeLease;

    public AnnotationWindow(CaptureRecord record, string path)
    {
        _record = record;
        _path = path;
        InitializeComponent();
        var bounds = WindowPlacement.MonitorUnderPointer(true);
        AppWindow.MoveAndResize(new RectInt32(bounds.Left + 20, bounds.Top + 20, Math.Max(760, bounds.Right - bounds.Left - 40), Math.Max(560, bounds.Bottom - bounds.Top - 40)));
        using var source = new System.Drawing.Bitmap(path);
        Stage.Width = source.Width;
        Stage.Height = source.Height;
        DrawingSurface.Width = source.Width;
        DrawingSurface.Height = source.Height;
        ScreenshotImage.Source = new BitmapImage(new Uri(path));
        DrawingSurface.KeyDown += DrawingSurface_KeyDown;
        _escapeLease = App.Services.EscapeHotkey.Capture(() => DispatcherQueue.TryEnqueue(Cancel));
        Activated += (_, _) => DrawingSurface.Focus(FocusState.Programmatic);
        Closed += (_, _) =>
        {
            _escapeLease.Dispose();
            if (!_finished)
            {
                Cancelled?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    public event EventHandler? Saved;
    public event EventHandler? Cancelled;

    private void Tool_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not ToggleButton { Tag: string tool } selected)
        {
            return;
        }
        _tool = tool;
        foreach (var button in new[] { PenTool, HighlightTool, ArrowTool, RectangleTool, TextTool })
        {
            button.IsChecked = ReferenceEquals(button, selected);
        }
    }

    private void Color_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is FrameworkElement { Tag: string value })
        {
            _color = ParseColor(value);
        }
    }

    private void DrawingSurface_PointerPressed(object sender, PointerRoutedEventArgs eventArgs)
    {
        _start = eventArgs.GetCurrentPoint(DrawingSurface).Position;
        DrawingSurface.CapturePointer(eventArgs.Pointer);
        if (_tool is "pen" or "highlight")
        {
            var points = new ObservableCollection<Point> { _start };
            var polyline = new Polyline
            {
                Stroke = new SolidColorBrush(_tool == "highlight" ? WithAlpha(_color, 92) : _color),
                StrokeThickness = _tool == "highlight" ? 22 : 6,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            polyline.Points.Add(_start);
            DrawingSurface.Children.Add(polyline);
            _active = new AnnotationOperation(_tool, _color, _start, _start, points, null, polyline);
        }
        else if (_tool == "rectangle")
        {
            var rectangle = new Rectangle { Stroke = new SolidColorBrush(_color), StrokeThickness = 5, Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(12, _color.R, _color.G, _color.B)) };
            DrawingSurface.Children.Add(rectangle);
            _active = new AnnotationOperation(_tool, _color, _start, _start, null, null, rectangle);
        }
        else if (_tool == "arrow")
        {
            var line = new Line { X1 = _start.X, Y1 = _start.Y, X2 = _start.X, Y2 = _start.Y, Stroke = new SolidColorBrush(_color), StrokeThickness = 6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Triangle };
            DrawingSurface.Children.Add(line);
            _active = new AnnotationOperation(_tool, _color, _start, _start, null, null, line);
        }
        else if (_tool == "text")
        {
            BeginText(_start);
        }
    }

    private void DrawingSurface_PointerMoved(object sender, PointerRoutedEventArgs eventArgs)
    {
        if (_active is null)
        {
            return;
        }
        var current = eventArgs.GetCurrentPoint(DrawingSurface).Position;
        _active.End = current;
        switch (_active.Visual)
        {
            case Polyline polyline:
                polyline.Points.Add(current);
                _active.Points?.Add(current);
                break;
            case Rectangle rectangle:
                Canvas.SetLeft(rectangle, Math.Min(_start.X, current.X));
                Canvas.SetTop(rectangle, Math.Min(_start.Y, current.Y));
                rectangle.Width = Math.Abs(current.X - _start.X);
                rectangle.Height = Math.Abs(current.Y - _start.Y);
                break;
            case Line line:
                line.X2 = current.X;
                line.Y2 = current.Y;
                break;
        }
    }

    private void DrawingSurface_PointerReleased(object sender, PointerRoutedEventArgs eventArgs)
    {
        DrawingSurface.ReleasePointerCapture(eventArgs.Pointer);
        if (_active is not null)
        {
            _active.End = eventArgs.GetCurrentPoint(DrawingSurface).Position;
            _operations.Add(_active);
            _active = null;
        }
    }

    private void BeginText(Point point)
    {
        var editor = new TextBox { Width = 320, FontSize = 32, Foreground = new SolidColorBrush(_color), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(210, 8, 14, 12)), PlaceholderText = "Type, then press Enter" };
        Canvas.SetLeft(editor, point.X);
        Canvas.SetTop(editor, point.Y);
        DrawingSurface.Children.Add(editor);
        editor.Focus(FocusState.Programmatic);
        editor.KeyDown += (_, args) =>
        {
            if (args.Key != VirtualKey.Enter)
            {
                return;
            }
            args.Handled = true;
            var text = editor.Text.Trim();
            DrawingSurface.Children.Remove(editor);
            if (text.Length == 0)
            {
                return;
            }
            var label = new TextBlock { Text = text, FontSize = 32, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(_color) };
            Canvas.SetLeft(label, point.X);
            Canvas.SetTop(label, point.Y);
            DrawingSurface.Children.Add(label);
            _operations.Add(new AnnotationOperation("text", _color, point, point, null, text, label));
            DrawingSurface.Focus(FocusState.Programmatic);
        };
    }

    private async void Save_Click(object sender, RoutedEventArgs eventArgs) => await SaveAsync();

    private async Task SaveAsync()
    {
        if (_finished)
        {
            return;
        }
        var temporary = _path + ".annotated.png";
        await Task.Run(() => RenderAnnotations(temporary));
        File.Move(temporary, _path, true);
        _finished = true;
        Saved?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void RenderAnnotations(string destination)
    {
        using var sourceStream = File.OpenRead(_path);
        using var sourceImage = System.Drawing.Image.FromStream(sourceStream);
        using var bitmap = new System.Drawing.Bitmap(sourceImage);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        foreach (var operation in _operations)
        {
            var color = System.Drawing.Color.FromArgb(operation.Tool == "highlight" ? 92 : 255, operation.Color.R, operation.Color.G, operation.Color.B);
            using var pen = new System.Drawing.Pen(color, operation.Tool == "highlight" ? 22 : 6) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = operation.Tool == "arrow" ? System.Drawing.Drawing2D.LineCap.ArrowAnchor : System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
            if (operation.Tool is "pen" or "highlight" && operation.Points is { Count: > 1 })
            {
                graphics.DrawLines(pen, operation.Points.Select(value => new System.Drawing.PointF((float)value.X, (float)value.Y)).ToArray());
            }
            else if (operation.Tool == "rectangle")
            {
                graphics.DrawRectangle(pen, (float)Math.Min(operation.Start.X, operation.End.X), (float)Math.Min(operation.Start.Y, operation.End.Y), (float)Math.Abs(operation.End.X - operation.Start.X), (float)Math.Abs(operation.End.Y - operation.Start.Y));
            }
            else if (operation.Tool == "arrow")
            {
                graphics.DrawLine(pen, (float)operation.Start.X, (float)operation.Start.Y, (float)operation.End.X, (float)operation.End.Y);
            }
            else if (operation.Tool == "text" && operation.Text is not null)
            {
                using var brush = new System.Drawing.SolidBrush(color);
                using var font = new System.Drawing.Font("Segoe UI", 32, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
                graphics.DrawString(operation.Text, font, brush, (float)operation.Start.X, (float)operation.Start.Y);
            }
        }
        bitmap.Save(destination, System.Drawing.Imaging.ImageFormat.Png);
    }

    private void Undo_Click(object sender, RoutedEventArgs eventArgs)
    {
        if (_operations.Count == 0)
        {
            return;
        }
        var operation = _operations[^1];
        _operations.RemoveAt(_operations.Count - 1);
        DrawingSurface.Children.Remove(operation.Visual);
    }

    private void DrawingSurface_KeyDown(object sender, KeyRoutedEventArgs eventArgs)
    {
        var controlDown = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (controlDown && eventArgs.Key == VirtualKey.Z)
        {
            eventArgs.Handled = true;
            Undo_Click(this, new RoutedEventArgs());
        }
        else if (eventArgs.Key == VirtualKey.Enter)
        {
            eventArgs.Handled = true;
            _ = SaveAsync();
        }
        else if (eventArgs.Key == VirtualKey.Escape)
        {
            eventArgs.Handled = true;
            Cancel();
        }
    }

    private void SaveAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs eventArgs)
    {
        // The inline text tool commits its label on Enter, so leave the key to the
        // editor while it has focus. Everywhere else Enter saves the screenshot,
        // whether or not anything was drawn on it.
        if (FocusManager.GetFocusedElement(Content.XamlRoot) is TextBox)
        {
            return;
        }
        eventArgs.Handled = true;
        _ = SaveAsync();
    }

    private void UndoAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs eventArgs)
    {
        if (FocusManager.GetFocusedElement(Content.XamlRoot) is TextBox)
        {
            return;
        }
        eventArgs.Handled = true;
        Undo_Click(this, new RoutedEventArgs());
    }

    private void Cancel_Click(object sender, RoutedEventArgs eventArgs) => Cancel();
    private void Cancel() { _finished = true; Cancelled?.Invoke(this, EventArgs.Empty); Close(); }

    private static Windows.UI.Color ParseColor(string hex)
    {
        var value = hex.TrimStart('#');
        return Windows.UI.Color.FromArgb(255, Convert.ToByte(value[..2], 16), Convert.ToByte(value.Substring(2, 2), 16), Convert.ToByte(value.Substring(4, 2), 16));
    }
    private static Windows.UI.Color WithAlpha(Windows.UI.Color value, byte alpha) => Windows.UI.Color.FromArgb(alpha, value.R, value.G, value.B);

    private sealed class AnnotationOperation(string tool, Windows.UI.Color color, Point start, Point end, ObservableCollection<Point>? points, string? text, UIElement visual)
    {
        public string Tool { get; } = tool;
        public Windows.UI.Color Color { get; } = color;
        public Point Start { get; } = start;
        public Point End { get; set; } = end;
        public ObservableCollection<Point>? Points { get; } = points;
        public string? Text { get; } = text;
        public UIElement Visual { get; } = visual;
    }
}
