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
    public void Video_preflight_does_not_start_before_readiness_and_targets_the_pointer_display()
    {
        var code = ReadFixture("VideoPreflightWindow.xaml.cs.txt");

        Assert.Contains("!StartButton.IsEnabled", code, StringComparison.Ordinal);
        Assert.Contains("DisplayIndexUnderPointer", code, StringComparison.Ordinal);
        Assert.DoesNotContain("WdaExcludeFromCapture", code, StringComparison.Ordinal);
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
        // It sits over the user's work while they demonstrate it, so it must not
        // take clicks or focus.
        Assert.Contains("MakeClickThrough", code, StringComparison.Ordinal);
        Assert.Contains("WsExTransparent", placement, StringComparison.Ordinal);
        Assert.Contains("RestoreFocus(sourceWindow)", code, StringComparison.Ordinal);
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
    public void The_squircle_self_view_is_shaped_by_a_window_region_and_is_never_draggable()
    {
        var code = ReadFixture("CameraSelfViewWindow.xaml.cs.txt");
        var placement = ReadFixture("WindowPlacement.cs.txt");

        Assert.Contains("SquircleGeometry.ComputePolygon", code, StringComparison.Ordinal);
        Assert.Contains("ClipToPolygonPixelRegion", placement, StringComparison.Ordinal);
        Assert.Contains("CreatePolygonRgn", placement, StringComparison.Ordinal);
        // Region clipping is only safe here because this surface is click-through
        // and never handed to the Windows move loop.
        Assert.Contains("MakeClickThrough", code, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginNativeDrag", code, StringComparison.Ordinal);
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
        Assert.Contains("await renderer.DisposeAsync()", preflight, StringComparison.Ordinal);
        Assert.True(preflight.IndexOf("await renderer.DisposeAsync()", StringComparison.Ordinal) <
            preflight.IndexOf("_mediaCapture?.Dispose()", StringComparison.Ordinal));
        Assert.Contains("await renderer.DisposeAsync()", selfView, StringComparison.Ordinal);
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
        Assert.Contains("FontSize=\"17\"", xaml, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"22\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Stop &amp; save\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BorderThickness=\"0\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("BorderBrush=\"#CCFF5A67\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Background=\"Transparent\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ThemeShadow", xaml, StringComparison.Ordinal);
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
        Assert.Contains("ClipToRoundedRegion", ReadFixture("RecordingHudWindow.xaml.cs.txt"), StringComparison.Ordinal);
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
