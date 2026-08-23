using CursorPocket.Core.Models;
using CursorPocket.Core.Services;
using CursorPocket_App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Point = Windows.Foundation.Point;

namespace CursorPocket_App;

/// <summary>
/// A screenshot the user chose to leave on screen while they carry on working.
/// </summary>
/// <remarks>
/// <para>
/// A pin is a receipt the user decided to keep. That framing is what separates it from the
/// unexplained floating widget the product's anti-references warn against: it is the
/// content itself, it only ever appears by explicit action, and it is never restored after
/// a restart — the Library holds the durable copy.
/// </para>
/// <para>
/// Deliberately <b>not</b> excluded from screen capture. A pin exists to be visible, so it
/// must appear in a screenshot or a recording taken while it is up. Visible equals
/// captured is the honest rule; a user who does not want it in the shot closes it.
/// </para>
/// </remarks>
public sealed partial class PinnedCaptureWindow : Window
{
    private readonly CaptureRecord _record;
    private readonly string _path;
    private readonly int _imageWidth;
    private readonly int _imageHeight;

    private double _scale = 1;
    private bool _dragging;
    private Point _grabOffset;

    private PinnedCaptureWindow(CaptureRecord record, string path, int imageWidth, int imageHeight, int index)
    {
        _record = record;
        _path = path;
        _imageWidth = imageWidth;
        _imageHeight = imageHeight;
        InitializeComponent();
        App.Theme.Register(this, Root, SurfaceRole.Pin);

        // Topmost so it stays a reference, but not capture-excluded: see the class remarks.
        WindowPlacement.ConfigureUtilityWindow(this, excludeFromCapture: false);

        PinImage.Source = new BitmapImage(new Uri(path));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
            Root,
            $"Pinned screenshot, {record.CreatedAt:HH:mm}");

