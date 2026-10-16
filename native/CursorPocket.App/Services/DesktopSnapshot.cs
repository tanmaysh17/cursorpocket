using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CursorPocket_App.Services;

/// <summary>
/// A grabbed desktop frame: the pixels, and the same pixels encoded for the backdrop.
/// Produced without touching WinUI, so it can be made on a worker thread ahead of time.
/// </summary>
internal sealed class FrozenDesktop : IDisposable
{
    private readonly long _takenAt = Stopwatch.GetTimestamp();

    public FrozenDesktop(NativeMethods.Rect bounds, Bitmap frame, MemoryStream encoded)
    {
        Bounds = bounds;
        Frame = frame;
        Encoded = encoded;
    }

    public NativeMethods.Rect Bounds { get; }

    /// <summary>The pixels. Whoever takes it from here owns it.</summary>
    public Bitmap? Frame { get; private set; }

    public MemoryStream? Encoded { get; private set; }

    public TimeSpan Age => Stopwatch.GetElapsedTime(_takenAt);

    public Bitmap TakeFrame()
    {
        var frame = Frame ?? throw new ObjectDisposedException(nameof(FrozenDesktop));
        Frame = null;
        return frame;
    }

    public MemoryStream TakeEncoded()
    {
        var encoded = Encoded ?? throw new ObjectDisposedException(nameof(FrozenDesktop));
        Encoded = null;
        return encoded;
    }

    public void Dispose()
    {
        Frame?.Dispose();
        Frame = null;
        Encoded?.Dispose();
        Encoded = null;
    }
}

internal static class DesktopSnapshot
{
    /// <summary>
    /// Grabs the desktop under the command overlay.
    /// <para>
    /// The frame never touches the filesystem. Staging it through the temp folder
    /// meant writing and then re-reading roughly 33 MB at 4K on the hotkey path,
    /// plus a temp-directory scan on every activation, which was the largest
    /// remaining cost of opening command mode. BMP still avoids synchronous PNG
    /// compression while handing the decoder an exact, lossless desktop frame.
    /// </para>
    /// <para>
    /// No WinUI is touched here, so this runs on a worker thread — and it can run
    /// while command mode is still on screen, because a GDI screen grab cannot see
    /// WinUI or DirectComposition surfaces. That is what lets the frame be taken the
    /// moment screenshot mode opens instead of after the region command is chosen.
    /// </para>
    /// </summary>
    public static FrozenDesktop Freeze(NativeMethods.Rect bounds)
    {
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (width < 1 || height < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), "Desktop snapshot bounds must have a visible area.");
        }

        var encoded = new MemoryStream(EstimateCapacity(width, height));
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        try
        {
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
            }

            bitmap.Save(encoded, ImageFormat.Bmp);
        }
        catch
        {
            bitmap.Dispose();
            encoded.Dispose();
            throw;
        }

        encoded.Position = 0;
        return new FrozenDesktop(bounds, bitmap, encoded);
    }

    /// <summary>
    /// The backdrop for a frozen frame. Takes the encoded copy; the pixels stay with
    /// <paramref name="frozen"/>. Must run on the UI thread.
    /// </summary>
    public static BitmapImage Present(FrozenDesktop frozen)
    {
        var source = new BitmapImage();
        _ = DecodeAsync(source, frozen.TakeEncoded());
        return source;
    }

    /// <summary>The current virtual desktop, in physical pixels.</summary>
    public static NativeMethods.Rect VirtualDesktop()
    {
        var left = NativeMethods.GetSystemMetrics(NativeMethods.SmXVirtualScreen);
        var top = NativeMethods.GetSystemMetrics(NativeMethods.SmYVirtualScreen);
        return new NativeMethods.Rect
        {
            Left = left,
            Top = top,
            Right = left + NativeMethods.GetSystemMetrics(NativeMethods.SmCxVirtualScreen),
            Bottom = top + NativeMethods.GetSystemMetrics(NativeMethods.SmCyVirtualScreen),
        };
    }

    private static int EstimateCapacity(int width, int height)
    {
        // 32bpp rows plus the BMP header, clamped so an implausible virtual-screen
        // size cannot ask for a negative or absurd initial buffer.
        var bytes = (long)width * height * 4 + 1024;
        return (int)Math.Clamp(bytes, 4096, int.MaxValue);
    }

    private static async Task DecodeAsync(BitmapImage image, MemoryStream frame)
    {
        try
        {
            using var randomAccess = frame.AsRandomAccessStream();
            await image.SetSourceAsync(randomAccess);
        }
        catch (Exception)
        {
            // The backdrop is decorative. Command mode stays usable over the plain
            // graphite surface if the decoder rejects this frame.
        }
        finally
        {
            await frame.DisposeAsync();
        }
    }
}
