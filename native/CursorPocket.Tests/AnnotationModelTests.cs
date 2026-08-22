using CursorPocket.Core.Annotations;

namespace CursorPocket.Tests;

public sealed class AnnotationHistoryTests
{
    [Fact]
    public void A_new_history_can_neither_undo_nor_redo()
    {
        var history = new AnnotationHistory();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.False(history.HasMarks);
        Assert.Empty(history.Visible);
    }

    [Fact]
    public void Undo_hides_the_newest_mark_and_redo_brings_it_back()
    {
        var history = new AnnotationHistory();
        var first = Line(history, 1);
        var second = Line(history, 2);

        Assert.Equal([first, second], history.Visible);

        Assert.Equal(second, history.Undo());
        Assert.Equal([first], history.Visible);
        Assert.True(history.CanRedo);

        Assert.Equal(second, history.Redo());
        Assert.Equal([first, second], history.Visible);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Redo_is_available_only_until_something_new_is_drawn()
    {
        var history = new AnnotationHistory();
        Line(history, 1);
        var second = Line(history, 2);

        history.Undo();
        Assert.True(history.CanRedo);

        // Drawing after an undo replaces the future rather than branching, so there is
        // only ever one redo path and the discarded mark cannot come back.
        var third = Line(history, 3);
        Assert.False(history.CanRedo);
        Assert.DoesNotContain(second, history.Visible);
        Assert.Contains(third, history.Visible);
    }

    [Fact]
    public void Undoing_everything_then_redoing_everything_restores_the_original_order()
    {
        var history = new AnnotationHistory();
        var marks = new[] { Line(history, 1), Line(history, 2), Line(history, 3) };

        while (history.CanUndo)
        {
            history.Undo();
        }

        Assert.Empty(history.Visible);
        // Nothing was destroyed — an undone mark is hidden, not removed.
        Assert.True(history.HasMarks);

        while (history.CanRedo)
        {
            history.Redo();
        }

        Assert.Equal(marks, history.Visible);
    }

    [Fact]
    public void Undo_and_redo_past_the_ends_report_nothing_rather_than_throwing()
    {
        var history = new AnnotationHistory();

        Assert.Null(history.Undo());
        Assert.Null(history.Redo());

        Line(history, 1);
        history.Undo();

        Assert.Null(history.Undo());
    }

    [Fact]
    public void Every_mark_gets_its_own_identity()
    {
        var history = new AnnotationHistory();

        var ids = Enumerable.Range(0, 5).Select(_ => history.AllocateId()).ToArray();

        // Identity is what lets the surface map a mark back to the element drawing it.
        // Records compare by value, so two visually identical marks would collide.
        Assert.Equal(ids.Distinct().Count(), ids.Length);
    }

    private static LineMark Line(AnnotationHistory history, int offset)
    {
        var mark = new LineMark
        {
            Id = history.AllocateId(),
            Colour = AnnotationPalette.Default.Colour,
            StrokeWidth = 6,
            Start = new AnnPoint(offset, offset),
            End = new AnnPoint(offset + 10, offset + 10),
        };
        history.Add(mark);
        return mark;
    }
}

public sealed class AnnotationMetricsTests
{
    [Theory]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    [InlineData(400, 300)]
    [InlineData(1, 1)]
    public void Sizes_increase_with_the_step_at_every_image_size(int width, int height)
    {
        var small = AnnotationMetrics.StrokeWidth(width, height, AnnotationSizeStep.Small);
        var medium = AnnotationMetrics.StrokeWidth(width, height, AnnotationSizeStep.Medium);
        var large = AnnotationMetrics.StrokeWidth(width, height, AnnotationSizeStep.Large);

        Assert.True(small < medium && medium < large, $"{small} / {medium} / {large} must increase.");

        var textSmall = AnnotationMetrics.TextSize(width, height, AnnotationSizeStep.Small);
        var textLarge = AnnotationMetrics.TextSize(width, height, AnnotationSizeStep.Large);
        Assert.True(textSmall < textLarge);
    }

    [Fact]
    public void A_tiny_region_capture_still_gets_readable_ink()
    {
        // The old surface drew every label at 32 px. On a 120 px tall region that is
        // most of the image; the floor is what keeps a small capture usable.
        var text = AnnotationMetrics.TextSize(200, 120, AnnotationSizeStep.Medium);
        var stroke = AnnotationMetrics.StrokeWidth(200, 120, AnnotationSizeStep.Medium);

        Assert.Equal(20, text, 6);
        Assert.Equal(3, stroke, 6);
    }

    [Fact]
    public void A_4K_screenshot_gets_ink_heavy_enough_to_see()
    {
        // ...and on a 2160 px tall shot, 32 px text is nearly invisible. This is the
        // other half of the same bug.
        var text = AnnotationMetrics.TextSize(3840, 2160, AnnotationSizeStep.Medium);
        var stroke = AnnotationMetrics.StrokeWidth(3840, 2160, AnnotationSizeStep.Medium);

        Assert.True(text > 32, $"Text at {text} px would be lost on a 4K capture.");
        Assert.True(stroke > 6, $"A {stroke} px stroke would be lost on a 4K capture.");
    }

    [Fact]
    public void The_short_edge_decides_the_size_so_a_panorama_and_a_column_match()
    {
        Assert.Equal(
            AnnotationMetrics.StrokeWidth(4000, 800, AnnotationSizeStep.Medium),
            AnnotationMetrics.StrokeWidth(800, 4000, AnnotationSizeStep.Medium),
            6);
    }

