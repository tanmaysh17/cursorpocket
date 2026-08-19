using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CursorPocket_App.Services;

internal static class DesktopSnapshot
{
    public static BitmapImage Capture(NativeMethods.Rect bounds)
    {
        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (width < 1 || height < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), "Desktop snapshot bounds must have a visible area.");
        }

        var cacheDirectory = Path.Combine(Path.GetTempPath(), "CursorPocket", "desktop-snapshots");
        Directory.CreateDirectory(cacheDirectory);
        DeleteExpiredSnapshots(cacheDirectory);
        // BMP avoids synchronous PNG compression on the hotkey path. At 4K this
        // removes the largest source of command-mode launch latency while the
        // decoder still receives an exact, lossless desktop frame.
        var snapshotPath = Path.Combine(cacheDirectory, $"{Guid.NewGuid():N}.bmp");

        using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
            bitmap.Save(snapshotPath, ImageFormat.Bmp);
        }

        var source = new BitmapImage();
        source.ImageOpened += (_, _) => DeleteSnapshot(snapshotPath);
        source.ImageFailed += (_, _) => DeleteSnapshot(snapshotPath);
        source.UriSource = new Uri(snapshotPath, UriKind.Absolute);
        return source;
    }

    private static void DeleteExpiredSnapshots(string directory)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(directory, "*.*"))
            {
                if ((path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)) &&
                    File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddHours(-1))
                {
                    File.Delete(path);
                }
            }
        }
        catch (IOException)
        {
            // A concurrent overlay may still be decoding its own snapshot.
        }
        catch (UnauthorizedAccessException)
        {
            // The overlay can still use its new file even if cleanup is unavailable.
        }
    }

    private static void DeleteSnapshot(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // The decoder can release the file shortly after the event; the next
            // overlay launch removes this short-lived cache file.
        }
        catch (UnauthorizedAccessException)
        {
            // Leave cleanup to the next launch rather than breaking capture mode.
        }
    }
}
