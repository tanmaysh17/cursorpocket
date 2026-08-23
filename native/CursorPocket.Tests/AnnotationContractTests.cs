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
    public void Every_declared_tool_key_is_actually_handled()
    {
        var xaml = ReadFixture("AnnotationWindow.xaml");
        var code = ReadFixture("AnnotationWindow.xaml.cs.txt");

        // Declaring the accelerator in XAML and mapping the key in the handler are two
        // separate edits, and forgetting the second one is silent: the accelerator fires,
        // the switch returns null, and the key simply does nothing. Four tools shipped
        // that way once. Read the declarations and insist each has a mapping.
        var declared = System.Text.RegularExpressions.Regex
            .Matches(xaml, "Key=\"(?<key>\\w+)\" Invoked=\"ToolAccelerator_Invoked\"")
            .Select(match => match.Groups["key"].Value)
            .ToArray();

        Assert.NotEmpty(declared);
        foreach (var key in declared)
        {
            Assert.Contains($"VirtualKey.{key} =>", code, StringComparison.Ordinal);
        }
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
        // same alpha — the preview reads the constant off the exporter rather than
        // carrying its own copy.
        Assert.Contains("FillAlpha", export, StringComparison.Ordinal);
        Assert.Contains("AnnotationExport.FillAlpha", code, StringComparison.Ordinal);
        Assert.Contains("box.Filled", code, StringComparison.Ordinal);
        Assert.Contains("box.Filled", export, StringComparison.Ordinal);
        // A rounded box has to round the same way whether it is being filled or stroked,
        // which is why both come off one path.
        Assert.Contains("box.CornerRadius", code, StringComparison.Ordinal);
        Assert.Contains("box.CornerRadius", export, StringComparison.Ordinal);
    }

    [Fact]
    public void A_highlighter_composites_once_so_it_cannot_darken_itself()
    {
        var code = ReadFixture("AnnotationWindow.xaml.cs.txt");
        var export = ReadFixture("AnnotationExport.cs.txt");

        // Stroking a translucent brush or pen directly makes a single stroke darken
        // itself everywhere it crosses over, which a highlighter never does on paper.
        // The preview strokes opaque and sets the element's own opacity; the exporter
        // strokes opaque into a layer and composites that once through a colour matrix.
        Assert.Contains("WithAlpha(255)", code, StringComparison.Ordinal);
        Assert.Contains("Opacity = stroke.Highlight", code, StringComparison.Ordinal);
        Assert.Contains("WithAlpha(255)", export, StringComparison.Ordinal);
        Assert.Contains("ColorMatrix", export, StringComparison.Ordinal);
    }

    [Fact]
    public void Redaction_defaults_to_the_only_mode_that_is_not_recoverable()
    {
        var code = ReadFixture("AnnotationWindow.xaml.cs.txt");

        // Pixelation and blur both derive their output from the pixels underneath, so for
        // short text they are only partially destructive. Solid replaces the pixels.
        Assert.Contains("RedactStyle _redactStyle = RedactStyle.Solid", code, StringComparison.Ordinal);
        // ...and the status strip says which of the two the user is currently getting.
        Assert.Contains("nothing recoverable", code, StringComparison.Ordinal);
        Assert.Contains("partly recoverable", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Pixel_marks_take_their_pixels_from_one_shared_sampler()
    {
        var code = ReadFixture("AnnotationWindow.xaml.cs.txt");
        var export = ReadFixture("AnnotationExport.cs.txt");
        var patches = ReadFixture("AnnotationPatches.cs.txt");

        // Redaction and the loupe read the screenshot rather than drawing over it, so
        // they are the one place where preview and export could disagree on pixels
        // instead of geometry. Both call the same sampler.
        Assert.Contains("AnnotationPatches.Redact", code, StringComparison.Ordinal);
        Assert.Contains("AnnotationPatches.Redact", export, StringComparison.Ordinal);
        Assert.Contains("AnnotationPatches.Loupe", code, StringComparison.Ordinal);
        Assert.Contains("AnnotationPatches.Loupe", export, StringComparison.Ordinal);

        // Always sampled from the untouched source, so a redaction cannot be weakened by
        // a mark drawn underneath it and re-rendering stays idempotent.
        Assert.Contains("ImageLockMode.ReadOnly", patches, StringComparison.Ordinal);
        Assert.Contains("internal static Rectangle Snap", patches, StringComparison.Ordinal);
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
    public void The_two_toolbar_clusters_cannot_overlap()
    {
        var xaml = ReadFixture("AnnotationWindow.xaml");
        var row = Slice(xaml, "x:Name=\"ToolbarRow\"", "x:Name=\"SelectTool\"");

        // Both clusters used to sit in one cell, left-aligned and right-aligned. Once the
        // tools outgrew the space they drew straight over the output buttons — a
        // strikethrough through "Discard" — rather than the row adapting. Two columns make
        // that structurally impossible.
        Assert.Contains("<Grid.ColumnDefinitions>", row, StringComparison.Ordinal);
        Assert.Equal(2, Occurrences(row, "<ColumnDefinition"));
        Assert.Contains("x:Name=\"ToolbarRight\"", xaml, StringComparison.Ordinal);
        var right = Slice(xaml, "x:Name=\"ToolbarRight\"", "x:Name=\"PinButton\"");
        Assert.Contains("Grid.Column=\"1\"", right, StringComparison.Ordinal);
    }

    [Fact]
    public void The_editor_uses_a_tool_rail_and_context_bar_without_scrolling()
    {
        var xaml = ReadFixture("AnnotationWindow.xaml");
        var code = ReadFixture("AnnotationWindow.xaml.cs.txt");

        Assert.Contains("x:Name=\"ToolRail\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PropertyPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ToolContextHost\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"OutputHost\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MoreToolsButton", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollViewer", xaml, StringComparison.Ordinal);
        Assert.Contains("AnnotationToolCatalog.Get", code, StringComparison.Ordinal);

        // No width state machine, and nothing collapses a key.
        Assert.DoesNotContain("CompactToolbarWidth", code, StringComparison.Ordinal);
        Assert.DoesNotContain("TightToolbarWidth", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyToolbarWidth", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_tool_button_carries_its_key_on_its_face()
    {
        var xaml = ReadFixture("AnnotationWindow.xaml");

        // The engraved letter is the teaching mechanism: it is why there is no hover
        // submenu and no legend anyone has to read. Every tool button has one, always.
        var toolButtons = Occurrences(xaml, "Style=\"{StaticResource PocketToolButton}\"");
        var engraved = Occurrences(xaml, "Style=\"{StaticResource ToolKeyText}\"");

        Assert.True(toolButtons > 10, $"only {toolButtons} tool buttons found — the regex has drifted");
        Assert.Equal(toolButtons, engraved);
    }

    [Fact]
    public void The_destructive_action_is_not_adjacent_to_a_benign_one()
    {
        var xaml = ReadFixture("AnnotationWindow.xaml");

        // Discard shipped between Keep original and Save, which is as adjacent as it gets.
        // It now lives at the far end of the status strip — the opposite corner of the
        // surface from the primary action.
        var outputs = Slice(xaml, "x:Name=\"ToolbarRight\"", "</Grid>");
        Assert.DoesNotContain("DiscardButton", outputs, StringComparison.Ordinal);
        Assert.Contains("PocketDangerButton", xaml, StringComparison.Ordinal);

        // Save is still the one primary action, and still on its own.
        Assert.Equal(1, Occurrences(xaml, "PocketPrimaryButton"));
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
    [Fact]
    public void Ocr_uses_the_engine_built_into_Windows_and_nothing_else()
    {
        var service = ReadFixture("OcrTextService.cs.txt");

        // Windows has shipped an OCR engine since 1809 and it reaches us through the same
        // WinRT projections the camera pipeline already uses. No Tesseract sidecar, no
        // model download, no network.
        Assert.Contains("OcrEngine.TryCreateFromUserProfileLanguages()", service, StringComparison.Ordinal);
        // No sidecar process and no network. Asserted on what the code can actually do
        // rather than on the word "tesseract", which the comment above the class uses to
        // explain why we do not need it.
        Assert.DoesNotContain("Process.Start", service, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", service, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Net", service, StringComparison.Ordinal);

        // Read off the engine, never hardcoded: it is a property for a reason and has
        // differed between Windows versions.
        Assert.Contains("OcrEngine.MaxImageDimension", service, StringComparison.Ordinal);
        Assert.DoesNotContain("8192", service, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_language_pack_disables_one_button_rather_than_crashing()
    {
        var service = ReadFixture("OcrTextService.cs.txt");
        var code = ReadFixture("AnnotationWindow.xaml.cs.txt");

        // TryCreate returning null, following the segmentation model's precedent: OCR is
        // a language pack the user may simply not have installed.
        Assert.Contains("internal static OcrTextService? TryCreate()", service, StringComparison.Ordinal);
        Assert.Contains("return null", service, StringComparison.Ordinal);
        Assert.Contains("ReadTextTool.IsEnabled = false", code, StringComparison.Ordinal);
        // ...and says why, without offering to install anything, because there is no
        // network in this app.
        Assert.Contains("language pack", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Download", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Recognised_text_never_reaches_the_clipboard_unasked()
    {
        var service = ReadFixture("OcrTextService.cs.txt");
        var code = ReadFixture("AnnotationWindow.xaml.cs.txt");

        // A screenshot is on the clipboard from the moment it is taken, and the design
        // gate says so. Silently replacing that image with text would break a promise the
        // app makes everywhere else, so the recognition path must not touch the clipboard
        // at all — only the explicit Copy action may.
        Assert.DoesNotContain("Clipboard", service, StringComparison.Ordinal);

        var reading = Slice(code, "private async Task ReadTextAsync", "private void CloseOcr_Click");
        Assert.DoesNotContain("Clipboard", reading, StringComparison.Ordinal);

        // The explicit path exists, on its own key rather than sharing Ctrl+C.
        Assert.Contains("CopyTextAccelerator_Invoked", code, StringComparison.Ordinal);
        Assert.Contains("Modifiers=\"Control,Shift\"", ReadFixture("AnnotationWindow.xaml"), StringComparison.Ordinal);
    }

    [Fact]
    public void Recognised_word_boxes_are_mapped_back_out_of_the_engines_coordinates()
    {
        var service = ReadFixture("OcrTextService.cs.txt");

        // The engine is handed a resampled copy and answers in that copy's space, so every
        // box has to come back through the same factor plus the region's own origin. Get
        // it wrong and the text is right while every highlight sits in the wrong place.
        Assert.Contains("OcrScaling.ScaleFor", service, StringComparison.Ordinal);
        Assert.Contains("OcrScaling.ToSource", service, StringComparison.Ordinal);
        Assert.Contains("OcrScaling.CannotBeRead", service, StringComparison.Ordinal);
    }

    [Fact]
    public void The_receipt_uses_click_actions_and_only_escape_to_dismiss()
    {
        var code = ReadFixture("ReceiptWindow.xaml.cs.txt");
        var xaml = ReadFixture("ReceiptWindow.xaml");

        Assert.DoesNotContain("PaletteHotkeyService", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ReceiptKeysHint", xaml, StringComparison.Ordinal);
        Assert.Contains("VirtualKey.Escape", code, StringComparison.Ordinal);
        Assert.Contains("Content=\"Show in folder\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"36\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_way_into_the_editor_goes_through_one_place()
    {
        var main = ReadFixture("MainWindow.xaml.cs.txt");

        // Four entry points now: a fresh capture, the Library, a receipt, and an image
        // CursorPocket never took. One construction site, so the clipboard re-copy, the
        // edited-copy path, and Discard cannot be wired on some paths and not others.
        Assert.Equal(1, Occurrences(main, "new AnnotationWindow("));
        Assert.Contains("public void AnnotateExisting(", main, StringComparison.Ordinal);
        Assert.Contains("public async Task AnnotateClipboardAsync()", main, StringComparison.Ordinal);
        Assert.Contains("public async Task AnnotateFileAsync(", main, StringComparison.Ordinal);
    }

    [Fact]
    public void A_fresh_capture_is_forced_forward_and_an_existing_one_is_merely_activated()
    {
        var main = ReadFixture("MainWindow.xaml.cs.txt");
        var open = Slice(main, "private void OpenEditor(", "private async Task RegisterEditedCopyAsync");

        // A transient surface has just hidden itself on the capture path, so the source app
        // still owns the foreground lock and Activate() loses that race — this is the
        // regression that once left the editor minimized. An editor opened from the Library
        // comes from a window that already has focus, where forcing is the wrong tool.
        Assert.Contains("AnnotationOrigin.FreshCapture", open, StringComparison.Ordinal);
        Assert.Contains("WindowPlacement.ForceForeground(editor)", open, StringComparison.Ordinal);
        Assert.Contains("editor.Activate()", open, StringComparison.Ordinal);
    }

    [Fact]
    public void Discarding_goes_to_the_recycle_bin_and_out_of_the_index()
    {
        var main = ReadFixture("MainWindow.xaml.cs.txt");
        var discard = Slice(main, "private async Task DiscardCaptureAsync", "private void SelectRegion");

        // The file is written before the editor opens, so this is the only way to undo
        // having taken the shot at all. Never a hard delete.
        Assert.Contains("RecycleOption.SendToRecycleBin", discard, StringComparison.Ordinal);
        Assert.Contains("RemoveFromIndexAsync", discard, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Delete", discard, StringComparison.Ordinal);
    }

    [Fact]
    public void A_geometry_change_writes_a_new_capture_rather_than_repairing_the_index()
    {
        var main = ReadFixture("MainWindow.xaml.cs.txt");
        var code = ReadFixture("AnnotationWindow.xaml.cs.txt");

        Assert.Contains("SaveTarget.For(", code, StringComparison.Ordinal);
        Assert.Contains("RegisterEditedCopyAsync", main, StringComparison.Ordinal);
        // The new record carries its own dimensions, so captures.jsonl stays append-only
        // and no path has to go back and rewrite a stale line.
        Assert.Contains("RegisterExistingAsync", main, StringComparison.Ordinal);
        Assert.DoesNotContain("RewriteIndex", main, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pin_never_takes_the_global_escape_lease()
    {
        var code = ReadFixture("PinnedCaptureWindow.xaml.cs.txt");
        var xaml = ReadFixture("PinnedCaptureWindow.xaml");

        // The escape service is a lease stack. A pin can sit on screen for hours while the
        // user works elsewhere, so holding the topmost lease would steal Escape from every
        // other application — including a recording, where Escape means stop and save. A
        // pin does not own the user's attention, so its Escape is a page accelerator.
        Assert.DoesNotContain("EscapeHotkey", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PaletteHotkeyService", code, StringComparison.Ordinal);
        Assert.Contains("Key=\"Escape\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pin_is_visible_in_a_capture_because_it_is_visible_on_screen()
    {
        var code = ReadFixture("PinnedCaptureWindow.xaml.cs.txt");

        // A pin exists to be looked at, so it must appear in a screenshot or recording
        // taken while it is up. Visible equals captured; a user who does not want it in the
        // shot closes it.
        Assert.Contains("excludeFromCapture: false", code, StringComparison.Ordinal);
        Assert.DoesNotContain("WdaExcludeFromCapture", code, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pin_is_dragged_by_pointer_tracking_and_carries_no_window_region()
    {
        var code = ReadFixture("PinnedCaptureWindow.xaml.cs.txt");

        // WinUI consumes the messages Windows' modal move loop needs, which is why
        // WindowPlacement has no such helper. And a window region takes the window off
        // DWM's fast path, which is exactly what makes a dragged window lag.
        Assert.Contains("Root.CapturePointer", code, StringComparison.Ordinal);
        Assert.Contains("WindowPlacement.MoveTo", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SetWindowRgn", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ClipToRoundedRegion", code, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pin_only_ever_appears_because_the_user_asked_for_it()
    {
        var main = ReadFixture("MainWindow.xaml.cs.txt");

        // Explicit action only, and never restored: a window that reappears after a reboot
        // with no explanation is the unexplained floating widget the anti-references warn
        // against. The Library holds the durable copy.
        Assert.Contains("editor.PinExportRequested", main, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(main, "PinnedCaptureWindow.TryShow"));
        Assert.DoesNotContain("RestorePins", main, StringComparison.Ordinal);
        Assert.DoesNotContain("pin_geometry", main, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pin_offers_all_three_payloads_when_dragged_out()
    {
        var code = ReadFixture("PinnedCaptureWindow.xaml.cs.txt");
        var xaml = ReadFixture("PinnedCaptureWindow.xaml");

        // Different targets want different things: Explorer takes the storage item, an
        // image editor takes the bitmap, and the preview is what makes the gesture legible.
        Assert.Contains("SetStorageItems", code, StringComparison.Ordinal);
        Assert.Contains("SetBitmap", code, StringComparison.Ordinal);
        Assert.Contains("DragUI.SetContentFromBitmapImage", code, StringComparison.Ordinal);
        // On its own element, because CanDrag competes with the window-move drag.
        Assert.Contains("x:Name=\"DragHandle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CanDrag=\"True\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void A_new_top_level_window_is_added_to_the_publish_verification_list()
    {
        var build = ReadFixture("build-native.ps1.txt");

        // An unpackaged WinUI publish can omit compiled XAML, and the app then dies with
        // XamlParseException only in the installed build. The staging step throws if a
        // listed resource is missing, so the list has to name every top-level XAML.
        Assert.Contains("PinnedCaptureWindow.xbf", build, StringComparison.Ordinal);
        Assert.Contains("AnnotationWindow.xbf", build, StringComparison.Ordinal);
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