    [Fact]
    public void A_highlighter_is_always_thicker_than_a_pen()
    {
        foreach (var step in Enum.GetValues<AnnotationSizeStep>())
        {
            Assert.True(
                AnnotationMetrics.HighlightWidth(1920, 1080, step) >
                AnnotationMetrics.StrokeWidth(1920, 1080, step));
        }
    }

    [Fact]
    public void Stepping_stops_at_the_ends_instead_of_wrapping()
    {
        Assert.Equal(AnnotationSizeStep.Small, AnnotationMetrics.Step(AnnotationSizeStep.Small, -1));
        Assert.Equal(AnnotationSizeStep.Large, AnnotationMetrics.Step(AnnotationSizeStep.Large, 1));
        Assert.Equal(AnnotationSizeStep.Medium, AnnotationMetrics.Step(AnnotationSizeStep.Small, 1));
        Assert.Equal(AnnotationSizeStep.Medium, AnnotationMetrics.Step(AnnotationSizeStep.Large, -1));
    }
}

public sealed class AnnotationPaletteTests
{
    [Fact]
    public void No_ink_is_one_of_the_apps_state_colours()
    {
        // Green means ready, live, the one primary action, or the current selection, and
        // the annotation surface spends green on the active tool and the crop handles.
        // Red means recording or destructive. Blue marks text and link captures. If the
        // user could paint with any of them, their mark would read as CursorPocket
        // talking rather than as their own annotation.
        string[] stateColours = ["#45E08C", "#FF5F6B", "#7FBBFF"];

        foreach (var ink in AnnotationPalette.Inks)
        {
            Assert.DoesNotContain(ink.Hex, stateColours, StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Six_inks_map_to_the_digit_keys_and_nothing_else_does()
    {
        Assert.Equal(6, AnnotationPalette.Inks.Count);

        for (var digit = 1; digit <= 6; digit++)
        {
            Assert.Equal(AnnotationPalette.Inks[digit - 1], AnnotationPalette.ForKey(digit));
        }

        Assert.Null(AnnotationPalette.ForKey(0));
        Assert.Null(AnnotationPalette.ForKey(7));
        Assert.Null(AnnotationPalette.ForKey(-1));
    }

    [Fact]
    public void Every_ink_parses_to_an_opaque_colour()
    {
        Assert.All(AnnotationPalette.Inks, ink => Assert.Equal(255, ink.Colour.A));
    }

    [Fact]
    public void A_highlighter_ink_is_translucent_enough_to_read_through()
    {
        var highlight = AnnotationPalette.Default.Colour.WithAlpha(AnnotationPalette.HighlightAlpha);

        Assert.Equal(AnnotationPalette.HighlightAlpha, highlight.A);
        Assert.True(highlight.A < 255 / 2, "Text under a highlighter has to stay legible.");
        // The hue is untouched; only the alpha changes.
        Assert.Equal(AnnotationPalette.Default.Colour.R, highlight.R);
    }

    [Theory]
    [InlineData("#FFFFFF", 255, 255, 255, 255)]
    [InlineData("2FD8E8", 255, 47, 216, 232)]
    [InlineData("#5C112233", 92, 17, 34, 51)]
    public void Hex_parses_with_or_without_a_hash_and_with_or_without_alpha(
        string hex, byte a, byte r, byte g, byte b)
    {
        Assert.Equal(new AnnColor(a, r, g, b), AnnColor.FromHex(hex));
    }

    [Fact]
    public void A_malformed_hex_colour_is_rejected_rather_than_silently_black()
    {
        Assert.Throws<FormatException>(() => AnnColor.FromHex("#ABC"));
    }

    [Fact]
    public void A_step_marker_number_stays_legible_on_every_ink()
    {
        var dark = new AnnColor(255, 11, 16, 15);
        var light = new AnnColor(255, 242, 247, 244);

        foreach (var ink in AnnotationPalette.Inks)
        {
            var on = AnnotationPalette.OnInk(ink.Colour);
            Assert.True(on == dark || on == light, "the digit is only ever near-black or near-white");
            Assert.True(Contrast(ink.Colour, on) > 3.5, $"{ink.Name} would not carry a readable digit");
        }
    }

    [Fact]
    public void Citron_takes_a_dark_digit_and_Signal_a_light_one()
    {
        // The case a plain channel mean gets wrong. Citron #F5E663 and Signal #F4353F
        // have almost the same mean, but green carries most of perceived brightness and
        // red carries little, so Rec. 709 luma puts them on opposite sides: 0.85 against
        // 0.37. A mean would have given both the same digit and made one unreadable.
        var citron = AnnotationPalette.Inks.Single(ink => ink.Name == "Citron");
        var signal = AnnotationPalette.Inks.Single(ink => ink.Name == "Signal");

        Assert.Equal(new AnnColor(255, 11, 16, 15), AnnotationPalette.OnInk(citron.Colour));
        Assert.Equal(new AnnColor(255, 242, 247, 244), AnnotationPalette.OnInk(signal.Colour));
    }

    private static double Contrast(AnnColor a, AnnColor b)
    {
        var first = Relative(a);
        var second = Relative(b);
        var lighter = Math.Max(first, second);
        var darker = Math.Min(first, second);
        return (lighter + 0.05) / (darker + 0.05);

        static double Relative(AnnColor colour) =>
            (0.2126 * Channel(colour.R)) + (0.7152 * Channel(colour.G)) + (0.0722 * Channel(colour.B));

        static double Channel(byte value)
        {
            var v = value / 255d;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
    }
}
