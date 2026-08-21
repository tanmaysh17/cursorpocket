using CursorPocket.Core.Models;
using CursorPocket.Core.Services;
using CursorPocket_App.Services;
using Microsoft.UI.Xaml;
using Windows.Devices.Enumeration;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace CursorPocket_App;

/// <summary>
/// The live camera picture-in-picture the user sees while recording.
/// <para>
/// This is the one CursorPocket surface that is intentionally <b>not</b> excluded
/// from screen capture: the webcam reaches the recorded file by being on screen
/// inside the captured area, which is also what lets the user watch their own
/// camera feed. CursorPocket holds the camera for the whole recording, so FFmpeg
/// must not be given a <c>dshow</c> camera input at the same time.
/// </para>
/// </summary>
public sealed partial class CameraSelfViewWindow : Window
{
    private MediaCapture? _mediaCapture;
    private MediaPlayer? _cameraPlayer;
    private CameraEffectRenderer? _effectRenderer;
    private CaptureBounds _captureArea = new(0, 0, 1920, 1080);
    private (int Left, int Top) _dragOrigin;
    private (int X, int Y) _dragStart;
    private bool _dragging;
    /// <summary>Remembered so the shape can be re-cut after a drag; see <see cref="ApplyShapeClip"/>.</summary>
    private string _cameraShape = "rounded";
    private int _clipWidth;
    private int _clipHeight;
    private bool _closed;

    private CameraSelfViewWindow()
    {
        InitializeComponent();
        // Not click-through: the user drags this to reposition their camera mid
        // recording, which needs pointer input. It stays small so the area it takes
        // out of the demonstration is minimal, and it never takes activation.
        WindowPlacement.ConfigureUtilityWindow(this, topmost: true, excludeFromCapture: false);
        Closed += (_, _) => ReleaseCamera();
    }

