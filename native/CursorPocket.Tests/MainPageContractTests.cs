using System.Text.RegularExpressions;

namespace CursorPocket.Tests;

public sealed class MainPageContractTests
{
    [Fact]
    public void Settings_use_the_full_window_without_cramping_narrow_layouts()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MainPage.xaml");
        var xaml = File.ReadAllText(path);

        Assert.Contains("x:Name=\"SettingsContent\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SettingsLeftColumn\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SettingsRightColumn\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"SettingsSecondColumn\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AdaptiveTrigger MinWindowWidth=\"1280\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Target=\"SettingsRightColumn.(Grid.Row)\" Value=\"2\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxWidth=\"880\"", xaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("FpsBox")]
    [InlineData("CountdownBox")]
    public void Numeric_combo_boxes_do_not_bind_string_tags_to_integer_values(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MainPage.xaml");
        var xaml = File.ReadAllText(path);
        var element = Regex.Match(
            xaml,
            $"<ComboBox[^>]*x:Name=\"{Regex.Escape(name)}\"[^>]*>",
            RegexOptions.CultureInvariant);

        Assert.True(element.Success, $"The {name} ComboBox was not found.");
        Assert.DoesNotContain("SelectedValue", element.Value, StringComparison.Ordinal);
    }
}
