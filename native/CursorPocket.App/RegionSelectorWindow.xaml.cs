using CursorPocket.Core.Models;
using CursorPocket.Core.Services;
using CursorPocket_App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using Point = Windows.Foundation.Point;

namespace CursorPocket_App;

/// <summary>
/// What the user selected, plus the frozen desktop frame it was selected from. Whoever
/// handles the selection owns <see cref="Snapshot"/> and must dispose it.
/// </summary>
public sealed record SelectedRegion(
    CaptureBounds Bounds,
    System.Drawing.Bitmap? Snapshot,
    int SnapshotLeft,
    int SnapshotTop);

public sealed partial class RegionSelectorWindow : Window
{
    private IDisposable? _escapeLease;
    private System.Drawing.Bitmap? _snapshot;
    private Point _start;
    private (int X, int Y) _startPhysical;
    private bool _dragging;
    private int _virtualLeft;
    private int _virtualTop;

    /// <summary>
    /// Builds the window without showing it. Construction is the expensive, state-free
    /// part (XAML, theme, window styles), so MainWindow keeps one built ahead of time and
    /// <see cref="Open"/> does only what has to happen at the moment of capture. Each
    /// instance is still used once and closed: nothing from one selection can leak into
    /// the next.
    /// </summary>
    public RegionSelectorWindow()
    {
        InitializeComponent();
        App.Theme.Register(this, Root, SurfaceRole.CaptureOverlay);
        WindowPlacement.ConfigureUtilityWindow(this, excludeFromCapture: false);
        Activated += (_, _) => Surface.Focus(FocusState.Programmatic);
        Closed += (_, _) =>
        {
            _escapeLease?.Dispose();
            _snapshot?.Dispose();
            _snapshot = null;
        };
    }

    public event EventHandler<SelectedRegion>? RegionSelected;

    /// <summary>Freezes the desktop under the selector and shows it.</summary>
    public void Open()
    {
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
        }, out var frame);
        _snapshot = frame;
        AppWindow.MoveAndResize(new RectInt32(_virtualLeft, _virtualTop, width, height));
        _escapeLease = App.Services.EscapeHotkey.Capture(() => DispatcherQueue.TryEnqueue(Cancel));
        Activate();
    }

    private void Surface_PointerPressed(object sender, PointerRoutedEventArgs eventArgs)
    {
        _dragging = true;
        _start = eventArgs.GetCurrentPoint(Surface).Position;
        // The capture itself is in physical pixels, so the corners are recorded from
        // the cursor rather than from XAML's device-independent coordinates. That also
        // stays correct across monitors with different scale factors, which a single
        // window's scale cannot describe.
        _startPhysical = WindowPlacement.PointerPosition();
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
        var (endX, endY) = WindowPlacement.PointerPosition();
        var bounds = RegionSelection.FromCorners(_startPhysical.X, _startPhysical.Y, endX, endY);
        if (!RegionSelection.IsUsable(bounds))
        {
            Close();
            return;
        }

        // The frame leaves with the selection, so Closed must not dispose it.
        var snapshot = _snapshot;
        _snapshot = null;
        Close();
        RegionSelected?.Invoke(this, new SelectedRegion(bounds, snapshot, _virtualLeft, _virtualTop));
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
        // Report the pixels that will actually be saved, not the scaled-down
        // device-independent size the rubber band is drawn in.
        var (pointerX, pointerY) = WindowPlacement.PointerPosition();
        var physical = RegionSelection.FromCorners(_startPhysical.X, _startPhysical.Y, pointerX, pointerY);
        SizeText.Text = $"{physical.Width} × {physical.Height}";
        Canvas.SetLeft(SizeBadge, left);
        Canvas.SetTop(SizeBadge, Math.Max(4, top - 30));
    }

    private void Surface_KeyDown(object sender, KeyRoutedEventArgs eventArgs)
    {
        if (eventArgs.Key == VirtualKey.Escape)
        {
            eventArgs.Handled = true;
            Cancel();
        }
    }

    private void Cancel() => Close();
}
