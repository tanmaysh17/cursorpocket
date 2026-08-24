using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace CursorPocket_App.Services;

/// <summary>
/// Renders the cursor companion independently of its native window so the tiny
/// status mark can be verified at the pixel level.
/// </summary>
internal static class CompanionFrameRenderer
{
    internal const int FrameSize = 28;
    internal static readonly Color ReadyColor = Color.FromArgb(255, 54, 229, 140);
    internal static readonly Color LightRecordingColor = Color.FromArgb(255, 215, 53, 70);
    internal static readonly Color RecordingColor = Color.FromArgb(255, 255, 89, 100);

    internal static (Color Ready, Color Recording, Color Outline) ResolvePalette(
        bool isHighContrast,
        bool isDark,
        Color selection)
    {
        var ready = isHighContrast ? selection : ReadyColor;
        var recording = isHighContrast
            ? selection
            : isDark ? RecordingColor : LightRecordingColor;
        var outline = isDark
            ? Color.FromArgb(220, 255, 255, 255)
            : Color.FromArgb(190, 0, 0, 0);
        return (ready, recording, outline);
    }

    internal static Bitmap Render(double phase, Color status, Color outline)
    {
        var bitmap = new Bitmap(FrameSize, FrameSize, PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var pulse = (float)((Math.Sin(phase) + 1) / 2);
        using var glow = new SolidBrush(Color.FromArgb((int)(34 + pulse * 34), status.R, status.G, status.B));
        var glowSize = 10f + pulse * 3f;
        graphics.FillEllipse(glow, 5.5f - glowSize / 2, 5.5f - glowSize / 2, glowSize, glowSize);

        // The contrast ring sits outside the 4 px status core. Drawing the core
        // last prevents a 1 px stroke from consuming most of this micro-indicator.
        using var edge = new Pen(outline, 1f);
        graphics.DrawEllipse(edge, 2.5f, 2.5f, 6f, 6f);
        using var dot = new SolidBrush(status);
        graphics.FillEllipse(dot, 3.5f, 3.5f, 4f, 4f);

        return bitmap;
    }
}
