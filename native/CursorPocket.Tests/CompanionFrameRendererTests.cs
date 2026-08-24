using System.Drawing;
using CursorPocket_App.Services;

namespace CursorPocket.Tests;

public sealed class CompanionFrameRendererTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Standard_themes_keep_the_documented_ready_and_recording_colours(bool isDark)
    {
        var colors = CompanionFrameRenderer.ResolvePalette(
            isHighContrast: false,
            isDark,
            Color.Magenta);

        Assert.Equal(CompanionFrameRenderer.ReadyColor, colors.Ready);
        Assert.Equal(
            isDark ? CompanionFrameRenderer.RecordingColor : CompanionFrameRenderer.LightRecordingColor,
            colors.Recording);
        Assert.Equal(isDark ? 220 : 190, colors.Outline.A);
        Assert.Equal(
            (isDark ? Color.White : Color.Black).ToArgb(),
            Color.FromArgb(colors.Outline.R, colors.Outline.G, colors.Outline.B).ToArgb());
    }

    [Fact]
    public void High_contrast_uses_the_system_selection_colour_for_both_states()
    {
        var selection = Color.FromArgb(255, 12, 34, 56);

        var colors = CompanionFrameRenderer.ResolvePalette(
            isHighContrast: true,
            isDark: true,
            selection);

        Assert.Equal(selection, colors.Ready);
        Assert.Equal(selection, colors.Recording);
    }

    [Fact]
    public void Ready_marker_keeps_a_bright_unobscured_four_pixel_core()
    {
        using var frame = CompanionFrameRenderer.Render(
            phase: 0,
            CompanionFrameRenderer.ReadyColor,
            Color.FromArgb(190, 0, 0, 0));

        Assert.Equal(CompanionFrameRenderer.ReadyColor, frame.GetPixel(5, 5));
        Assert.True(
            CountBrightStatusPixels(frame, CompanionFrameRenderer.ReadyColor) >= 6,
            "The contrast ring must not consume the ready marker's bright core.");
        Assert.True(
            PerceptualBrightness(CompanionFrameRenderer.ReadyColor) >= 0.55,
            "A four-pixel status mark needs a high-luminance ready colour.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Recording_marker_keeps_its_contrasting_red_core(bool isDark)
    {
        var status = isDark
            ? CompanionFrameRenderer.RecordingColor
            : CompanionFrameRenderer.LightRecordingColor;
        var outline = isDark
            ? Color.FromArgb(220, 255, 255, 255)
            : Color.FromArgb(190, 0, 0, 0);
        using var frame = CompanionFrameRenderer.Render(
            phase: Math.PI,
            status,
            outline);

        Assert.Equal(status, frame.GetPixel(5, 5));
        Assert.True(
            CountBrightStatusPixels(frame, status) >= 6,
            "The contrast ring must remain outside the recording marker's core.");
    }

    private static int CountBrightStatusPixels(Bitmap bitmap, Color expected)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.A >= 220 &&
                    Math.Abs(pixel.R - expected.R) <= 2 &&
                    Math.Abs(pixel.G - expected.G) <= 2 &&
                    Math.Abs(pixel.B - expected.B) <= 2)
                {
                    count++;
                }
            }
        }
        return count;
    }

    private static double PerceptualBrightness(Color color) =>
        ((0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B)) / 255;
}
