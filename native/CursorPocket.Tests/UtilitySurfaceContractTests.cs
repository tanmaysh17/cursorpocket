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
    }

    [Fact]
    public void Command_palette_restores_the_source_window_on_every_close_path()
    {
        var code = ReadFixture("CommandPaletteWindow.xaml.cs.txt");

        Assert.Contains("App.Services.Context.RestoreFocus(SourceWindow)", code, StringComparison.Ordinal);
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
    public void Recording_state_subscription_survives_capture_folder_changes()
    {
        var code = ReadFixture("MainWindow.xaml.cs.txt");

        Assert.Contains("SubscribeToRecordingState", code, StringComparison.Ordinal);
        Assert.Contains("_subscribedRecording.StateChanged -= Recording_StateChanged", code, StringComparison.Ordinal);
    }

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
