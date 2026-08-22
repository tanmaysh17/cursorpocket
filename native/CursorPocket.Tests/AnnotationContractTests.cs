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
        Assert.Contains("Key=\"Y\" Modifiers=\"Control\"", xaml, StringComparison.Ordinal);

        // Escape alone stays a global scoped lease: the window can lose activation while
        // still shown, and the drawing surface cannot hold focus to catch it.
        Assert.Contains("EscapeHotkey.Capture", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Something_focusable_holds_focus_so_accelerators_can_route()
    {
        var xaml = ReadFixture("AnnotationWindow.xaml");
        var code = ReadFixture("AnnotationWindow.xaml.cs.txt");

        // Accelerators only route while an element inside the window holds focus. A
        // Canvas cannot take focus — Focus() on it returns false and says nothing — and
        // a Grid cannot stand in for it either, because IsTabStop belongs to Control and
        // a Grid is a Panel. Verified on the installed build: with nothing focused, not
        // one key worked until a toolbar button had been clicked.
        Assert.Contains("x:Name=\"CanvasHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsTabStop=\"True\"", xaml, StringComparison.Ordinal);
        // No focus ring: this host exists to receive keys, not to look selected.
        Assert.Contains("UseSystemFocusVisuals=\"False\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DrawingSurface.Focus(", code, StringComparison.Ordinal);

        // Focus must land on Loaded. Activated fires before the content tree exists, so
        // focusing there silently does nothing.
        Assert.Contains("CanvasHost.Loaded", code, StringComparison.Ordinal);
        Assert.Contains("CanvasHost.Focus(FocusState.Programmatic)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Focus_lookups_survive_a_window_that_has_no_XamlRoot_yet()
    {
        var code = ReadFixture("AnnotationWindow.xaml.cs.txt");

        // FocusManager.GetFocusedElement throws ArgumentException rather than returning
        // null when XamlRoot is not ready, and the first Activated fires before it is.
        // That crashed the editor on open, on the installed build.
        var guards = Occurrences(code, "Content?.XamlRoot is null");
        var lookups = Occurrences(code, "FocusManager.GetFocusedElement(");
        Assert.Equal(lookups, guards);
    }

    [Fact]
    public void Bare_letter_keys_are_page_accelerators_rather_than_global_hotkeys()
    {
        var xaml = ReadFixture("AnnotationWindow.xaml");
        var code = ReadFixture("AnnotationWindow.xaml.cs.txt");

        // Command mode registers bare keys globally because it owns the user's attention
        // and cannot take focus. This surface can take focus, so accelerators are
        // strictly better: they cannot leak a keystroke into another application, and
        // they cannot fail to register because something else already holds the key.
        foreach (var key in new[] { "V", "A", "L", "P", "H", "R", "E", "T" })
        {
            Assert.Contains($"Key=\"{key}\" Invoked=\"ToolAccelerator_Invoked\"", xaml, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("PaletteHotkeyService", code, StringComparison.Ordinal);
        Assert.DoesNotContain("RegisterHotKey", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_accelerator_stands_down_while_a_text_box_has_focus()
    {
        var code = ReadFixture("AnnotationWindow.xaml.cs.txt");

        // Bare letters would otherwise swallow typing in the inline text editor. With
        // this many accelerators, repeating the check per handler is how one gets
        // missed, so they all route through one guard.
        Assert.Contains("private bool ToolKeysActive() =>", code, StringComparison.Ordinal);
        Assert.Contains("is not TextBox", code, StringComparison.Ordinal);

        // Every Invoked handler must consult it. Counted rather than spot-checked,
        // because the failure mode is one handler quietly missing the guard.
        var handlers = Occurrences(code, "_Invoked(KeyboardAccelerator sender");
        var guards = Occurrences(code, "if (!ToolKeysActive())");
        Assert.True(
            guards >= handlers - 1,
            $"{handlers} accelerator handlers but only {guards} guards — one is unguarded.");
    }

    [Fact]
    public void Escape_returns_to_select_first_and_then_keeps_the_original()
    {
        var code = ReadFixture("AnnotationWindow.xaml.cs.txt");

        var escape = Slice(code, "private void HandleEscape()", "private void Cancel()");
        // Two stage: an armed creation tool returns to Select, and Escape from Select
        // closes. The last press always keeps the original, so nothing is ever lost —
        // it can just take two presses. A dead-feeling first press is the risk, so the
        // status strip has to say what the next press will do.
        Assert.Contains("AnnotationTool.Select", escape, StringComparison.Ordinal);
        Assert.Contains("Esc again to keep the original", escape, StringComparison.Ordinal);
        Assert.Contains("Cancel();", escape, StringComparison.Ordinal);

        var cancel = Slice(code, "private void Cancel()", "// ------");
        Assert.Contains("Cancelled?.Invoke", cancel, StringComparison.Ordinal);
        // Cancelling is the one path that must not touch the saved capture.
        Assert.DoesNotContain("File.Move", cancel, StringComparison.Ordinal);
        Assert.DoesNotContain("Flatten", cancel, StringComparison.Ordinal);
    }

    [Fact]
    public void Drawing_coordinates_are_image_pixels()
    {
        var code = ReadFixture("AnnotationWindow.xaml.cs.txt");

        // The stage is sized to the source bitmap, so a canvas coordinate IS an image
        // pixel. Nudging by one pixel, the native-pixel readout, and a faithful export
        // all rest on this identity; nothing may scale pointer input on the way in.
        Assert.Contains("Stage.Width = _sourceWidth", code, StringComparison.Ordinal);
        Assert.Contains("Stage.Height = _sourceHeight", code, StringComparison.Ordinal);
        Assert.Contains("DrawingSurface.Width = _sourceWidth", code, StringComparison.Ordinal);
        Assert.Contains("DrawingSurface.Height = _sourceHeight", code, StringComparison.Ordinal);
        // Pointer input is read straight from the drawing surface, never rescaled.
        Assert.Contains("GetCurrentPoint(DrawingSurface).Position", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_preview_and_the_export_share_one_geometry_source()
    {
        var code = ReadFixture("AnnotationWindow.xaml.cs.txt");
        var export = ReadFixture("AnnotationExport.cs.txt");

        // Both sides derive every shape from Core. This is the structural fix for a
        // preview and an export that had already drifted three ways.
        Assert.Contains("AnnotationGeometry.ArrowOutline", code, StringComparison.Ordinal);
        Assert.Contains("AnnotationGeometry.ArrowOutline", export, StringComparison.Ordinal);

        // The arrow head is a filled polygon on both sides. A line cap would reintroduce
        // the original bug: cap styles differ per renderer and scale with pen width in
        // renderer-specific ways.
        Assert.DoesNotContain("ArrowAnchor", export, StringComparison.Ordinal);
        Assert.DoesNotContain("AdjustableArrowCap", export, StringComparison.Ordinal);
        Assert.DoesNotContain("PenLineCap.Triangle", code, StringComparison.Ordinal);

        // Grid fitting snaps glyph advances to the pixel grid, making the same string a
        // different width at a different scale.
        // Qualified, so the comment explaining why grid fitting is wrong does not itself
        // trip the assertion.
        Assert.Contains("TextRenderingHint = TextRenderingHint.AntiAlias;", export, StringComparison.Ordinal);
        Assert.DoesNotContain("TextRenderingHint.AntiAliasGridFit", export, StringComparison.Ordinal);
    }

    [Fact]
    public void A_filled_shape_is_filled_in_the_saved_file_too()
    {
        var code = ReadFixture("AnnotationWindow.xaml.cs.txt");
        var export = ReadFixture("AnnotationExport.cs.txt");

        // The old surface previewed a faint fill behind every rectangle and wrote none
        // of it. Fill is now a property of the mark and both renderers honour it at the
        // same alpha.
        Assert.Contains("FillAlpha", export, StringComparison.Ordinal);
        Assert.Contains("FillRectangle", export, StringComparison.Ordinal);
        Assert.Contains("box.Filled", code, StringComparison.Ordinal);
        Assert.Contains("box.Filled", export, StringComparison.Ordinal);
    }

    [Fact]
    public void The_source_is_decoded_once_and_never_read_back_from_the_file()
    {
        var code = ReadFixture("AnnotationWindow.xaml.cs.txt");

        // Saving moves a temporary file over the capture. The old surface had the same
        // path open twice — as the Image source and again at save time — while doing it.
        Assert.Contains("File.ReadAllBytes", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new BitmapImage(new Uri", code, StringComparison.Ordinal);
        Assert.DoesNotContain("File.OpenRead", code, StringComparison.Ordinal);
        // A malformed image makes GDI+ throw a bare OutOfMemoryException, which reads as
        // a resource problem and gets misdiagnosed.
        Assert.Contains("could not be opened as an image", code, StringComparison.Ordinal);
    }

    [Fact]
    public void No_state_colour_is_offered_as_ink()
    {
        var xaml = ReadFixture("AnnotationWindow.xaml");

        // The first four swatches were the Ready and Recording tokens. This surface now
        // spends green on the active tool, so a green mark would be indistinguishable
        // from CursorPocket's own state. The swatches are built from AnnotationPalette,
        // which excludes every state colour.
        Assert.DoesNotContain("45E08C", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("FF5F6B", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_toolbar_degrades_its_teaching_and_never_its_capability()
    {
        var code = ReadFixture("AnnotationWindow.xaml.cs.txt");

        var apply = Slice(code, "private void ApplyToolbarWidth", "// ------");
        // At full width every tool shows its key. As the window narrows the keys go, then
        // a label shortens — but no tool is hidden, there is no overflow menu, and the
        // toolbar never scrolls.
        Assert.Contains("Visibility.Collapsed", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled = false", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("Children.Remove", apply, StringComparison.Ordinal);
    }

    [Fact]
    public void Mark_sizes_come_from_the_image_rather_than_a_constant()
    {
        var code = ReadFixture("AnnotationWindow.xaml.cs.txt");

        // 32 px text is most of a small region capture and nearly invisible on a 4K
        // shot. Every weight and size is derived from the image being annotated.
        Assert.Contains("AnnotationMetrics.StrokeWidth", code, StringComparison.Ordinal);
        Assert.Contains("AnnotationMetrics.TextSize", code, StringComparison.Ordinal);
        Assert.Contains("AnnotationMetrics.HighlightWidth", code, StringComparison.Ordinal);
        Assert.DoesNotContain("FontSize = 32", code, StringComparison.Ordinal);
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
