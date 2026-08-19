using CursorPocket.Core.Models;

namespace CursorPocket.Core.Services;

/// <summary>
/// Places the live camera self-view inside the area being recorded.
/// <para>
/// CursorPocket owns the camera during a recording rather than handing it to
/// FFmpeg: DirectShow gives a single consumer exclusive use of the device, so a
/// self-view and an FFmpeg <c>dshow</c> camera input cannot coexist. The webcam
/// therefore reaches the file the same way the user sees it — the self-view sits
/// on screen inside the captured area and the screen capture picks it up. That
/// only works if the window lands within the recorded rectangle, which is what
/// this type guarantees.
/// </para>
/// </summary>
public static class CameraSelfViewPlacement
{
    /// <summary>Matches the inset FFmpeg used when it composited the overlay itself.</summary>
    public const int Margin = 32;

    public const int MinimumWidth = 160;
    public const int MaximumWidth = 640;

    public static CaptureBounds Compute(CaptureBounds captureArea, string position, int cameraWidth)
    {
        if (captureArea.Width < 2 || captureArea.Height < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(captureArea), "The recorded area must have a visible size.");
        }

        var width = Math.Clamp(cameraWidth, MinimumWidth, MaximumWidth);
        width = Math.Min(width, captureArea.Width);
        var height = Math.Max(90, (int)Math.Round(width * 9d / 16d));
        height -= height % 2;
        height = Math.Min(height, captureArea.Height);

        // A capture area too small for the preferred inset keeps whatever room is
        // left, so the self-view is never pushed outside the recorded rectangle.
        var insetX = Math.Min(Margin, Math.Max(0, captureArea.Width - width));
        var insetY = Math.Min(Margin, Math.Max(0, captureArea.Height - height));
        var alignLeft = position is "top-left" or "bottom-left";
        var alignTop = position is "top-left" or "top-right";
        var left = alignLeft ? captureArea.Left + insetX : captureArea.Right - width - insetX;
        var top = alignTop ? captureArea.Top + insetY : captureArea.Bottom - height - insetY;
        return new CaptureBounds(left, top, left + width, top + height);
    }

    /// <summary>
    /// Whether the screen capture for this source will actually contain the
    /// self-view. Window capture reads a single HWND, so an overlay on top of it
    /// is not part of the recorded frames and the file ends up with no webcam.
    /// </summary>
    public static bool IsRecordedForSource(VideoSourceKind sourceKind) =>
        sourceKind is VideoSourceKind.Display or VideoSourceKind.Region;
}
