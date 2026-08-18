using CursorPocket.Core.Models;
using CursorPocket_App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Point = Windows.Foundation.Point;

namespace CursorPocket_App;

public sealed partial class RegionSelectorWindow : Window
{
    private Point _start;
    private bool _dragging;
    private int _virtualLeft;
    private int _virtualTop;

    public RegionSelectorWindow()
    {
        InitializeComponent();
        WindowPlacement.ConfigureUtilityWindow(this);
        _virtualLeft = NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen);
        _virtualTop = NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen);
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen);
        BackdropImage.Source = DesktopSnapshot.Capture(new NativeMethods.Rect
        {
            Left = _virtualLeft,
            Top = _virtualTop,
            Right = _virtualLeft + width,
            Bottom = _virtualTop + height,
        });
        AppWindow.MoveAndResize(new RectInt32(_virtualLeft, _virtualTop, width, height));
        Activated += (_, _) => Surface.Focus(FocusState.Programmatic);
    }

    public event EventHandler<CaptureBounds>? RegionSelected;

    private void Surface_PointerPressed(object sender, PointerRoutedEventArgs eventArgs)
    {
        _dragging = true;
        _start = eventArgs.GetCurrentPoint(Surface).Position;
        Surface.CapturePointer(eventArgs.Pointer);
        Selection.Visibility = Visibility.Visible;
        SizeBadge.Visibility = Visibility.Visible;
        UpdateSelection(_start);
    }

    private void Surface_PointerMoved(object sender, PointerRoutedEventArgs eventArgs)
    {
        if (_dragging)
        {
            UpdateSelection(eventArgs.GetCurrentPoint(Surface).Position);
        }
    }

    private void Surface_PointerReleased(object sender, PointerRoutedEventArgs eventArgs)
    {
        if (!_dragging)
        {
            return;
        }
        _dragging = false;
        Surface.ReleasePointerCapture(eventArgs.Pointer);
        var end = eventArgs.GetCurrentPoint(Surface).Position;
        var left = (int)Math.Round(Math.Min(_start.X, end.X));
        var top = (int)Math.Round(Math.Min(_start.Y, end.Y));
        var right = (int)Math.Round(Math.Max(_start.X, end.X));
        var bottom = (int)Math.Round(Math.Max(_start.Y, end.Y));
        if (right - left < 4 || bottom - top < 4)
        {
            Close();
            return;
        }
        var bounds = new CaptureBounds(left + _virtualLeft, top + _virtualTop, right + _virtualLeft, bottom + _virtualTop);
        RegionSelected?.Invoke(this, bounds);
        Close();
    }

    private void UpdateSelection(Point point)
    {
        var left = Math.Min(_start.X, point.X);
        var top = Math.Min(_start.Y, point.Y);
        var width = Math.Abs(point.X - _start.X);
        var height = Math.Abs(point.Y - _start.Y);
        Canvas.SetLeft(Selection, left);
        Canvas.SetTop(Selection, top);
        Selection.Width = width;
        Selection.Height = height;
        SizeText.Text = $"{Math.Round(width)} × {Math.Round(height)}";
        Canvas.SetLeft(SizeBadge, left);
        Canvas.SetTop(SizeBadge, Math.Max(4, top - 30));
    }

    private void Surface_KeyDown(object sender, KeyRoutedEventArgs eventArgs)
    {
        if (eventArgs.Key == VirtualKey.Escape)
        {
            eventArgs.Handled = true;
            Close();
        }
    }
}
