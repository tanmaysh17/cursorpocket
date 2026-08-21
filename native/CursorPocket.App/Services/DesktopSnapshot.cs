using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CursorPocket_App.Services;

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
    /// </summary>
    public static BitmapImage Capture(NativeMethods.Rect bounds)
    {
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (width < 1 || height < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), "Desktop snapshot bounds must have a visible area.");
        }

        var frame = new MemoryStream(EstimateCapacity(width, height));
        using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
            bitmap.Save(frame, ImageFormat.Bmp);
        }
        frame.Position = 0;

        var source = new BitmapImage();
        _ = DecodeAsync(source, frame);
        return source;
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
