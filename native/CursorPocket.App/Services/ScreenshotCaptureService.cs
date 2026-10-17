using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CursorPocket.Core.Models;
using CursorPocket.Core.Services;
using CursorPocket.Core.Storage;

namespace CursorPocket_App.Services;

/// <summary>
/// A screenshot whose pixels are in memory and whose file is still being written.
/// </summary>
/// <param name="Pixels">The editor's own copy. The editor owns it and disposes it.</param>
/// <param name="DisplayPixels">Packed BGRA rows, ready for a WriteableBitmap.</param>
/// <param name="Path">Where the PNG will be once <paramref name="Committed"/> completes.</param>
/// <param name="Committed">Completes once the PNG is on disk and in the index.</param>
public sealed record ScreenshotFrame(
    Bitmap Pixels,
    byte[] DisplayPixels,
    string Path,
    Task<CaptureRecord> Committed);

public sealed class ScreenshotCaptureService(CaptureStore store) : ICaptureService
{
    private readonly CaptureTransaction _transaction = new(store);

    public Task<CaptureRecord> CaptureDisplayAsync(CancellationToken cancellationToken = default) =>
        CommittedOnlyAsync(BeginDisplayAsync(cancellationToken));

    public Task<CaptureRecord> CaptureAllDisplaysAsync(CancellationToken cancellationToken = default) =>
        CommittedOnlyAsync(BeginAllDisplaysAsync(cancellationToken));

    public Task<CaptureRecord> CaptureWindowAsync(long windowHandle, CancellationToken cancellationToken = default) =>
        CommittedOnlyAsync(BeginWindowAsync(windowHandle, cancellationToken));

    public Task<CaptureRecord> CaptureRegionAsync(CaptureBounds bounds, CancellationToken cancellationToken = default) =>
        CommittedOnlyAsync(BeginRegionAsync(bounds, cancellationToken));

