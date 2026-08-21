using System.Drawing;
using System.Drawing.Imaging;
using CursorPocket.Core.Models;
using CursorPocket.Core.Services;
using CursorPocket.Core.Storage;

namespace CursorPocket_App.Services;

public sealed class ScreenshotCaptureService(CaptureStore store) : ICaptureService
{
    public Task<CaptureRecord> CaptureDisplayAsync(CancellationToken cancellationToken = default)
    {
        NativeMethods.GetCursorPos(out var cursor);
        var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            throw new InvalidOperationException("Windows could not identify the display under the pointer.");
        }
        return CaptureBoundsAsync(new CaptureBounds(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom), cancellationToken);
    }

    public Task<CaptureRecord> CaptureAllDisplaysAsync(CancellationToken cancellationToken = default)
    {
        var left = NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen);
        var top = NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen);
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen);
        return CaptureBoundsAsync(new CaptureBounds(left, top, left + width, top + height), cancellationToken);
    }

    public Task<CaptureRecord> CaptureWindowAsync(long windowHandle, CancellationToken cancellationToken = default)
    {
        var hwnd = (nint)windowHandle;
        if (hwnd == 0 || NativeMethods.IsIconic(hwnd) || !NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            throw new InvalidOperationException("That window is no longer visible.");
        }
        return CaptureBoundsAsync(new CaptureBounds(rect.Left, rect.Top, rect.Right, rect.Bottom), cancellationToken);
    }

    public Task<CaptureRecord> CaptureRegionAsync(CaptureBounds bounds, CancellationToken cancellationToken = default) =>
        CaptureBoundsAsync(bounds, cancellationToken);

    private async Task<CaptureRecord> CaptureBoundsAsync(CaptureBounds bounds, CancellationToken cancellationToken)
    {
        if (bounds.Width < 2 || bounds.Height < 2)
        {
            throw new ArgumentException("Screenshot selection is empty.", nameof(bounds));
        }
        var reservation = store.Reserve(CaptureKind.Screenshot, ".png");
        // The screen grab and the PNG encode are both expensive at 4K. Doing them
        // inline froze the UI thread for the whole encode after every screenshot key.
        await Task.Run(
            () =>
            {
                using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
                bitmap.Save(reservation.AbsolutePath, ImageFormat.Png);
            },
            cancellationToken);
        return await store.RegisterExistingAsync(
            CaptureKind.Screenshot,
            reservation.AbsolutePath,
            $"Screenshot · {bounds.Width} × {bounds.Height}",
            new Dictionary<string, object?>
            {
                ["bounds"] = new[] { bounds.Left, bounds.Top, bounds.Right, bounds.Bottom },
                ["width"] = bounds.Width,
                ["height"] = bounds.Height,
            },
            cancellationToken);
    }
}
