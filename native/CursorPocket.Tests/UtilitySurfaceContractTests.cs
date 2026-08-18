namespace CursorPocket.Tests;

public sealed class UtilitySurfaceContractTests
{
    [Fact]
    public void Command_palette_uses_a_desktop_snapshot_instead_of_fallback_acrylic()
    {
        var xaml = ReadFixture("CommandPaletteWindow.xaml");

        Assert.Contains("x:Name=\"BackdropImage\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DesktopAcrylicBackdrop", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IBufferByteAccess", ReadFixture("DesktopSnapshot.cs.txt"), StringComparison.Ordinal);
    }

    [Fact]
    public void Region_selector_keeps_the_desktop_visible_while_selecting()
    {
        var xaml = ReadFixture("RegionSelectorWindow.xaml");

        Assert.Contains("x:Name=\"BackdropImage\"", xaml, StringComparison.Ordinal);
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
