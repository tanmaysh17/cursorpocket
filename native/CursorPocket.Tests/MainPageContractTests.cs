using System.Text.RegularExpressions;

namespace CursorPocket.Tests;

public sealed class MainPageContractTests
{
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
