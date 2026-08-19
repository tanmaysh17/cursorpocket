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
    private bool _closed;

    private CameraSelfViewWindow()
    {
        InitializeComponent();
        WindowPlacement.ConfigureUtilityWindow(this, topmost: true, excludeFromCapture: false);
        WindowPlacement.MakeClickThrough(this);
        Closed += (_, _) => ReleaseCamera();
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
        var placement = CameraSelfViewPlacement.Compute(bounds, options.CameraPosition, options.CameraWidth);
        var window = new CameraSelfViewWindow();
        window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(placement.Left, placement.Top, placement.Width, placement.Height));
        WindowPlacement.ClipToRoundedPixelRegion(window, placement.Width, placement.Height, 12);
        window.AppWindow.Show(false);
        if (!await window.StartCameraAsync(options.CameraName))
        {
            window.Close();
            return null;
        }
        // Never take focus from whatever the user is about to demonstrate.
        App.Services.Context.RestoreFocus(sourceWindow);
        return window;
    }

    public void Dismiss()
    {
        if (_closed)
        {
            return;
        }
        _closed = true;
        ReleaseCamera();
        Close();
    }

    private static CaptureBounds ResolveCaptureArea(RecordingOptions options, long sourceWindow)
    {
        switch (options.SourceKind)
        {
            case VideoSourceKind.Region when options.Bounds is not null:
                return options.Bounds;
            case VideoSourceKind.Window:
                var handle = options.WindowHandle is null or 0 ? sourceWindow : options.WindowHandle.Value;
                if (handle != 0 && NativeMethods.GetWindowRect((nint)handle, out var windowRect))
                {
                    return new CaptureBounds(windowRect.Left, windowRect.Top, windowRect.Right, windowRect.Bottom);
                }
                break;
        }
        var monitor = options.SourceKind == VideoSourceKind.Display
            ? WindowPlacement.MonitorBoundsAt(Math.Max(0, options.DisplayIndex))
            : WindowPlacement.MonitorUnderPointer();
        return new CaptureBounds(monitor.Left, monitor.Top, monitor.Right, monitor.Bottom);
    }

    private async Task<bool> StartCameraAsync(string cameraName)
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
            _cameraPlayer = new MediaPlayer { AutoPlay = true, IsLoopingEnabled = true };
            _cameraPlayer.Source = MediaSource.CreateFromMediaFrameSource(source);
            CameraPreview.SetMediaPlayer(_cameraPlayer);
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

    private void ReleaseCamera()
    {
        CameraPreview.SetMediaPlayer(null);
        _cameraPlayer?.Dispose();
        _cameraPlayer = null;
        _mediaCapture?.Dispose();
        _mediaCapture = null;
    }
}
