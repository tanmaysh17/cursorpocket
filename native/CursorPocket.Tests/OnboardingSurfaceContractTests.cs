using System.Text.RegularExpressions;

namespace CursorPocket.Tests;

public sealed class OnboardingSurfaceContractTests
{
    [Fact]
    public void Tour_maps_the_complete_capture_loop_before_teaching_commands()
    {
        var xaml = ReadFixture("OnboardingPage.xaml");

        Assert.Contains("ItemsSource=\"{x:Bind CapabilityStages}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Capture, shape, retrieve", xaml, StringComparison.Ordinal);
        Assert.Contains("Nothing is uploaded.", xaml, StringComparison.Ordinal);
        Assert.Contains("Green means ready or saved", xaml, StringComparison.Ordinal);
        Assert.Contains("Red means recording or discard", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Image", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Tour_teaches_every_activation_path_and_the_shared_command_catalogue()
    {
        var xaml = ReadFixture("OnboardingPage.xaml");
        var code = ReadFixture("OnboardingPage.xaml.cs.txt");

        Assert.Contains("ItemsSource=\"{x:Bind Commands}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Draw two circles", xaml, StringComparison.Ordinal);
        Assert.Contains("Hold both mouse buttons", xaml, StringComparison.Ordinal);
        Assert.Contains("ShortcutText", xaml, StringComparison.Ordinal);
        Assert.Contains("CaptureActionCatalog.Get(action)", code, StringComparison.Ordinal);
        Assert.Contains("R region · W window · D display · A all displays · P previous region", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Final_step_preserves_choices_and_hands_off_to_the_real_product()
    {
        var xaml = ReadFixture("OnboardingPage.xaml");
        var code = ReadFixture("OnboardingPage.xaml.cs.txt");
        var main = ReadFixture("MainWindow.xaml.cs.txt");

        Assert.Contains("StartWithWindowsCheckBox", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowCompanionCheckBox", xaml, StringComparison.Ordinal);
        Assert.Contains("RehearsalFolderPathText", xaml, StringComparison.Ordinal);
        Assert.Contains("ScreenshotStarterButton", xaml, StringComparison.Ordinal);
        Assert.Contains("VideoStarterButton", xaml, StringComparison.Ordinal);
        Assert.Contains("AudioStarterButton", xaml, StringComparison.Ordinal);
        Assert.Contains("FinishAsync(openCommandMode: true)", code, StringComparison.Ordinal);
        Assert.Contains("DispatcherQueue.TryEnqueue(() => ShowCommandPalette())", main, StringComparison.Ordinal);
    }

    [Fact]
    public void Tour_uses_one_document_scroll_and_keeps_critical_actions_named()
    {
        var xaml = ReadFixture("OnboardingPage.xaml");

        Assert.Single(Regex.Matches(xaml, "<ScrollViewer", RegexOptions.CultureInvariant).Cast<Match>());
        Assert.Contains("HorizontalScrollMode=\"Disabled\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Skip tour\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Finish for now\"", xaml, StringComparison.Ordinal);
        Assert.Contains("NextButton", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"44\"", xaml, StringComparison.Ordinal);
    }

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
