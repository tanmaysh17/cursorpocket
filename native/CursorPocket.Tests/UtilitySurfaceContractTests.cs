namespace CursorPocket.Tests;

public sealed class UtilitySurfaceContractTests
{
    [Fact]
    public void Command_mode_is_a_small_glass_panel_that_holds_one_position()
    {
        var code = ReadFixture("CommandPaletteWindow.xaml.cs.txt");
        var xaml = ReadFixture("CommandPaletteWindow.xaml");
        var main = ReadFixture("MainWindow.xaml.cs.txt");
        var theme = ReadFixture("ThemeCoordinator.cs.txt");

        Assert.Contains("RegularWidth = 304", code, StringComparison.Ordinal);
        Assert.Contains("ShortWidth = 520", code, StringComparison.Ordinal);
        // Acrylic blurs the live desktop, so the frozen full-screen snapshot and its
        // per-move realignment are gone along with the keep-away behaviour.
        Assert.Contains("new PocketAcrylicBackdrop(", theme, StringComparison.Ordinal);
        Assert.Contains("DesktopAcrylicController", theme, StringComparison.Ordinal);
        Assert.DoesNotContain("Window.SystemBackdrop", xaml, StringComparison.Ordinal);
        // Desktop Acrylic is the command window's desktop-sampling material. A
        // full-window AcrylicBrush only blurs XAML content in WinUI 3 and masks the
        // system backdrop with a nearly flat tint when there is no scene behind it.
        Assert.Contains("Background=\"Transparent\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PocketGlassPanel", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("BackdropImage", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DesktopSnapshot.Capture", code, StringComparison.Ordinal);
        // The panel only ever moves because the user dragged it — never on its own.
        Assert.DoesNotContain("NotifyPointerMoved", code, StringComparison.Ordinal);
        Assert.DoesNotContain("_palette.NotifyPointerMoved", main, StringComparison.Ordinal);
        Assert.DoesNotContain("PalettePlacementPolicy", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollViewer", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PulseStoryboard", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PulseRing", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PulseStoryboard", code, StringComparison.Ordinal);
        Assert.Contains("CaptureActionCatalog.Primary", code, StringComparison.Ordinal);
        Assert.Contains("TransientWindowLayoutPolicy.Resolve", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Acrylic_is_owned_once_and_every_surface_has_an_explicit_material_role()
    {
        var app = ReadFixture("App.xaml");
        var theme = ReadFixture("ThemeCoordinator.cs.txt");
        var main = ReadFixture("MainWindow.xaml");
        var command = ReadFixture("CommandPaletteWindow.xaml");
        var preflight = ReadFixture("VideoPreflightWindow.xaml");
        var annotation = ReadFixture("AnnotationWindow.xaml");

        Assert.Contains("<AcrylicBrush x:Key=\"PocketGlassPanel\"", app, StringComparison.Ordinal);
        Assert.Contains("TintOpacity=\"0.48\"", app, StringComparison.Ordinal);
        Assert.Contains("TintOpacity=\"0.54\"", app, StringComparison.Ordinal);
        Assert.Contains("TintOpacity=\"0.62\"", app, StringComparison.Ordinal);
        Assert.Contains("TintOpacity=\"0.68\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"PocketGlassDense\"", app, StringComparison.Ordinal);
        Assert.Contains("TintOpacity=\"0.84\"", app, StringComparison.Ordinal);
        Assert.Contains("#8F101815", app, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#A8F8FCFA", app, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PocketGlassRim", app, StringComparison.Ordinal);
        Assert.Contains("PocketGlassTopEdge", app, StringComparison.Ordinal);
        Assert.Contains("<SolidColorBrush x:Key=\"PocketGlassPanel\" Color=\"{ThemeResource SystemColorWindowColor}\"", app, StringComparison.Ordinal);

        Assert.Contains("registration.Window.SystemBackdrop = new PocketAcrylicBackdrop(", theme, StringComparison.Ordinal);
        Assert.Contains("registration.Window.SystemBackdrop is PocketAcrylicBackdrop backdrop", theme, StringComparison.Ordinal);
        Assert.Contains("backdrop.Update(tint, fallback, tintOpacity, luminosityOpacity)", theme, StringComparison.Ordinal);
        Assert.Contains("new DesktopAcrylicBackdrop()", theme, StringComparison.Ordinal);
        Assert.Contains("SurfaceRole.Pin or SurfaceRole.CaptureOverlay", theme, StringComparison.Ordinal);
        Assert.DoesNotContain("TransparencyAllowed", theme, StringComparison.Ordinal);
        Assert.DoesNotContain("MicaBackdrop", theme, StringComparison.Ordinal);
        foreach (var xaml in new[] { main, command, preflight, annotation })
        {
            Assert.DoesNotContain("Window.SystemBackdrop", xaml, StringComparison.Ordinal);
        }

        Assert.Contains("SurfaceRole.Persistent", ReadFixture("MainWindow.xaml.cs.txt"), StringComparison.Ordinal);
        Assert.Contains("SurfaceRole.Workspace", ReadFixture("AnnotationWindow.xaml.cs.txt"), StringComparison.Ordinal);
        Assert.Contains("SurfaceRole.Transient", ReadFixture("CommandPaletteWindow.xaml.cs.txt"), StringComparison.Ordinal);
        Assert.Contains("SurfaceRole.Transient", ReadFixture("VideoPreflightWindow.xaml.cs.txt"), StringComparison.Ordinal);
        Assert.Contains("SurfaceRole.Hud", ReadFixture("RecordingHudWindow.xaml.cs.txt"), StringComparison.Ordinal);
        Assert.Contains("SurfaceRole.Receipt", ReadFixture("ReceiptWindow.xaml.cs.txt"), StringComparison.Ordinal);
        Assert.Contains("SurfaceRole.Pin", ReadFixture("PinnedCaptureWindow.xaml.cs.txt"), StringComparison.Ordinal);
        Assert.Contains("SurfaceRole.CaptureOverlay", ReadFixture("RegionSelectorWindow.xaml.cs.txt"), StringComparison.Ordinal);
    }

    [Fact]
    public void Glass_transparency_is_visible_persisted_and_applied_live()
    {
        var page = ReadFixture("MainPage.xaml");
        var viewModel = ReadFixture("MainPageViewModel.cs.txt");
        var app = ReadFixture("App.xaml.cs.txt");
        var main = ReadFixture("MainWindow.xaml.cs.txt");
        var theme = ReadFixture("ThemeCoordinator.cs.txt");

        Assert.Contains("AutomationProperties.Name=\"Glass transparency\"", page, StringComparison.Ordinal);
        Assert.Contains("ViewModel.GlassTransparencyIndex", page, StringComparison.Ordinal);
        Assert.Contains("Content=\"Very transparent\"", page, StringComparison.Ordinal);
        Assert.Contains("Content=\"More transparent\"", page, StringComparison.Ordinal);
        Assert.Contains("Content=\"Balanced\"", page, StringComparison.Ordinal);
        Assert.Contains("Content=\"More solid\"", page, StringComparison.Ordinal);
        Assert.Contains("Content=\"Very solid\"", page, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LibraryListPane\"", page, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DetailPane\"", page, StringComparison.Ordinal);

        Assert.Contains("nameof(GlassTransparencyIndex)", viewModel, StringComparison.Ordinal);
        Assert.Contains("App.Theme.SetGlassTransparency", viewModel, StringComparison.Ordinal);
        Assert.Contains("GlassTransparency = GlassTransparencyIndex switch", viewModel, StringComparison.Ordinal);
        Assert.Contains("new ThemeCoordinator(Services.Settings.ThemeMode, Services.Settings.GlassTransparency)", app, StringComparison.Ordinal);
        Assert.Contains("SetGlassTransparency(settings.GlassTransparency)", main, StringComparison.Ordinal);

        Assert.Contains("panel.TintOpacity = profile.PanelTint", theme, StringComparison.Ordinal);
        Assert.Contains("panel.TintLuminosityOpacity = profile.PanelLuminosity", theme, StringComparison.Ordinal);
        Assert.Contains("raised.TintOpacity = profile.RaisedTint", theme, StringComparison.Ordinal);
        Assert.Contains("raised.TintLuminosityOpacity = profile.RaisedLuminosity", theme, StringComparison.Ordinal);
        Assert.Contains("SetBrushAlpha(resources, \"PocketSurface\"", theme, StringComparison.Ordinal);
        Assert.Contains("GlassTransparencyLevel.VeryClear", theme, StringComparison.Ordinal);
        Assert.Contains("GlassTransparencyLevel.Clear", theme, StringComparison.Ordinal);
        Assert.Contains("GlassTransparencyLevel.Solid", theme, StringComparison.Ordinal);
        Assert.Contains("GlassTransparencyLevel.VerySolid", theme, StringComparison.Ordinal);
        Assert.Contains("new(0.06, 0.18", theme, StringComparison.Ordinal);
        Assert.Contains("new(0.92, 0.98", theme, StringComparison.Ordinal);
        Assert.Contains("_controller.TintOpacity = _tintOpacity", theme, StringComparison.Ordinal);
        Assert.Contains("_controller.LuminosityOpacity = _luminosityOpacity", theme, StringComparison.Ordinal);
        Assert.Contains("GetDefaultSystemBackdropConfiguration", theme, StringComparison.Ordinal);
        Assert.Contains("controller.AddSystemBackdropTarget(connectedTarget)", theme, StringComparison.Ordinal);
        Assert.Contains("var isHud = registration.Role == SurfaceRole.Hud", theme, StringComparison.Ordinal);
        Assert.Contains("var tintOpacity = isHud ? 0.84 : profile.PanelTint", theme, StringComparison.Ordinal);
        Assert.Contains("LibraryListPane.Background = App.Theme.GlassBrush()", ReadFixture("MainPage.xaml.cs.txt"), StringComparison.Ordinal);
        Assert.Contains("DetailPane.Background = App.Theme.GlassBrush()", ReadFixture("MainPage.xaml.cs.txt"), StringComparison.Ordinal);
        var apply = Section(theme, "private void ApplyGlassTransparency", "private static GlassProfile ProfileFor");
        Assert.DoesNotContain("PocketGlassDense", apply, StringComparison.Ordinal);
        Assert.DoesNotContain("HighContrast", apply, StringComparison.Ordinal);
    }

    [Fact]
    public void Library_copy_places_the_capture_itself_on_the_clipboard()
    {
        var xaml = ReadFixture("MainPage.xaml");
        var code = ReadFixture("MainPage.xaml.cs.txt");

        Assert.Contains("Invoked=\"CopyCaptureAccelerator_Invoked\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"CopyCapture_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.ToolTip=\"Copy capture · Ctrl+C\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SetStorageItems([file])", code, StringComparison.Ordinal);
        Assert.Contains("CaptureKind.Screenshot", code, StringComparison.Ordinal);
        Assert.Contains("SetBitmap", code, StringComparison.Ordinal);
        Assert.Contains("Clipboard.Flush()", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SetText(ViewModel.SelectedItem.AbsolutePath)", code, StringComparison.Ordinal);
        Assert.Contains("Capture copied", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Circle_gesture_sensitivity_is_visible_persisted_and_applied_live()
    {
        var page = ReadFixture("MainPage.xaml");
        var main = ReadFixture("MainWindow.xaml.cs.txt");
        var mouse = ReadFixture("MouseActivityService.cs.txt");

        Assert.Contains("AutomationProperties.Name=\"Circle gesture sensitivity\"", page, StringComparison.Ordinal);
        Assert.Contains("ViewModel.MouseGestureSensitivityIndex", page, StringComparison.Ordinal);
        Assert.Contains("Content=\"Low\"", page, StringComparison.Ordinal);
        Assert.Contains("Content=\"Balanced\"", page, StringComparison.Ordinal);
        Assert.Contains("Content=\"High\"", page, StringComparison.Ordinal);
        Assert.Contains("IsEnabled=\"{x:Bind ViewModel.MouseGestureEnabled, Mode=OneWay}\"", page, StringComparison.Ordinal);
        Assert.Contains("GestureSensitivity = settings.MouseGestureSensitivity", main, StringComparison.Ordinal);
        Assert.Contains("GestureSensitivity = App.Services.Settings.MouseGestureSensitivity", main, StringComparison.Ordinal);
        Assert.Contains("Volatile.Read(ref _gestureSensitivity)", mouse, StringComparison.Ordinal);
        Assert.Contains("_gesture.Feed", mouse, StringComparison.Ordinal);
    }

    [Fact]
    public void Command_mode_can_be_dragged_anywhere_and_reopens_where_it_was_left()
    {
        var code = ReadFixture("CommandPaletteWindow.xaml.cs.txt");
        var xaml = ReadFixture("CommandPaletteWindow.xaml");
        var placement = ReadFixture("WindowPlacement.cs.txt");
        var services = ReadFixture("AppServices.cs.txt");

        // The whole panel drags, not just a title strip.
        Assert.Contains("PointerPressed=\"Root_PointerPressed\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DoubleTapped=\"Root_DoubleTapped\"", xaml, StringComparison.Ordinal);
        // ...but a press on any button stays a click, including the keycaps, which are
        // Buttons nested inside a Button's content.
        Assert.Contains("IsOverButton", code, StringComparison.Ordinal);
        Assert.Contains("node is ButtonBase", code, StringComparison.Ordinal);
        // The drag is tracked from pointer events, not handed to Windows' modal move
        // loop: WinUI consumes the messages that loop needs, so the window never moved.
        Assert.Contains("Root.CapturePointer", code, StringComparison.Ordinal);
        Assert.Contains("PointerReleased=\"Root_PointerReleased\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PointerCaptureLost=\"Root_PointerCaptureLost\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("WmNcLButtonDown", placement, StringComparison.Ordinal);
        Assert.Contains("SwpNoZOrder", placement, StringComparison.Ordinal);
        // A region clip would take the window off DWM's fast path and make the drag lag.
        Assert.DoesNotContain("ClipToRoundedPixelRegion", code, StringComparison.Ordinal);
        // Stored as fractions of the free space, so a remembered position cannot come
        // back off screen on a different display.
        Assert.Contains("CommandPanelPlacement.Resolve", code, StringComparison.Ordinal);
        Assert.Contains("CommandPanelPlacement.AnchorFor", code, StringComparison.Ordinal);
        Assert.Contains("UpdateCommandPanelAnchorAsync", services, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_capture_can_be_acted_on_without_a_mouse()
    {
        var receipt = ReadFixture("ReceiptWindow.xaml.cs.txt");
        var receiptXaml = ReadFixture("ReceiptWindow.xaml");
        var page = ReadFixture("MainPage.xaml.cs.txt");
        var pageXaml = ReadFixture("MainPage.xaml");
        var hotkeys = ReadFixture("PaletteHotkeyService.cs.txt");

        Assert.DoesNotContain("PaletteHotkeyService", receipt, StringComparison.Ordinal);
        Assert.Contains("VirtualKey.Escape", receipt, StringComparison.Ordinal);
        Assert.Contains("Content=\"Open\"", receiptXaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Show in folder\"", receiptXaml, StringComparison.Ordinal);

        // The Library is driven by page accelerators, which cannot affect other apps.
        Assert.Contains("OpenAccelerator_Invoked", pageXaml, StringComparison.Ordinal);
        Assert.Contains("PlayAccelerator_Invoked", pageXaml, StringComparison.Ordinal);
        Assert.Contains("DeleteAccelerator_Invoked", pageXaml, StringComparison.Ordinal);
        Assert.Contains("RevealAccelerator_Invoked", pageXaml, StringComparison.Ordinal);
        // ...and they stand down while a text box has focus, or Settings would lose
        // Space and Ctrl+A to the Library.
        Assert.Contains("is not TextBox", page, StringComparison.Ordinal);
        Assert.Contains("LibraryKeysActive()", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_surfaces_are_forced_to_the_foreground_rather_than_merely_activated()
    {
        var main = ReadFixture("MainWindow.xaml.cs.txt");
        var placement = ReadFixture("WindowPlacement.cs.txt");

        // Command mode hides itself before a capture surface opens, handing the
        // foreground to the source app; Activate() alone loses that race and the
        // annotation window stays minimized.
        Assert.Contains("WindowPlacement.ForceForeground(editor)", main, StringComparison.Ordinal);
        Assert.Contains("editor.AppWindow.Show(true)", main, StringComparison.Ordinal);
        Assert.Contains("AttachThreadInput", placement, StringComparison.Ordinal);
        Assert.Contains("IsIconic(handle) ? NativeMethods.SwRestore", placement, StringComparison.Ordinal);
    }

    [Fact]
    public void Region_selection_captures_physical_pixels_not_layout_coordinates()
    {
        var code = ReadFixture("RegionSelectorWindow.xaml.cs.txt");

        // Screen capture is in physical pixels. Taking the corners from the cursor
        // rather than from XAML positions is what keeps a scaled display from losing
        // the right and bottom of every region.
        Assert.Contains("WindowPlacement.PointerPosition()", code, StringComparison.Ordinal);
        Assert.Contains("RegionSelection.FromCorners", code, StringComparison.Ordinal);
        Assert.Contains("RegionSelection.IsUsable", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Math.Min(_start.X, end.X)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Region_selector_keeps_the_desktop_visible_while_selecting()
    {
        var xaml = ReadFixture("RegionSelectorWindow.xaml");
        var snapshot = ReadFixture("DesktopSnapshot.cs.txt");

        // Region selection is now the only surface using the frozen desktop, so its
        // fast lossless snapshot path is asserted here rather than on the palette.
        Assert.Contains("x:Name=\"BackdropImage\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ImageFormat.Bmp", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("IBufferByteAccess", snapshot, StringComparison.Ordinal);
    }

    [Fact]
    public void Cursor_companion_uses_per_pixel_alpha_without_stealing_focus()
    {
        var code = ReadFixture("NativeCompanionWindow.cs.txt");

        Assert.Contains("UpdateLayeredWindow", code, StringComparison.Ordinal);
        Assert.Contains("WsExNoActivate", code, StringComparison.Ordinal);
        Assert.Contains("_x = x + 10", code, StringComparison.Ordinal);
        Assert.Contains("_y = y + 12", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Command_palette_restores_capture_sources_but_keeps_library_in_front()
    {
        var code = ReadFixture("CommandPaletteWindow.xaml.cs.txt");

        Assert.Contains("App.Services.Context.RestoreFocus(SourceWindow)", code, StringComparison.Ordinal);
        Assert.Contains("_restoreSourceOnClose = command != CaptureActionId.Library", code, StringComparison.Ordinal);
        Assert.Contains("_openLibraryAfterPaletteCloses", ReadFixture("MainWindow.xaml.cs.txt"), StringComparison.Ordinal);
        Assert.Contains("excludeFromCapture: false", code, StringComparison.Ordinal);
        Assert.Contains("FocusCommandSurface", code, StringComparison.Ordinal);
        Assert.Contains("CommandAccelerator_Invoked", ReadFixture("CommandPaletteWindow.xaml"), StringComparison.Ordinal);
        var globalKeys = ReadFixture("PaletteHotkeyService.cs.txt");
        Assert.Contains("RegisterHotKey", globalKeys, StringComparison.Ordinal);
        Assert.Contains("ModNoRepeat", globalKeys, StringComparison.Ordinal);
    }

    [Fact]
    public void Video_preflight_does_not_start_before_readiness()
    {
        var code = ReadFixture("VideoPreflightWindow.xaml.cs.txt");

        Assert.Contains("!StartButton.IsEnabled", code, StringComparison.Ordinal);
        Assert.DoesNotContain("WdaExcludeFromCapture", code, StringComparison.Ordinal);
    }

    [Fact]
    public void A_display_recording_captures_the_screen_command_mode_was_opened_on()
    {
        var main = ReadFixture("MainWindow.xaml.cs.txt");
        var preflight = ReadFixture("VideoPreflightWindow.xaml.cs.txt");
        var placement = ReadFixture("WindowPlacement.cs.txt");
        var locator = ReadFixture("DisplayOutputLocator.cs.txt");

        // Resolved when the user asks to record. Resolving it at Start instead reads
        // the pointer over the preflight window, which Windows may have opened on
        // another screen.
        Assert.Contains("SnapshotDisplayTarget", main, StringComparison.Ordinal);
        Assert.Contains("DisplayTargetUnderPointer", placement, StringComparison.Ordinal);
        Assert.Contains("_displayBounds", preflight, StringComparison.Ordinal);
        Assert.Contains("_displayOutputIndex", preflight, StringComparison.Ordinal);
        // ddagrab's output_idx is a DXGI ordering, so it must come from DXGI and never
        // from a monitor enumeration index.
        Assert.Contains("EnumOutputs", locator, StringComparison.Ordinal);
        Assert.Contains("DeviceName", locator, StringComparison.Ordinal);
        Assert.DoesNotContain("DisplayIndexUnderPointer", preflight, StringComparison.Ordinal);
        Assert.DoesNotContain("DisplayIndexUnderPointer", placement, StringComparison.Ordinal);
    }

    [Fact]
    public void The_camera_self_view_is_recorded_off_the_screen_and_never_steals_input()
    {
        var code = ReadFixture("CameraSelfViewWindow.xaml.cs.txt");
        var xaml = ReadFixture("CameraSelfViewWindow.xaml");
        var main = ReadFixture("MainWindow.xaml.cs.txt");
        var placement = ReadFixture("WindowPlacement.cs.txt");

        // This is the one surface that must appear in captured media: the webcam
        // reaches the file by being on screen inside the recorded area.
        Assert.Contains("excludeFromCapture: false", code, StringComparison.Ordinal);
        Assert.DoesNotContain("WdaExcludeFromCapture", code, StringComparison.Ordinal);
        // It is dragged to reposition the camera mid recording, so it accepts pointer
        // input — but it must never take activation from the work being demonstrated.
        Assert.Contains("Root.CapturePointer", code, StringComparison.Ordinal);
        Assert.Contains("RestoreFocus(sourceWindow)", code, StringComparison.Ordinal);
        // The clamp is not cosmetic: outside the recorded rectangle the webcam is
        // simply absent from the file.
        Assert.Contains("_captureArea.Right - width", code, StringComparison.Ordinal);
        Assert.Contains("_captureArea.Bottom - height", code, StringComparison.Ordinal);
        Assert.Contains("CameraSelfViewPlacement.Compute", code, StringComparison.Ordinal);
        // The feed remains unaltered while denied-camera fallback follows the theme.
        Assert.Contains("Background=\"{ThemeResource PocketBase}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"Transparent\"", xaml, StringComparison.Ordinal);
        // The camera has to be held before FFmpeg writes frames and released as
        // soon as the recording stops, or the next preflight preview finds it busy.
        Assert.True(main.IndexOf("ShowCameraSelfViewAsync(options)", StringComparison.Ordinal) <
            main.IndexOf("RecordingSession.StartVideoAsync(options)", StringComparison.Ordinal));
        Assert.Contains("DismissCameraSelfView", main, StringComparison.Ordinal);
        Assert.Contains("RecordingState.Finalizing or RecordingState.Idle or RecordingState.Failed", main, StringComparison.Ordinal);
    }

    [Fact]
    public void The_camera_effect_pipeline_only_replaces_the_plain_preview_when_effects_are_on()
    {
        var code = ReadFixture("CameraSelfViewWindow.xaml.cs.txt");
        var xaml = ReadFixture("CameraSelfViewWindow.xaml");
        var renderer = ReadFixture("CameraEffectRenderer.cs.txt");

        // Both render paths exist; with no effects the untouched MediaPlayer
        // path still runs, so an unconfigured user is on the pre-effects code.
        Assert.Contains("x:Name=\"CameraEffectView\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MediaPlayerElement", xaml, StringComparison.Ordinal);
        Assert.Contains("effects.HasAnyEffect", code, StringComparison.Ordinal);
        Assert.Contains("MediaSource.CreateFromMediaFrameSource(source)", code, StringComparison.Ordinal);
        // A frame reader that cannot start must fall back rather than fail.
        Assert.Contains("MediaFrameReaderStartStatus.Success", renderer, StringComparison.Ordinal);
        // Latest-frame-wins keeps a slow machine from accumulating latency.
        Assert.Contains("MediaFrameReaderAcquisitionMode.Realtime", renderer, StringComparison.Ordinal);
        Assert.Contains("Interlocked.CompareExchange(ref _busy", renderer, StringComparison.Ordinal);
        // The renderer holds the camera, so the self-view must release it.
        Assert.Contains("_effectRenderer?.Dispose()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Camera_effects_degrade_instead_of_breaking_a_recording()
    {
        var renderer = ReadFixture("CameraEffectRenderer.cs.txt");
        var segmenter = ReadFixture("SelfieSegmenter.cs.txt");

        // A missing or broken model disables background effects rather than
        // throwing into the recording path: both public entry points swallow.
        // (The private constructor may throw — TryCreate is what catches it.)
        Assert.Contains("public static SelfieSegmenter? TryCreate", segmenter, StringComparison.Ordinal);
        Assert.Contains("return null", segmenter, StringComparison.Ordinal);
        var tryGetMask = segmenter[segmenter.IndexOf("public bool TryGetMask", StringComparison.Ordinal)..];
        Assert.Contains("catch (Exception)", tryGetMask, StringComparison.Ordinal);
        Assert.Contains("_failed = true", tryGetMask, StringComparison.Ordinal);
        // When frames cost too much, inference is stretched before anything visible drops.
        Assert.Contains("InferenceInterval", renderer, StringComparison.Ordinal);
    }

    [Fact]
    public void The_shaped_self_view_drops_its_window_region_while_it_is_dragged()
    {
        var code = ReadFixture("CameraSelfViewWindow.xaml.cs.txt");
        var placement = ReadFixture("WindowPlacement.cs.txt");

        Assert.Contains("SquircleGeometry.ComputePolygon", code, StringComparison.Ordinal);
        Assert.Contains("ClipToPolygonPixelRegion", placement, StringComparison.Ordinal);
        Assert.Contains("CreatePolygonRgn", placement, StringComparison.Ordinal);
        // This surface is both shaped and draggable, which a window region cannot be
        // during the drag itself: it takes the window off DWM's fast path and the
        // drag visibly lags. Both shapes go through one clip path so press/release
        // has a single thing to drop and restore.
        Assert.Contains("private void ApplyShapeClip()", code, StringComparison.Ordinal);
        Assert.Contains("ClearWindowRegion", code, StringComparison.Ordinal);
        Assert.Contains("SetWindowRgn(WinRT.Interop.WindowNative.GetWindowHandle(window), 0, true)", placement, StringComparison.Ordinal);
        // Cleared on press, re-cut on release.
        var pressed = Section(code, "Root_PointerPressed", "Root_PointerMoved");
        Assert.Contains("ClearWindowRegion", pressed, StringComparison.Ordinal);
        var released = Section(code, "Root_PointerReleased", "ApplyShapeClip()");
        Assert.Contains("ReleasePointerCapture", released, StringComparison.Ordinal);
        // Capture can be lost without a release, so that path restores it too.
        var captureLost = Section(code, "Root_PointerCaptureLost", "private void ApplyShapeClip");
        Assert.Contains("ApplyShapeClip()", captureLost, StringComparison.Ordinal);
    }

    [Fact]
    public void Starting_the_preview_can_never_orphan_a_renderer()
    {
        var preflight = ReadFixture("VideoPreflightWindow.xaml.cs.txt");

        // Several UI events start the preview, and LoadDevicesAsync seeds
        // CameraBox.SelectedItem, which fires SelectionChanged synchronously. Two
        // interleaved starts used to overwrite _previewRenderer and strand the
        // first one — and an orphaned frame reader holds the camera open, which is
        // what kept the capture light on with nothing on screen.
        Assert.Contains("SemaphoreSlim", preflight, StringComparison.Ordinal);
        Assert.Contains("_cameraGate.WaitAsync()", preflight, StringComparison.Ordinal);
        Assert.Contains("_cameraGate.Release()", preflight, StringComparison.Ordinal);
        // The seed does not trigger a start of its own.
        var selectionChanged = Section(preflight, "private async void CameraBox_SelectionChanged", "}");
        Assert.Contains("_seeding", selectionChanged, StringComparison.Ordinal);
        // A start owns candidates locally until it is safe to publish them. Closing
        // at any await therefore leaves the resources reachable by finally instead
        // of publishing a new camera after teardown has already run.
        var start = Section(preflight, "private async Task StartCameraPreviewCoreAsync", "private void ShowCameraSlot");
        Assert.Contains("MediaCapture? capture = null", start, StringComparison.Ordinal);
        Assert.Contains("CameraEffectRenderer? renderer = null", start, StringComparison.Ordinal);
        Assert.Contains("if (_closing)", start, StringComparison.Ordinal);
        Assert.Contains("_mediaCapture = capture", start, StringComparison.Ordinal);
        Assert.Contains("if (!published)", start, StringComparison.Ordinal);
        Assert.Contains("await renderer.DisposeAsync()", start, StringComparison.Ordinal);
        Assert.Contains("capture?.Dispose()", start, StringComparison.Ordinal);
    }

    [Fact]
    public void Releasing_the_camera_never_depends_on_a_closing_windows_dispatcher()
    {
        var renderer = ReadFixture("CameraEffectRenderer.cs.txt");
        var preflight = ReadFixture("VideoPreflightWindow.xaml.cs.txt");
        var selfView = ReadFixture("CameraSelfViewWindow.xaml.cs.txt");

        // Both surfaces tear the camera down from their Closed handler. A
        // continuation posted back to a closing window's dispatcher may never be
        // drained, which strands the MediaCapture and leaves the capture light on.
        Assert.Contains("ConfigureAwait(false)", renderer, StringComparison.Ordinal);
        Assert.Contains("await renderer.DisposeAsync().ConfigureAwait(false)", preflight, StringComparison.Ordinal);
        // Normal close is intercepted and awaits serialized teardown while the
        // window dispatcher is alive. Closed retains a synchronous last resort.
        Assert.Contains("AppWindow.Closing += AppWindow_Closing", preflight, StringComparison.Ordinal);
        var close = Section(preflight, "private async Task CloseAfterCleanupAsync()", "private void EmergencyCleanupDevices()");
        Assert.Contains("await StopCameraPreviewAsync()", close, StringComparison.Ordinal);
        Assert.True(close.IndexOf("await StopCameraPreviewAsync()", StringComparison.Ordinal) <
            close.IndexOf("Close()", StringComparison.Ordinal));
        var cleanup = Section(preflight, "private void EmergencyCleanupDevices()", "private static string GetDiskSpaceStatus");
        Assert.Contains("_mediaCapture?.Dispose()", cleanup, StringComparison.Ordinal);
        // The self-view disposes its capture synchronously, after the async
        // renderer teardown is kicked off rather than awaited.
        var release = Section(selfView, "private void ReleaseCamera()", "_mediaCapture = null;");
        Assert.Contains("_mediaCapture?.Dispose()", release, StringComparison.Ordinal);
    }

    [Fact]
    public void The_camera_is_fully_released_before_the_same_device_is_reopened()
    {
        var renderer = ReadFixture("CameraEffectRenderer.cs.txt");
        var preflight = ReadFixture("VideoPreflightWindow.xaml.cs.txt");
        var selfView = ReadFixture("CameraSelfViewWindow.xaml.cs.txt");

        // Teardown has to await the frame reader stop, not fire and forget it —
        // DirectShow allows one consumer, so returning early is what makes the
        // next preview or self-view find the camera busy.
        Assert.Contains("public async Task DisposeAsync", renderer, StringComparison.Ordinal);
        Assert.Contains("await reader.StopAsync()", renderer, StringComparison.Ordinal);
        // And it must outlast any frame still inside the pipeline, because
        // releasing the ONNX session under a running inference is a native
        // use-after-free rather than a catchable exception.
        Assert.Contains("WaitForFrameWorkAsync", renderer, StringComparison.Ordinal);
        Assert.Contains("_processing", renderer, StringComparison.Ordinal);
        // Callers await it, and the renderer goes down before the MediaCapture.
        // The capture is held in a local across that await so the field cannot be
        // reassigned underneath it, hence the ordering check is against that local.
        Assert.Contains("await renderer.DisposeAsync()", preflight, StringComparison.Ordinal);
        Assert.True(preflight.IndexOf("await renderer.DisposeAsync()", StringComparison.Ordinal) <
            preflight.IndexOf("capture?.Dispose()", StringComparison.Ordinal));
        Assert.Contains("await renderer.DisposeAsync()", selfView, StringComparison.Ordinal);
    }

    [Fact]
    public void Recording_releases_the_camera_after_capture_and_before_finalization_work()
    {
        var recording = ReadFixture("RecordingService.cs.txt");
        var main = ReadFixture("MainWindow.xaml.cs.txt");
        var stop = Section(recording, "public async Task<CaptureRecord?> StopVideoAsync", "private void StartVideoMicrophone");

        // The self-view must be present through FFmpeg's last frame. Finalizing is
        // published only after the process has exited and been disposed, but before
        // muxing and capture registration can keep the operation busy.
        Assert.True(stop.IndexOf("process.Dispose()", StringComparison.Ordinal) <
            stop.IndexOf("SetState(RecordingState.Finalizing)", StringComparison.Ordinal));
        Assert.True(stop.IndexOf("SetState(RecordingState.Finalizing)", StringComparison.Ordinal) <
            stop.IndexOf("MuxVideoMicrophoneAsync", StringComparison.Ordinal));
        Assert.True(stop.IndexOf("process.Dispose()", StringComparison.Ordinal) <
            stop.IndexOf("SetState(RecordingState.Failed)", StringComparison.Ordinal));
        Assert.Contains("RecordingState.Finalizing or RecordingState.Idle or RecordingState.Failed", main, StringComparison.Ordinal);
        Assert.Contains("DismissCameraSelfView()", main, StringComparison.Ordinal);
    }

    [Fact]
    public void Camera_frames_are_copied_with_the_projected_buffer_api_and_failures_are_recorded()
    {
        var renderer = ReadFixture("CameraEffectRenderer.cs.txt");

        // LockBuffer plus a hand-declared IMemoryBufferByteAccess does not work under
        // CsWinRT: the reference arrives as a projected WinRT.IInspectable and the
        // cast throws on every frame. Verified against a real camera — it produced a
        // permanently blank preview with the capture light on.
        // Matched as code, not as prose: the comment above the fix names both of the
        // old APIs on purpose, so a bare word search would flag its own explanation.
        Assert.DoesNotContain("[ComImport]", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain("(IMemoryBufferByteAccess)", renderer, StringComparison.Ordinal);
        Assert.DoesNotContain(".LockBuffer(", renderer, StringComparison.Ordinal);
        Assert.Contains(".CopyToBuffer(", renderer, StringComparison.Ordinal);
        Assert.Contains(".CopyFromBuffer(", renderer, StringComparison.Ordinal);
        Assert.Contains(".AsBuffer(", renderer, StringComparison.Ordinal);

        // The frame loop swallows per-frame exceptions on purpose, so it has to
        // record them. Without this, a step that fails every single frame is
        // indistinguishable from a camera that produced nothing.
        Assert.Contains("catch (Exception error)", renderer, StringComparison.Ordinal);
        Assert.Contains("_skipReason", renderer, StringComparison.Ordinal);
        Assert.Contains("Diagnosis", renderer, StringComparison.Ordinal);
        // No requested subtype: forcing Bgra8 starves cameras that only offer NV12.
        Assert.DoesNotContain("MediaEncodingSubtypes.Bgra8", renderer, StringComparison.Ordinal);
    }

    [Fact]
    public void The_both_button_chord_is_hidden_from_the_window_underneath()
    {
        var code = ReadFixture("MouseActivityService.cs.txt");

        // The first button passes through, so ordinary clicks and drags are
        // untouched. Only the second one -- already a deliberate thing to do -- is
        // swallowed, by returning non-zero instead of chaining the hook.
        Assert.Contains("return 1;", code, StringComparison.Ordinal);
        Assert.Contains("_swallowingChord", code, StringComparison.Ordinal);
        // The button that did reach the app has to be released for it, or the app
        // sits in a drag or capture state after command mode opens.
        Assert.Contains("ReleaseHeldButtonsForApplication", code, StringComparison.Ordinal);
        Assert.Contains("MouseEventFLeftUp", code, StringComparison.Ordinal);
        Assert.Contains("MouseEventFRightUp", code, StringComparison.Ordinal);
        // Our own synthesized release must not be read back as user input.
        Assert.Contains("LowLevelMouseInjected", code, StringComparison.Ordinal);
        // A perfectly still hold emits no further mouse messages, so the hold can
        // only be noticed by a timer -- the hook alone would never fire it.
        Assert.Contains("System.Threading.Timer", code, StringComparison.Ordinal);
        Assert.Contains("ShouldActivate", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Preflight_handlers_that_can_fire_mid_parse_check_the_tree_exists_first()
    {
        var xaml = ReadFixture("VideoPreflightWindow.xaml");
        var code = ReadFixture("VideoPreflightWindow.xaml.cs.txt");

        // SourceBox carries a value *and* a handler in XAML, so the handler runs
        // while InitializeComponent is still parsing. Anything it touches further
        // down the document is still null at that point -- an unguarded write is a
        // NullReferenceException that takes the whole preflight window down.
        var sourceBox = Section(xaml, "x:Name=\"SourceBox\"", "</ComboBox>");
        Assert.Contains("SelectedIndex=", sourceBox, StringComparison.Ordinal);
        Assert.Contains("SelectionChanged=", sourceBox, StringComparison.Ordinal);
        Assert.True(
            xaml.IndexOf("x:Name=\"SourceBox\"", StringComparison.Ordinal) <
            xaml.IndexOf("x:Name=\"FrameRateTag\"", StringComparison.Ordinal),
            "FrameRateTag is expected to come after SourceBox, which is what makes the guard necessary.");

        var summaryTags = Section(code, "private void UpdateSummaryTags()", "FrameRateTag.Text");
        Assert.Contains("is null", summaryTags, StringComparison.Ordinal);
        Assert.Contains("return;", summaryTags, StringComparison.Ordinal);
        // The same exposure applies to every seed-time handler on this surface.
        foreach (var guarded in new[] { "UpdateCameraSourceNotice", "UpdateCameraSlotShape", "UpdateEffectValueReadouts" })
        {
            var body = Section(code, $"private void {guarded}()", "}");
            Assert.Contains("is null", body, StringComparison.Ordinal);
        }
        // And the effect push bails before reading any control.
        var push = Section(code, "private async Task PushEffectSettingsAsync()", "await _previewRenderer");
        Assert.Contains("_previewRenderer is null", push, StringComparison.Ordinal);
    }

    [Fact]
    public void Preflight_never_opens_a_file_picker_while_seeding_its_controls()
    {
        var preflight = ReadFixture("VideoPreflightWindow.xaml.cs.txt");

        // Assigning SelectedIndex fires SelectionChanged synchronously, so a
        // remembered custom background must not reopen the picker on load.
        Assert.Contains("_seeding", preflight, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(_customBackgroundPath)", preflight, StringComparison.Ordinal);
        Assert.True(preflight.IndexOf("SeedBackgroundSelection(App.Services.Settings", StringComparison.Ordinal) <
            preflight.IndexOf("_seeding = false", StringComparison.Ordinal));
    }

    [Fact]
    public void Rapid_effect_changes_publish_the_latest_settings_not_the_fastest()
    {
        var renderer = ReadFixture("CameraEffectRenderer.cs.txt");

        // Loading a replacement image awaits a decode, so two quick changes can
        // complete out of order and leave the preview disagreeing with the UI.
        Assert.Contains("_settingsRevision", renderer, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Increment(ref _settingsRevision)", renderer, StringComparison.Ordinal);
    }

    [Fact]
    public void Audio_note_cleanup_never_leaves_a_file_the_library_would_adopt()
    {
        var recording = ReadFixture("RecordingService.cs.txt");
        var cleanup = recording[recording.IndexOf("TryCleanupAudioNoteAsync(string wavPath", StringComparison.Ordinal)..];

        // Orphan recovery adopts any .wav under audio/<date>, so the temp file
        // has to live outside the capture categories.
        Assert.Contains(".cursorpocket\", \"temp\"", cleanup, StringComparison.Ordinal);
        Assert.DoesNotContain("wavPath + \".cleanup.wav\"", cleanup, StringComparison.Ordinal);
        // A cancelled stop must not leave ffmpeg holding the file.
        Assert.Contains("process.Kill(true)", cleanup, StringComparison.Ordinal);
        // Same format in and out, so a short result means audio was lost.
        Assert.Contains("originalLength * 0.9", cleanup, StringComparison.Ordinal);
    }

    [Fact]
    public void Microphone_cleanup_runs_at_finalize_time_so_a_filter_can_never_lose_a_take()
    {
        var recording = ReadFixture("RecordingService.cs.txt");

        // The raw capture is written first and only replaced on success.
        Assert.Contains("AudioCleanupFilterBuilder.Build", recording, StringComparison.Ordinal);
        Assert.Contains("TryCleanupAudioNoteAsync", recording, StringComparison.Ordinal);
        Assert.True(recording.IndexOf("_waveIn.StopRecording()", StringComparison.Ordinal) <
            recording.IndexOf("TryCleanupAudioNoteAsync(reservation.AbsolutePath", StringComparison.Ordinal));
        // Video stays a stream copy: cleanup must never re-encode the picture.
        Assert.Contains("\"-c:v\", \"copy\"", recording, StringComparison.Ordinal);
    }

    [Fact]
    public void The_effect_model_and_runtime_are_verified_by_the_packaging_script()
    {
        var build = ReadFixture("build-native.ps1.txt");

        Assert.Contains("fetch_models.ps1", build, StringComparison.Ordinal);
        Assert.Contains("selfie_segmenter.onnx", build, StringComparison.Ordinal);
        Assert.Contains("onnxruntime.dll", build, StringComparison.Ordinal);
        Assert.Contains("Published camera-effects artifact is missing", build, StringComparison.Ordinal);
        Assert.Contains("Assets\\Backgrounds\\graphite.png", build, StringComparison.Ordinal);
    }

    [Fact]
    public void Video_preflight_has_one_bounded_inspector_scroll_and_a_fixed_start_action()
    {
        var xaml = ReadFixture("VideoPreflightWindow.xaml");
        var code = ReadFixture("VideoPreflightWindow.xaml.cs.txt");
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(xaml, "<ScrollViewer").Cast<System.Text.RegularExpressions.Match>());
        Assert.Contains("x:Name=\"OptionsPanel\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Camera appearance", xaml, StringComparison.Ordinal);
        Assert.Contains("Recording options", xaml, StringComparison.Ordinal);
        Assert.Contains("TransientWindowLayoutPolicy.Resolve", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Recording_hud_uses_large_high_contrast_status_and_actions()
    {
        var xaml = ReadFixture("RecordingHudWindow.xaml");

        Assert.Contains("Background=\"{ThemeResource PocketGlassDense}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"20\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"17\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"13\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Stop and save\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"40\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("BorderBrush=\"#CCFF5A67\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"Transparent\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ThemeShadow", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void The_recording_hud_sits_small_at_the_top_edge_and_opens_on_hover()
    {
        var xaml = ReadFixture("RecordingHudWindow.xaml");
        var code = ReadFixture("RecordingHudWindow.xaml.cs.txt");

        Assert.Contains("PointerEntered=\"Root_PointerEntered\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PointerExited=\"Root_PointerExited\"", xaml, StringComparison.Ordinal);
        // Keyboard users must be able to reach the actions without a pointer.
        Assert.Contains("GotFocus=\"Root_GotFocus\"", xaml, StringComparison.Ordinal);
        Assert.Contains("StripHeight = 40", code, StringComparison.Ordinal);
        // The window is one fixed size that slides; resizing it or recomputing a window
        // region per frame is what made the travel stutter.
        Assert.Contains("WindowPlacement.MoveTo(this, _panelLeft, top)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ClipToRoundedRegion", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PlaceTopCenter", code, StringComparison.Ordinal);
        Assert.Contains("SetDrawerTarget", code, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowPlacement.PointerPosition()", code, StringComparison.Ordinal);
        Assert.Contains("DrawerAnimation.Advance", code, StringComparison.Ordinal);
        // Escape still stops and saves, so a collapsed HUD never traps a recording.
        Assert.Contains("EscapeHotkey.Capture", code, StringComparison.Ordinal);
        // The level meter is a rolling waveform, not one bar sliding left and right.
        Assert.Contains("AudioLevelHistory", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ProgressBar", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("LevelBar", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Transient_receipts_and_hud_do_not_expose_white_window_gutters()
    {
        var receipt = ReadFixture("ReceiptWindow.xaml");
        var placement = ReadFixture("WindowPlacement.cs.txt");

        Assert.Contains("Background=\"{ThemeResource PocketGlassPanel}\"", receipt, StringComparison.Ordinal);
        Assert.Contains("Background=\"Transparent\"", receipt, StringComparison.Ordinal);
        Assert.Contains("BorderBrush=\"{ThemeResource PocketGlassRim}\"", receipt, StringComparison.Ordinal);
        Assert.Contains("PocketGlassTopEdge", receipt, StringComparison.Ordinal);
        Assert.Contains("DwmSetWindowAttribute", placement, StringComparison.Ordinal);
        Assert.Contains("DwmWindowCornerPreferenceRound", placement, StringComparison.Ordinal);
        Assert.Contains("DwmwaBorderColor", placement, StringComparison.Ordinal);
        Assert.Contains("DwmColorNone", placement, StringComparison.Ordinal);
        Assert.Contains("GwlStyle", placement, StringComparison.Ordinal);
        Assert.Contains("WsCaption", placement, StringComparison.Ordinal);
        Assert.Contains("SwpFrameChanged", placement, StringComparison.Ordinal);
        Assert.Contains("DwmNcRenderingDisabled", placement, StringComparison.Ordinal);
        Assert.Contains("SetWindowRgn", placement, StringComparison.Ordinal);
        // The HUD deliberately takes its rounded corners from DWM instead of a window
        // region: it slides every frame, and a region clip drops the window off DWM's
        // fast path. ConfigureUtilityWindow is what asks for the rounding.
        Assert.Contains("WindowPlacement.ConfigureUtilityWindow(this)", ReadFixture("RecordingHudWindow.xaml.cs.txt"), StringComparison.Ordinal);
        Assert.Contains("DwmwaWindowCornerPreference", placement, StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_commands_have_no_artificial_waits_on_the_critical_path()
    {
        var main = ReadFixture("MainWindow.xaml.cs.txt");
        var recording = ReadFixture("RecordingService.cs.txt");
        var snapshot = ReadFixture("DesktopSnapshot.cs.txt");
        var hud = ReadFixture("RecordingHudWindow.xaml.cs.txt");

        Assert.DoesNotContain("Task.Delay(170", main, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay(130", main, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay(350", recording, StringComparison.Ordinal);
        Assert.Contains("ImageFormat.Bmp", snapshot, StringComparison.Ordinal);
        // The desktop frame is handed to the decoder in memory. Staging it through
        // the temp folder wrote and re-read tens of megabytes on the hotkey path.
        Assert.DoesNotContain("Path.GetTempPath()", snapshot, StringComparison.Ordinal);
        Assert.DoesNotContain("UriSource", snapshot, StringComparison.Ordinal);
        Assert.Contains("Starting…", hud, StringComparison.Ordinal);
        Assert.True(main.IndexOf("RecordingHudWindow.ShowForVideo", StringComparison.Ordinal) <
            main.IndexOf("RecordingSession.StartVideoAsync(options)", StringComparison.Ordinal));
    }

    [Fact]
    public void Command_palette_is_warm_reused_and_scopes_bare_hotkeys_to_visibility()
    {
        var main = ReadFixture("MainWindow.xaml.cs.txt");
        var palette = ReadFixture("CommandPaletteWindow.xaml.cs.txt");
        var hotkeys = ReadFixture("PaletteHotkeyService.cs.txt");

        Assert.Contains("InitializeCommandPalette();", main, StringComparison.Ordinal);
        Assert.Contains("_palette!.Show", main, StringComparison.Ordinal);
        Assert.DoesNotContain("new CommandPaletteWindow(_lastSourceWindow", main, StringComparison.Ordinal);
        Assert.Contains("_commandKeys.SetEnabled(true)", palette, StringComparison.Ordinal);
        Assert.Contains("_commandKeys.SetEnabled(false)", palette, StringComparison.Ordinal);
        Assert.Contains("AppWindow.Hide()", palette, StringComparison.Ordinal);
        Assert.Contains("WmSetEnabled", hotkeys, StringComparison.Ordinal);
        Assert.Contains("_enabledChanged.Wait", hotkeys, StringComparison.Ordinal);
    }

    [Fact]
    public void Escape_is_scoped_to_recording_and_screenshot_surfaces()
    {
        var service = ReadFixture("ScopedEscapeHotkeyService.cs.txt");

        Assert.Contains("RegisterHotKey", service, StringComparison.Ordinal);
        Assert.Contains("VirtualKeyEscape", service, StringComparison.Ordinal);
        Assert.Contains("UnregisterHotKey", service, StringComparison.Ordinal);
        Assert.Contains("EscapeHotkey.Capture", ReadFixture("RecordingHudWindow.xaml.cs.txt"), StringComparison.Ordinal);
        Assert.Contains("EscapeHotkey.Capture", ReadFixture("RegionSelectorWindow.xaml.cs.txt"), StringComparison.Ordinal);
        Assert.Contains("EscapeHotkey.Capture", ReadFixture("AnnotationWindow.xaml.cs.txt"), StringComparison.Ordinal);
        Assert.Contains("StopAsync(false)", ReadFixture("RecordingHudWindow.xaml.cs.txt"), StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_generation_is_serialized_atomic_and_non_fatal()
    {
        var code = ReadFixture("PreviewService.cs.txt");

        Assert.Contains("SemaphoreSlim", code, StringComparison.Ordinal);
        Assert.Contains("File.Move(temporary, target, false)", code, StringComparison.Ordinal);
        Assert.Contains("The capture remains valid", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_path_and_audio_waveform_are_healed_on_use()
    {
        Assert.Contains("Startup.SetEnabled(settings.StartWithWindows)", ReadFixture("AppServices.cs.txt"), StringComparison.Ordinal);
        var mainPage = ReadFixture("MainPage.xaml.cs.txt");
        Assert.Contains("CaptureKind.Audio", mainPage, StringComparison.Ordinal);
        Assert.Contains("GetPreviewAsync(item.Record", mainPage, StringComparison.Ordinal);
        Assert.Contains("DetailPlayer.Height = 92", mainPage, StringComparison.Ordinal);
    }

    [Fact]
    public void Recording_state_subscription_survives_capture_folder_changes()
    {
        var code = ReadFixture("MainWindow.xaml.cs.txt");

        Assert.Contains("SubscribeToRecordingState", code, StringComparison.Ordinal);
        Assert.Contains("_subscribedRecording.StateChanged -= Recording_StateChanged", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Tray_uses_brand_logo_one_with_state_specific_tooltips()
    {
        var code = ReadFixture("MainWindow.xaml.cs.txt");

        Assert.Contains("LoadTrayIcon(\"TrayReady.ico\")", code, StringComparison.Ordinal);
        Assert.Contains("LoadTrayIcon(\"TrayRecording.ico\")", code, StringComparison.Ordinal);
        Assert.Contains("TrayPresentation.For(state)", code, StringComparison.Ordinal);
        Assert.Contains("_trayRecordingIcon ?? _trayReadyIcon", code, StringComparison.Ordinal);
        Assert.Contains("DisposeTray()", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Primary_logo_fills_command_title_and_taskbar_surfaces()
    {
        var command = ReadFixture("CommandPaletteWindow.xaml");
        var window = ReadFixture("MainWindow.xaml");
        var code = ReadFixture("MainWindow.xaml.cs.txt");
        var installer = ReadFixture("CursorPocket.iss.txt");
        var localInstall = ReadFixture("install.ps1.txt");

        Assert.Contains("<Grid Width=\"40\" Height=\"40\">", command, StringComparison.Ordinal);
        Assert.Contains("<Image Width=\"40\" Height=\"40\"", command, StringComparison.Ordinal);
        Assert.Contains("RegularHeight = 438", ReadFixture("CommandPaletteWindow.xaml.cs.txt"), StringComparison.Ordinal);
        Assert.Contains("ShortHeight = 308", ReadFixture("CommandPaletteWindow.xaml.cs.txt"), StringComparison.Ordinal);
        Assert.Contains("ScreenshotHeight = 294", ReadFixture("CommandPaletteWindow.xaml.cs.txt"), StringComparison.Ordinal);
        Assert.Contains("x:Name=\"AppTitleBar\" Height=\"48\"", window, StringComparison.Ordinal);
        Assert.Contains("Width=\"40\"", window, StringComparison.Ordinal);
        Assert.Contains("Height=\"40\"", window, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(AppContext.BaseDirectory, \"Assets\", \"AppIcon.ico\")", code, StringComparison.Ordinal);
        Assert.Contains("AppWindow.SetTaskbarIcon(iconPath)", code, StringComparison.Ordinal);
        Assert.Contains("AppWindow.SetTitleBarIcon(iconPath)", code, StringComparison.Ordinal);
        Assert.Contains("IconFilename: \"{app}\\Assets\\AppIcon.ico\"", installer, StringComparison.Ordinal);
        Assert.Contains("$shortcut.IconLocation = \"$installedIcon,0\"", localInstall, StringComparison.Ordinal);
        Assert.Contains("Assets\\AppIconRecording.ico", localInstall, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath $obsoleteRecordingIcon", localInstall, StringComparison.Ordinal);
    }

    [Fact]
    public void Native_package_stages_and_verifies_compiled_winui_resources()
    {
        var script = ReadFixture("build-native.ps1.txt");

        Assert.Contains("Get-ChildItem -LiteralPath $targetDir -Filter \"*.xbf\"", script, StringComparison.Ordinal);
        Assert.Contains("Get-ChildItem -LiteralPath $targetDir -Filter \"*.pri\"", script, StringComparison.Ordinal);
        Assert.Contains("$requiredWinUiResources", script, StringComparison.Ordinal);
        Assert.Contains("Assets\\AppIcon.ico", script, StringComparison.Ordinal);
        Assert.Contains("Assets\\TrayReady.ico", script, StringComparison.Ordinal);
        Assert.Contains("Assets\\TrayRecording.ico", script, StringComparison.Ordinal);
        Assert.Contains("\"SplashScreen\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Background_startup_wins_the_initial_winui_show_race()
    {
        var code = ReadFixture("App.xaml.cs.txt");

        Assert.Contains("--background", code, StringComparison.Ordinal);
        Assert.Contains("Environment.GetCommandLineArgs()", code, StringComparison.Ordinal);
        Assert.Contains("DispatcherQueuePriority.Low", code, StringComparison.Ordinal);
        Assert.True(code.Split("Window.AppWindow.Hide()", StringSplitOptions.None).Length >= 3);
    }

    /// <summary>
    /// The slice of a source fixture between two markers, so an assertion can say
    /// "inside this method" instead of "somewhere in the file".
    /// </summary>
    private static string Section(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{startMarker}' was not found.");
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end >= 0, $"'{endMarker}' was not found after '{startMarker}'.");
        return source[start..end];
    }

    [Fact]
    public void Pointer_tracking_stays_off_the_ui_thread_and_allocation_free()
    {
        var mouse = ReadFixture("MouseActivityService.cs.txt");
        var main = ReadFixture("MainWindow.xaml.cs.txt");

        // The low-level hook fires on the thread that installed it, so it must own a
        // thread and pump messages rather than borrowing the XAML dispatcher.
        Assert.Contains("SetWindowsHookEx", mouse, StringComparison.Ordinal);
        Assert.Contains("GetMessage", mouse, StringComparison.Ordinal);
        Assert.Contains("CursorPocket.MouseHook", mouse, StringComparison.Ordinal);
        Assert.DoesNotContain("Marshal.PtrToStructure<", mouse, StringComparison.Ordinal);
        // Gesture work is skipped outright when the user turned the gesture off.
        Assert.Contains("_gestureEnabled", mouse, StringComparison.Ordinal);
        Assert.Contains("GestureEnabled = App.Services.Settings.MouseGestureEnabled", main, StringComparison.Ordinal);
        // One coalesced dispatcher item, not one closure per mouse event.
        Assert.Contains("TryConsumeLatestPosition", main, StringComparison.Ordinal);
        // The coalesced signal latches, so the hook must go live only after the
        // handlers are attached.
        Assert.True(
            main.IndexOf("_mouseActivity.Moved +=", StringComparison.Ordinal) <
            main.IndexOf("_mouseActivity.Start();", StringComparison.Ordinal),
            "The pointer hook must start after Moved is subscribed.");
    }

    [Fact]
    public void Cursor_companion_caches_its_pulse_and_idles_when_hidden()
    {
        var code = ReadFixture("NativeCompanionWindow.cs.txt");

        Assert.Contains("_readyFrames", code, StringComparison.Ordinal);
        Assert.Contains("_recordingFrames", code, StringComparison.Ordinal);
        // Nothing visible means nothing to animate.
        Assert.Contains("_pulseTimer.Stop()", code, StringComparison.Ordinal);
        // The frames and the shared memory DC are GDI handles and must be released.
        Assert.Contains("ReleaseFrames", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Screenshots_encode_off_the_ui_thread()
    {
        var code = ReadFixture("ScreenshotCaptureService.cs.txt");

        Assert.Contains("Task.Run", code, StringComparison.Ordinal);
        Assert.True(
            code.IndexOf("Task.Run", StringComparison.Ordinal) <
            code.IndexOf("ImageFormat.Png", StringComparison.Ordinal),
            "The PNG encode must happen inside the background work item.");
    }

    [Fact]
    public void Launch_does_not_wait_on_the_capture_folder_or_poll_for_activation()
    {
        var services = ReadFixture("AppServices.cs.txt");
        var app = ReadFixture("App.xaml.cs.txt");

        // Orphan recovery walks the capture folder; it must not gate the first window.
        Assert.Contains("StartOrphanRecovery", services, StringComparison.Ordinal);
        Assert.DoesNotContain("await captureStore.RecoverOrphanedMediaAsync", services, StringComparison.Ordinal);
        Assert.Contains("RegisterWaitForSingleObject", app, StringComparison.Ordinal);
        Assert.DoesNotContain("WaitOne(500)", app, StringComparison.Ordinal);
        // A tray-only launch must not build the Library.
        Assert.Contains("StartedInBackground", app, StringComparison.Ordinal);
        Assert.Contains("EnsureLibraryLoadedAsync", ReadFixture("MainPage.xaml.cs.txt"), StringComparison.Ordinal);
    }

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