    /// <summary>
    /// Drag the self-view anywhere inside the area being recorded. The clamp is not
    /// cosmetic: the webcam reaches the file by being on screen inside that rectangle,
    /// so a self-view dragged outside it would silently vanish from the recording.
    /// </summary>
    private void Root_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs eventArgs)
    {
        var bounds = WindowPlacement.BoundsOf(this);
        _dragOrigin = (bounds.Left, bounds.Top);
        _dragStart = WindowPlacement.PointerPosition();
        _dragging = Root.CapturePointer(eventArgs.Pointer);
        eventArgs.Handled = _dragging;
        if (_dragging)
        {
            // A window region takes the window off DWM's fast path, so dragging a
            // clipped surface visibly lags. Drop the clip for the duration of the
            // drag and cut it again on release: the shape is what appears in the
            // recording at rest, and the square corners only show while the user is
            // actively holding the window.
            WindowPlacement.ClearWindowRegion(this);
        }
    }

    private void Root_PointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs eventArgs)
    {
        if (!_dragging)
        {
            return;
        }
        eventArgs.Handled = true;
        var (pointerX, pointerY) = WindowPlacement.PointerPosition();
        var bounds = WindowPlacement.BoundsOf(this);
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        var left = Math.Clamp(
            _dragOrigin.Left + (pointerX - _dragStart.X),
            _captureArea.Left,
            Math.Max(_captureArea.Left, _captureArea.Right - width));
        var top = Math.Clamp(
            _dragOrigin.Top + (pointerY - _dragStart.Y),
            _captureArea.Top,
            Math.Max(_captureArea.Top, _captureArea.Bottom - height));
        WindowPlacement.MoveTo(this, left, top);
    }

    private void Root_PointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs eventArgs)
    {
        if (!_dragging)
        {
            return;
        }
        _dragging = false;
        eventArgs.Handled = true;
        Root.ReleasePointerCapture(eventArgs.Pointer);
        ApplyShapeClip();
    }

    private void Root_PointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs eventArgs)
    {
        if (!_dragging)
        {
            return;
        }
        // Capture can be lost without a release — the clip still has to come back.
        _dragging = false;
        ApplyShapeClip();
    }

    /// <summary>
    /// Cuts the self-view to its configured shape. The squircle is a superellipse
    /// polygon; everything else is the rounded rectangle. Safe to call repeatedly,
    /// which is what lets the clip be dropped for a drag and restored after.
    /// </summary>
    private void ApplyShapeClip()
    {
        if (_clipWidth < 2 || _clipHeight < 2)
        {
            return;
        }
        if (_cameraShape == "squircle")
        {
            WindowPlacement.ClipToPolygonPixelRegion(this, SquircleGeometry.ComputePolygon(_clipWidth, _clipHeight));
        }
        else
        {
            WindowPlacement.ClipToRoundedPixelRegion(this, _clipWidth, _clipHeight, 12);
        }
    }

    /// <summary>
    /// Shows the self-view inside the area being recorded, or returns null when the
    /// camera cannot be opened. A failed self-view never blocks the recording: the
    /// screen still records, just without a webcam inset.
    /// </summary>
    public static async Task<CameraSelfViewWindow?> ShowForAsync(RecordingOptions options, long sourceWindow)
    {
        if (!options.IncludeCamera || string.IsNullOrWhiteSpace(options.CameraName))
        {
            return null;
        }
        var bounds = ResolveCaptureArea(options, sourceWindow);
        var placement = CameraSelfViewPlacement.Compute(bounds, options.CameraPosition, options.CameraWidth, options.CameraShape);
        var window = new CameraSelfViewWindow();
        window._captureArea = bounds;
        window._cameraShape = options.CameraShape;
        window._clipWidth = placement.Width;
        window._clipHeight = placement.Height;
        window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(placement.Left, placement.Top, placement.Width, placement.Height));
        window.ApplyShapeClip();
        window.AppWindow.Show(false);
        if (!await window.StartCameraAsync(options.CameraName, options.ToCameraEffectSettings()))
        {
            window.Close();
            return null;
        }
        // Never take focus from whatever the user is about to demonstrate.
        App.Services.Context.RestoreFocus(sourceWindow);
        return window;
    }

    public void Dismiss() => _ = DismissAsync();

    /// <summary>
    /// Releases the camera and closes. Awaiting this matters: the next preflight
    /// preview opens the same device, and DirectShow allows a single consumer.
    /// </summary>
    public async Task DismissAsync()
    {
        if (_closed)
        {
            return;
        }
        _closed = true;
        var renderer = _effectRenderer;
        _effectRenderer = null;
        // Taken before the await so the device handle cannot be stranded: whatever
        // happens to the continuation, this local is disposed below. The camera must
        // be free the moment the recording ends, not whenever the process exits.
        var capture = _mediaCapture;
        _mediaCapture = null;
        if (renderer is not null)
        {
            await renderer.DisposeAsync().ConfigureAwait(false);
        }
        try
        {
            capture?.Dispose();
        }
        catch (Exception)
        {
            // Racing an in-flight reader stop; the handle is going away regardless.
        }
        // Everything below touches XAML, so it has to be back on the UI thread —
        // ConfigureAwait(false) above means we may not be.
        if (DispatcherQueue is null || DispatcherQueue.HasThreadAccess)
        {
            FinishClose();
        }
        else
        {
            DispatcherQueue.TryEnqueue(FinishClose);
        }
    }

    private void FinishClose()
    {
        try
        {
            _effectRenderer?.Dispose();
            _effectRenderer = null;
            CameraPreview.SetMediaPlayer(null);
            _cameraPlayer?.Dispose();
            _cameraPlayer = null;
            Close();
        }
        catch (Exception)
        {
            // The window is going away; the camera is already released above.
        }
    }

    private static CaptureBounds ResolveCaptureArea(RecordingOptions options, long sourceWindow)
    {
        switch (options.SourceKind)
        {
            // Bounds is the recorded rectangle for both a region and a display, so it
            // is also the area the self-view has to stay inside.
            case VideoSourceKind.Region or VideoSourceKind.Display when options.Bounds is not null:
                return options.Bounds;
            case VideoSourceKind.Window:
                var handle = options.WindowHandle is null or 0 ? sourceWindow : options.WindowHandle.Value;
                if (handle != 0 && NativeMethods.GetWindowRect((nint)handle, out var windowRect))
                {
                    return new CaptureBounds(windowRect.Left, windowRect.Top, windowRect.Right, windowRect.Bottom);
                }
                break;
        }
        var monitor = WindowPlacement.MonitorUnderPointer();
        return new CaptureBounds(monitor.Left, monitor.Top, monitor.Right, monitor.Bottom);
    }

    private async Task<bool> StartCameraAsync(string cameraName, CursorPocket.Core.Media.CameraEffectSettings effects)
    {
        try
        {
            var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            var device = devices.FirstOrDefault(item => string.Equals(item.Name, cameraName, StringComparison.OrdinalIgnoreCase))
                ?? devices.FirstOrDefault();
            if (device is null)
            {
                return false;
            }
            _mediaCapture = new MediaCapture();
            await _mediaCapture.InitializeAsync(new MediaCaptureInitializationSettings
            {
                VideoDeviceId = device.Id,
                StreamingCaptureMode = StreamingCaptureMode.Video,
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
            });
            var source = _mediaCapture.FrameSources.Values.FirstOrDefault(frame => frame.Info.SourceKind == MediaFrameSourceKind.Color);
            if (source is null)
            {
                ReleaseCamera();
                return false;
            }
            if (effects.HasAnyEffect)
            {
                _effectRenderer = await CameraEffectRenderer.StartAsync(
                    _mediaCapture,
                    source,
                    effects,
                    CameraEffectView,
                    DispatcherQueue,
                    // The shape decides the aspect, so the crop follows the person
                    // instead of slicing the sides off a 4:3 camera down the middle.
                    _clipHeight > 0 ? _clipWidth / (double)_clipHeight : 0);
            }
            if (_effectRenderer is not null)
            {
                CameraEffectView.Visibility = Visibility.Visible;
                // Exactly one path is ever on screen. An idle MediaPlayerElement
                // still paints, so leaving it visible puts a black rectangle behind
                // the effects image for no reason.
                CameraPreview.Visibility = Visibility.Collapsed;
            }
            else
            {
                // No effects requested, or the frame reader could not start:
                // the plain preview is exactly the pre-effects pipeline.
                _cameraPlayer = new MediaPlayer { AutoPlay = true, IsLoopingEnabled = true };
                _cameraPlayer.Source = MediaSource.CreateFromMediaFrameSource(source);
                CameraPreview.SetMediaPlayer(_cameraPlayer);
            }
            CameraStatus.Visibility = Visibility.Collapsed;
            return true;
        }
        catch (Exception)
        {
            // Windows privacy settings, an unplugged camera, or another app holding
            // the device all land here. The recording continues without a self-view.
            ReleaseCamera();
            return false;
        }
    }

    /// <summary>
    /// Frees the camera. The MediaCapture is disposed synchronously and last, on
    /// purpose: the renderer teardown it follows is asynchronous, and this runs from
    /// <c>Closed</c> where a continuation may never be drained. Disposing the
    /// capture here is what actually turns the camera light off.
    /// </summary>
    private void ReleaseCamera()
    {
        try
        {
            _effectRenderer?.Dispose();
            _effectRenderer = null;
            CameraPreview.SetMediaPlayer(null);
            _cameraPlayer?.Dispose();
            _cameraPlayer = null;
        }
        catch (Exception)
        {
            // Whatever else fails, the device below still has to be released.
        }
        try
        {
            _mediaCapture?.Dispose();
        }
        catch (Exception)
        {
            // Racing an in-flight reader stop; the handle is going away regardless.
        }
        _mediaCapture = null;
    }
}