        var work = WindowPlacement.MonitorUnderPointer(true);
        var area = new CaptureBounds(work.Left, work.Top, work.Right, work.Bottom);
        var (width, height) = PinnedCapturePlacement.Size(area, _imageWidth, _imageHeight, _scale);
        var bounds = PinnedCapturePlacement.Place(area, width, height, index);
        AppWindow.MoveAndResize(new RectInt32(bounds.Left, bounds.Top, bounds.Width, bounds.Height));
    }

    public event EventHandler<CaptureRecord>? EditRequested;

    /// <summary>Opens a pin for a saved capture. Never called automatically.</summary>
    public static PinnedCaptureWindow? TryShow(CaptureRecord record, string path, int index)
    {
        try
        {
            using var probe = new System.Drawing.Bitmap(path);
            var pin = new PinnedCaptureWindow(record, path, probe.Width, probe.Height, index);
            pin.AppWindow.Show(false);
            return pin;
        }
        catch (Exception)
        {
            // A pin is a convenience. If the image cannot be read there is nothing worth
            // interrupting the user for — the capture itself is already saved.
            return null;
        }
    }

    // ------------------------------------------------------------------ hover controls

    private void Root_PointerEntered(object sender, PointerRoutedEventArgs eventArgs) =>
        ControlStrip.Opacity = 1;

    private void Root_PointerExited(object sender, PointerRoutedEventArgs eventArgs)
    {
        if (!_dragging)
        {
            ControlStrip.Opacity = 0;
        }
    }

    // ------------------------------------------------------------------------ dragging

    private void Root_PointerPressed(object sender, PointerRoutedEventArgs eventArgs)
    {
        var properties = eventArgs.GetCurrentPoint(Root).Properties;
        if (properties.IsMiddleButtonPressed)
        {
            Close();
            return;
        }

        if (!properties.IsLeftButtonPressed)
        {
            return;
        }

        // Tracked from pointer events rather than handed to Windows' modal move loop: WinUI
        // consumes the messages that loop needs, which is why WindowPlacement has no such
        // helper and why command mode and the camera self-view both track it this way.
        _dragging = true;
        _grabOffset = eventArgs.GetCurrentPoint(Root).Position;
        Root.CapturePointer(eventArgs.Pointer);
    }

    private void Root_PointerMoved(object sender, PointerRoutedEventArgs eventArgs)
    {
        if (!_dragging)
        {
            return;
        }

        var (pointerX, pointerY) = WindowPlacement.PointerPosition();
        var scale = WindowPlacement.ScaleFor(this);
        WindowPlacement.MoveTo(
            this,
            pointerX - (int)Math.Round(_grabOffset.X * scale),
            pointerY - (int)Math.Round(_grabOffset.Y * scale));
    }

    private void Root_PointerReleased(object sender, PointerRoutedEventArgs eventArgs) => EndDrag(eventArgs);

    private void Root_PointerCaptureLost(object sender, PointerRoutedEventArgs eventArgs) => EndDrag(eventArgs);

    private void EndDrag(PointerRoutedEventArgs eventArgs)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        Root.ReleasePointerCapture(eventArgs.Pointer);
    }

    /// <summary>
    /// The wheel resizes in discrete notches, never per frame: a continuous resize would
    /// recompute the window's geometry on every wheel event while the pointer is over it.
    /// </summary>
    private void Root_PointerWheelChanged(object sender, PointerRoutedEventArgs eventArgs)
    {
        var delta = eventArgs.GetCurrentPoint(Root).Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        eventArgs.Handled = true;
        _scale = PinnedCapturePlacement.StepScale(_scale, Math.Sign(delta));

        var work = WindowPlacement.MonitorUnderPointer(true);
        var area = new CaptureBounds(work.Left, work.Top, work.Right, work.Bottom);
        var (width, height) = PinnedCapturePlacement.Size(area, _imageWidth, _imageHeight, _scale);
        AppWindow.Resize(new SizeInt32(width, height));
    }

    // ------------------------------------------------------------------- drag out as file

    /// <summary>
    /// Hands the PNG to whatever the user drops it on. All three payloads, because
    /// different targets want different things: Explorer takes the storage item, an image
    /// editor takes the bitmap, and the drag preview is what makes the gesture legible.
    /// </summary>
    private async void DragHandle_DragStarting(UIElement sender, DragStartingEventArgs eventArgs)
    {
        var deferral = eventArgs.GetDeferral();
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(_path);
            eventArgs.Data.RequestedOperation = DataPackageOperation.Copy;
            eventArgs.Data.SetStorageItems([file]);
            eventArgs.Data.SetBitmap(Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(file));

            var preview = new BitmapImage { DecodePixelWidth = 256 };
            preview.UriSource = new Uri(_path);
            eventArgs.DragUI.SetContentFromBitmapImage(preview);
        }
        catch (Exception)
        {
            eventArgs.Cancel = true;
        }
        finally
        {
            deferral.Complete();
        }
    }

    // -------------------------------------------------------------------------- actions

    private async void Copy_Click(object sender, RoutedEventArgs eventArgs) => await CopyImageAsync();

    private async Task CopyImageAsync()
    {
        try
        {
            var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(_path);
            var package = new DataPackage();
            package.SetBitmap(Windows.Storage.Streams.RandomAccessStreamReference.CreateFromFile(file));
            Clipboard.SetContent(package);
            // Flushed so the image outlives the pin: closing the pin must not take the
            // screenshot back off the clipboard.
            Clipboard.Flush();
        }
        catch (Exception)
        {
            // Another app can hold the clipboard open. The file is still on disk.
        }
    }

    private void CopyPath_Click(object sender, RoutedEventArgs eventArgs)
    {
        var package = new DataPackage();
        package.SetText(_path);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    private void Edit_Click(object sender, RoutedEventArgs eventArgs)
    {
        EditRequested?.Invoke(this, _record);
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs eventArgs) => Close();

    private void CloseAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        Close();
    }

    private void CopyAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        _ = CopyImageAsync();
    }
}
