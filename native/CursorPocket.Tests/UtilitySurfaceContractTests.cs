namespace CursorPocket.Tests;

public sealed class UtilitySurfaceContractTests
{
    [Fact]
    public void Command_mode_is_a_small_glass_panel_that_holds_one_position()
    {
        var code = ReadFixture("CommandPaletteWindow.xaml.cs.txt");
        var xaml = ReadFixture("CommandPaletteWindow.xaml");
        var main = ReadFixture("MainWindow.xaml.cs.txt");

        Assert.Contains("PanelWidth = 296", code, StringComparison.Ordinal);
        Assert.Contains("PanelHeight = 340", code, StringComparison.Ordinal);
        // Acrylic blurs the live desktop, so the frozen full-screen snapshot and its
        // per-move realignment are gone along with the keep-away behaviour.
        Assert.Contains("DesktopAcrylicBackdrop", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("BackdropImage", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DesktopSnapshot.Capture", code, StringComparison.Ordinal);
        // The panel only ever moves because the user dragged it — never on its own.
        Assert.DoesNotContain("NotifyPointerMoved", code, StringComparison.Ordinal);
        Assert.DoesNotContain("NotifyPointerMoved", main, StringComparison.Ordinal);
        Assert.DoesNotContain("PalettePlacementPolicy", code, StringComparison.Ordinal);
        // The command list still has to scroll rather than clip at 250% scale.
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
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

        // A receipt never takes focus, so its actions are reachable only through global
        // keys — and those must carry modifiers, or they would swallow the typing the
        // user does during the twelve seconds the receipt is up.
        Assert.Contains("Control: true, Alt: true", receipt, StringComparison.Ordinal);
        Assert.Contains("ModControl", hotkeys, StringComparison.Ordinal);
        Assert.Contains("_keys.SetEnabled(false)", receipt, StringComparison.Ordinal);
        Assert.Contains("ReceiptKeysHint", receiptXaml, StringComparison.Ordinal);

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
        Assert.Contains("4f, 4f", code, StringComparison.Ordinal);
        Assert.Contains("_x = x + 10", code, StringComparison.Ordinal);
        Assert.Contains("_y = y + 12", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Command_palette_restores_capture_sources_but_keeps_library_in_front()
    {
        var code = ReadFixture("CommandPaletteWindow.xaml.cs.txt");

        Assert.Contains("App.Services.Context.RestoreFocus(SourceWindow)", code, StringComparison.Ordinal);
        Assert.Contains("_restoreSourceOnClose = command != \"library\"", code, StringComparison.Ordinal);
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
        // Opaque edge to edge, like every other transient surface.
        Assert.Contains("Background=\"#FF09110F\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"Transparent\"", xaml, StringComparison.Ordinal);
        // The camera has to be held before FFmpeg writes frames and released as
        // soon as the recording stops, or the next preflight preview finds it busy.
        Assert.True(main.IndexOf("ShowCameraSelfViewAsync(options)", StringComparison.Ordinal) <
            main.IndexOf("Recording.StartVideoAsync(options)", StringComparison.Ordinal));
        Assert.Contains("DismissCameraSelfView", main, StringComparison.Ordinal);
        Assert.Contains("RecordingState.Idle or RecordingState.Failed", main, StringComparison.Ordinal);
    }

    [Fact]
    public void Video_preflight_keeps_advanced_controls_scrollable_and_auto_reveals_them()
    {
        var xaml = ReadFixture("VideoPreflightWindow.xaml");
        var code = ReadFixture("VideoPreflightWindow.xaml.cs.txt");

        Assert.Contains("VerticalScrollBarVisibility=\"Visible\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Expanding=\"MoreOptions_Expanding\"", xaml, StringComparison.Ordinal);
        Assert.Contains("OptionsScroll.ChangeView", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Recording_hud_uses_large_high_contrast_status_and_actions()
    {
        var xaml = ReadFixture("RecordingHudWindow.xaml");

        Assert.Contains("Background=\"#FF09110F\"", xaml, StringComparison.Ordinal);
        // Sized to fit the drawer. The previous 17/22 pt text overflowed the panel and
        // was cut off, which is worse for legibility than smaller text that fits.
        Assert.Contains("FontSize=\"15\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"12\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("FontSize=\"22\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Stop &amp; save\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BorderThickness=\"0\"", xaml, StringComparison.Ordinal);
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
        Assert.Contains("StripHeight = 32", code, StringComparison.Ordinal);
        // The window is one fixed size that slides; resizing it or recomputing a window
        // region per frame is what made the travel stutter.
        Assert.Contains("WindowPlacement.MoveTo(this, _panelLeft, top)", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ClipToRoundedRegion", code, StringComparison.Ordinal);
        Assert.DoesNotContain("PlaceTopCenter", code, StringComparison.Ordinal);
        // Opens on approach, not on contact.
        Assert.Contains("DrawerAnimation.IsPointerNear", code, StringComparison.Ordinal);
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

        Assert.Contains("Background=\"#FF141E1A\"", receipt, StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"Transparent\"", receipt, StringComparison.Ordinal);
        Assert.DoesNotContain("BorderBrush=", receipt, StringComparison.Ordinal);
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
        Assert.Contains("Starting…", hud, StringComparison.Ordinal);
        Assert.True(main.IndexOf("RecordingHudWindow.ShowForVideo", StringComparison.Ordinal) <
            main.IndexOf("Recording.StartVideoAsync(options)", StringComparison.Ordinal));
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
        Assert.Contains("GetPreviewAsync(item.Record)", mainPage, StringComparison.Ordinal);
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
    public void Native_package_stages_and_verifies_compiled_winui_resources()
    {
        var script = ReadFixture("build-native.ps1.txt");

        Assert.Contains("Get-ChildItem -LiteralPath $targetDir -Filter \"*.xbf\"", script, StringComparison.Ordinal);
        Assert.Contains("Get-ChildItem -LiteralPath $targetDir -Filter \"*.pri\"", script, StringComparison.Ordinal);
        Assert.Contains("$requiredWinUiResources", script, StringComparison.Ordinal);
        Assert.Contains("Assets\\AppIcon.ico", script, StringComparison.Ordinal);
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

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