    public Task<ScreenshotFrame> BeginDisplayAsync(CancellationToken cancellationToken = default)
    {
        NativeMethods.GetCursorPos(out var cursor);
        var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>() };
        if (!NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            throw new InvalidOperationException("Windows could not identify the display under the pointer.");
        }
        return BeginRegionAsync(new CaptureBounds(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom), cancellationToken);
    }

    public Task<ScreenshotFrame> BeginAllDisplaysAsync(CancellationToken cancellationToken = default)
    {
        var left = NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen);
        var top = NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen);
        var width = NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen);
        var height = NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen);
        return BeginRegionAsync(new CaptureBounds(left, top, left + width, top + height), cancellationToken);
    }

    public Task<ScreenshotFrame> BeginWindowAsync(long windowHandle, CancellationToken cancellationToken = default)
    {
        var hwnd = (nint)windowHandle;
        if (hwnd == 0 || NativeMethods.IsIconic(hwnd) || !NativeMethods.GetWindowRect(hwnd, out var rect))
        {
            throw new InvalidOperationException("That window is no longer visible.");
        }
        return BeginRegionAsync(new CaptureBounds(rect.Left, rect.Top, rect.Right, rect.Bottom), cancellationToken);
    }

    /// <summary>Grabs the screen inside <paramref name="bounds"/>.</summary>
    public Task<ScreenshotFrame> BeginRegionAsync(CaptureBounds bounds, CancellationToken cancellationToken = default) =>
        BeginAsync(bounds, () =>
        {
            var grabbed = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            try
            {
                using (var graphics = Graphics.FromImage(grabbed))
                {
                    graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, grabbed.Size, CopyPixelOperation.SourceCopy);
                }

                var (editor, display) = Duplicate(grabbed, new Rectangle(0, 0, bounds.Width, bounds.Height));
                return (grabbed, editor, display);
            }
            catch
            {
                grabbed.Dispose();
                throw;
            }
        }, cancellationToken);

    /// <summary>
    /// Cuts the shot out of the frozen desktop frame the region selector was showing,
    /// instead of grabbing the screen a second time. That is faster, and it is exactly
    /// what the user saw while selecting. Takes ownership of <paramref name="snapshot"/>.
    /// </summary>
    public Task<ScreenshotFrame> BeginFromSnapshotAsync(
        Bitmap snapshot,
        int snapshotLeft,
        int snapshotTop,
        CaptureBounds bounds,
        CancellationToken cancellationToken = default)
    {
        var local = RegionSelection.WithinSnapshot(bounds, snapshotLeft, snapshotTop, snapshot.Width, snapshot.Height);
        if (local is null || local.Width != bounds.Width || local.Height != bounds.Height)
        {
            // A selection that runs off the frame (a display arrived mid-selection) is
            // taken live rather than silently cropped.
            snapshot.Dispose();
            return BeginRegionAsync(bounds, cancellationToken);
        }

        return BeginAsync(bounds, () =>
        {
            try
            {
                var area = new Rectangle(local.Left, local.Top, local.Width, local.Height);
                var (editor, display) = Duplicate(snapshot, area);
                try
                {
                    return (FromPixels(display, area.Width, area.Height), editor, display);
                }
                catch
                {
                    editor.Dispose();
                    throw;
                }
            }
            finally
            {
                snapshot.Dispose();
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Reserves the file, gets the pixels into memory, and starts writing the PNG without
    /// waiting for it. The editor opens on the in-memory pixels; nothing it shows needs
    /// the file to exist yet.
    /// </summary>
    private async Task<ScreenshotFrame> BeginAsync(
        CaptureBounds bounds,
        Func<(Bitmap Encode, Bitmap Editor, byte[] Display)> grab,
        CancellationToken cancellationToken)
    {
        if (bounds.Width < 2 || bounds.Height < 2)
        {
            throw new ArgumentException("Screenshot selection is empty.", nameof(bounds));
        }

        var request = new CaptureTransactionRequest(
            CaptureKind.Screenshot,
            ".png",
            $"Screenshot · {bounds.Width} × {bounds.Height}",
            new Dictionary<string, object?>
            {
                ["bounds"] = new[] { bounds.Left, bounds.Top, bounds.Right, bounds.Bottom },
                ["width"] = bounds.Width,
                ["height"] = bounds.Height,
            });
        var reservation = store.Reserve(request.Kind, request.Extension);
        var (encode, editor, display) = await Task.Run(grab, cancellationToken);
        var committed = CommitAsync(reservation, request, encode, cancellationToken);
        return new ScreenshotFrame(editor, display, reservation.AbsolutePath, committed);
    }

    /// <summary>
    /// Encodes and writes the PNG. Started from the UI thread and not awaited there, so
    /// the encode runs on a worker while the move and the index append come back to the
    /// UI thread exactly as before — CaptureCompleted is still raised where it always was.
    /// The bitmap encoded here is never the one the editor holds: GDI+ objects are not
    /// safe to use from two threads at once.
    /// </summary>
    private async Task<CaptureRecord> CommitAsync(
        CaptureReservation reservation,
        CaptureTransactionRequest request,
        Bitmap encode,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _transaction.CommitReservedAsync(
                reservation,
                request,
                (temporaryPath, token) => Task.Run(() => encode.Save(temporaryPath, ImageFormat.Png), token),
                cancellationToken);
            return result.Record;
        }
        finally
        {
            encode.Dispose();
        }
    }

    private static async Task<CaptureRecord> CommittedOnlyAsync(Task<ScreenshotFrame> begin)
    {
        var frame = await begin;
        frame.Pixels.Dispose();
        return await frame.Committed;
    }

    /// <summary>
    /// Copies an area into packed BGRA rows and into an independent bitmap built from
    /// them. Two deep copies, deliberately: the editor's bitmap must share nothing with
    /// the one being encoded on another thread.
    /// </summary>
    private static (Bitmap Editor, byte[] Display) Duplicate(Bitmap source, Rectangle area)
    {
        var rowBytes = area.Width * 4;
        var display = new byte[rowBytes * area.Height];
        // The desktop frame is premultiplied. Screen pixels are opaque, where the two
        // layouts are byte-identical, so reading it in its own format skips a per-pixel
        // conversion GDI+ would otherwise do.
        var format = source.PixelFormat == PixelFormat.Format32bppPArgb
            ? PixelFormat.Format32bppPArgb
            : PixelFormat.Format32bppArgb;
        var data = source.LockBits(area, ImageLockMode.ReadOnly, format);
        try
        {
            for (var row = 0; row < area.Height; row++)
            {
                Marshal.Copy(data.Scan0 + (row * data.Stride), display, row * rowBytes, rowBytes);
            }
        }
        finally
        {
            source.UnlockBits(data);
        }

        return (FromPixels(display, area.Width, area.Height), display);
    }

    private static Bitmap FromPixels(byte[] pixels, int width, int height)
    {
        var rowBytes = width * 4;
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var row = 0; row < height; row++)
            {
                Marshal.Copy(pixels, row * rowBytes, data.Scan0 + (row * data.Stride), rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }
}
