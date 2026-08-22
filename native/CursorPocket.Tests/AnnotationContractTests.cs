namespace CursorPocket.Tests;

/// <summary>
/// Locks the annotation surface's guarantees. Until this class existed the annotation
/// XAML was not a test fixture at all, so the toolbar could be rewritten without
/// breaking a single test. These assertions describe intent — when the surface is
/// rebuilt, move an assertion to match the new intent rather than reverting the code.
/// </summary>
public sealed class AnnotationContractTests
{
    [Fact]
    public void Annotation_keys_survive_a_canvas_that_cannot_take_focus()
    {
        var xaml = ReadFixture("AnnotationWindow.xaml");
        var code = ReadFixture("AnnotationWindow.xaml.cs.txt");

        // A Canvas never receives KeyDown, so the drawing surface cannot carry these
        // keys. They live on the root as accelerators, which fire whichever toolbar
        // control currently holds focus.
        Assert.Contains("<Grid.KeyboardAccelerators>", xaml, StringComparison.Ordinal);
        Assert.Contains("Key=\"Enter\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Key=\"Z\" Modifiers=\"Control\"", xaml, StringComparison.Ordinal);

        // ...but the inline text tool commits on Enter and owns its own undo, so every
        // accelerator stands down while a text box has focus.
        Assert.Contains("is TextBox", code, StringComparison.Ordinal);

        // Escape also arrives globally: the window can lose activation while still shown.
        Assert.Contains("EscapeHotkey.Capture", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Escape_keeps_the_original_and_never_writes_a_file()
    {
        var code = ReadFixture("AnnotationWindow.xaml.cs.txt");

        var cancel = Slice(code, "private void Cancel()", "private static Windows.UI.Color ParseColor");
        Assert.Contains("Cancelled?.Invoke", cancel, StringComparison.Ordinal);
        // Cancelling is the one path that must not touch the saved capture.
        Assert.DoesNotContain("File.Move", cancel, StringComparison.Ordinal);
        Assert.DoesNotContain("Save", cancel, StringComparison.Ordinal);
    }

    [Fact]
    public void Drawing_coordinates_are_image_pixels()
    {
        var code = ReadFixture("AnnotationWindow.xaml.cs.txt");

        // The stage is sized to the source bitmap, so a canvas coordinate IS an image
        // pixel. Nudging by one pixel, the native-pixel readout, and a faithful export
        // all rest on this identity; nothing may scale pointer input on the way in.
        Assert.Contains("Stage.Width = source.Width", code, StringComparison.Ordinal);
        Assert.Contains("Stage.Height = source.Height", code, StringComparison.Ordinal);
        Assert.Contains("DrawingSurface.Width = source.Width", code, StringComparison.Ordinal);
        Assert.Contains("DrawingSurface.Height = source.Height", code, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fresh_screenshot_is_copied_before_the_editor_opens_and_the_editor_comes_forward()
    {
        var main = ReadFixture("MainWindow.xaml.cs.txt");
        var capture = Slice(main, "private async Task CaptureScreenshotAsync", "private void SelectRegion");

        // Pasteable the moment it is taken, not only once annotation is dismissed.
        var copy = capture.IndexOf("CopyImageToClipboardAsync", StringComparison.Ordinal);
        var open = capture.IndexOf("new AnnotationWindow", StringComparison.Ordinal);
        Assert.True(copy >= 0 && open > copy, "The shot must reach the clipboard before the editor opens.");

        // Command mode has just hidden itself, so the source app owns the foreground
        // lock. Activate() alone loses that race and leaves the editor behind or
        // minimized — this is why ForceForeground exists.
        Assert.Contains("WindowPlacement.ForceForeground(editor)", capture, StringComparison.Ordinal);

        // Both outcomes produce a receipt; a capture never completes silently.
        Assert.Contains("editor.Saved", capture, StringComparison.Ordinal);
        Assert.Contains("editor.Cancelled", capture, StringComparison.Ordinal);
    }

    [Fact]
    public void The_annotation_surface_has_exactly_one_primary_action()
    {
        var xaml = ReadFixture("AnnotationWindow.xaml");

        var primaries = Occurrences(xaml, "PocketPrimaryButton");
        Assert.Equal(1, primaries);
    }


    [Theory]
    [InlineData("AnnotationInkSignal")]
    [InlineData("AnnotationInkAmber")]
    [InlineData("AnnotationInkCitron")]
    [InlineData("AnnotationInkCyan")]
    [InlineData("AnnotationInkViolet")]
    [InlineData("AnnotationInkChalk")]
    public void Every_annotation_ink_has_a_colour_and_a_brush(string name)
    {
        var xaml = ReadFixture("App.xaml");

        Assert.Contains($"x:Key=\"{name}Color\"", xaml, StringComparison.Ordinal);
        Assert.Contains($"x:Key=\"{name}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void No_annotation_ink_is_a_state_colour()
    {
        var xaml = ReadFixture("App.xaml");
        var inks = Slice(xaml, "AnnotationInkSignalColor", "SolidColorBrush x:Key=\"AnnotationInkSignal\"");

        // Green means ready, live, the one primary action, or the current selection, and
        // red means recording or destructive. If the user could paint with either, a
        // mark on a screenshot would be indistinguishable from CursorPocket's own state
        // — and the annotation surface needs green for the active tool and crop handles.
        Assert.DoesNotContain("45E08C", inks, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FF5F6B", inks, StringComparison.OrdinalIgnoreCase);
        // Blue is reserved for text and link captures, and PocketBlue is close enough to
        // omasnap's blue ink to be confusable.
        Assert.DoesNotContain("7FBBFF", inks, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tool_icons_share_one_box_stroke_and_cap()
    {
        var xaml = ReadFixture("App.xaml");
        var style = Slice(xaml, "x:Key=\"PocketToolIconStroke\"", "x:Key=\"PocketToolIconStrokeHeavy\"");

        // 19 px of content in a 24 px box on a 2 px round-capped stroke. These
        // proportions are what make a dense toolbar read as an instrument, so they are
        // set once here rather than per icon.
        Assert.Contains("Property=\"Width\" Value=\"24\"", style, StringComparison.Ordinal);
        Assert.Contains("Property=\"Height\" Value=\"24\"", style, StringComparison.Ordinal);
        Assert.Contains("Property=\"StrokeThickness\" Value=\"2\"", style, StringComparison.Ordinal);
        Assert.Contains("Property=\"StrokeStartLineCap\" Value=\"Round\"", style, StringComparison.Ordinal);
        Assert.Contains("Property=\"StrokeLineJoin\" Value=\"Round\"", style, StringComparison.Ordinal);
        // Stretch None keeps every geometry authored in that same 24 px box; letting it
        // scale would make the stroke weight vary per icon.
        Assert.Contains("Property=\"Stretch\" Value=\"None\"", style, StringComparison.Ordinal);
    }

    [Fact]
    public void Icon_geometry_is_never_a_PathGeometry_resource()
    {
        var xaml = ReadFixture("App.xaml");

        // WinUI only parses the abbreviated "M4,20 L20,4" syntax through Path.Data's own
        // type converter. PathGeometry.Figures rejects it at compile time (WMC0055) and
        // an x:String resource reaches Path.Data untyped and fails at load, so icon
        // geometry lives inline at each use site.
        Assert.DoesNotContain("<PathGeometry", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tool_button_is_the_same_control_as_every_other_button()
    {
        var xaml = ReadFixture("App.xaml");
        var style = Slice(xaml, "x:Key=\"PocketToolButton\"", "x:Key=\"PocketSwatchButton\"");

        // One button template means one height, radius, hover, and pressed response
        // everywhere in the app. A toolbar that rolled its own would drift.
        Assert.Contains("BasedOn=\"{StaticResource PocketButtonBase}\"", style, StringComparison.Ordinal);
        Assert.Contains("Property=\"Width\" Value=\"46\"", style, StringComparison.Ordinal);
        Assert.Contains("Property=\"Height\" Value=\"36\"", style, StringComparison.Ordinal);
    }
    private static int Occurrences(string source, string value)
    {
        var count = 0;
        for (var index = source.IndexOf(value, StringComparison.Ordinal); index >= 0;
             index = source.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{startMarker}' was not found.");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end >= 0, $"'{endMarker}' was not found after '{startMarker}'.");
        return source[start..end];
    }

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
