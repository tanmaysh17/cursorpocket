using CursorPocket.Core.Models;

namespace CursorPocket.Core.Services;

public enum TransientLayoutMode
{
    Regular,
    Short,
    Narrow,
    Constrained,
}

public sealed record TransientWindowLayout(
    CaptureBounds Bounds,
    TransientLayoutMode Mode,
    double Scale);

public static class TransientWindowLayoutPolicy
{
    public static TransientWindowLayout Resolve(
        CaptureBounds workArea,
        int desiredWidthDips,
        int desiredHeightDips,
        double scale,
        int marginDips = 22,
        double textScale = 1)
    {
        scale = double.IsFinite(scale) ? Math.Max(1, scale) : 1;
        textScale = double.IsFinite(textScale) ? Math.Clamp(textScale, 1, 2.25) : 1;
        var margin = Math.Max(0, (int)Math.Round(marginDips * scale));
        var availableWidth = Math.Max(1, workArea.Width - (margin * 2));
        var availableHeight = Math.Max(1, workArea.Height - (margin * 2));
        var desiredWidth = Math.Max(1, (int)Math.Round(desiredWidthDips * scale));
        var desiredHeight = Math.Max(1, (int)Math.Round(desiredHeightDips * scale * textScale));
        var width = Math.Min(desiredWidth, availableWidth);
        var height = Math.Min(desiredHeight, availableHeight);
        var widthFits = desiredWidth <= availableWidth;
        var heightFits = desiredHeight <= availableHeight;
        var mode = (widthFits, heightFits) switch
        {
            (true, true) => TransientLayoutMode.Regular,
            (true, false) when availableHeight >= desiredHeight * 0.66 => TransientLayoutMode.Short,
            (false, true) when availableWidth >= desiredWidth * 0.66 => TransientLayoutMode.Narrow,
            _ => TransientLayoutMode.Constrained,
        };
        return new TransientWindowLayout(
            new CaptureBounds(
                workArea.Right - width - margin,
                workArea.Top + margin,
                workArea.Right - margin,
                workArea.Top + margin + height),
            mode,
            scale);
    }
}
