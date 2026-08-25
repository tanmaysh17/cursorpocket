using System.Text.RegularExpressions;

namespace CursorPocket.Tests;

public sealed class MainPageContractTests
{
    [Fact]
    public void Library_spends_the_detail_pane_on_media_instead_of_chrome()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MainPage.xaml");
        var xaml = File.ReadAllText(path);

        Assert.Contains("<ColumnDefinition Width=\"35*\" MinWidth=\"280\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition x:Name=\"DetailColumn\" Width=\"65*\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"FilterBar\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsCompact=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ShowAndHideAutomatically=\"True\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IsCompact=\"False\"", xaml, StringComparison.Ordinal);
    }

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

    [Fact]
    public void Text_captures_open_and_edit_inside_the_library()
    {
        var xamlPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MainPage.xaml");
        var codePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MainPage.xaml.cs.txt");
        var xaml = File.ReadAllText(xamlPath);
        var code = File.ReadAllText(codePath);

        Assert.Contains("x:Name=\"DetailTextEditor\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TextEditActions\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"Open_Click\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Command=\"{x:Bind ViewModel.OpenSelectedCommand}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("OpenSelectedAsync", code, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumnSpan(DetailPane, 2)", code, StringComparison.Ordinal);
        Assert.Contains("BeginTextEdit", code, StringComparison.Ordinal);
        Assert.Matches(
            "BeginTextEditAsync[\\s\\S]*?EnsureTextDetailReadyAsync\\(item\\)[\\s\\S]*?_editingTextId = item.Id",
            code);
        Assert.Matches(
            "EnsureTextDetailReadyAsync[\\s\\S]*?SetPreviewMaximized\\(true\\)",
            code);
        Assert.Contains("await App.Services.CaptureStore.UpdateTextAsync", code, StringComparison.Ordinal);
    }

    [Fact]
    public void Text_editing_preserves_non_text_actions_and_restores_library_state()
    {
        var xamlPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MainPage.xaml");
        var codePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "MainPage.xaml.cs.txt");
        var xaml = File.ReadAllText(xamlPath);
        var code = File.ReadAllText(codePath);

        Assert.Matches(
            "CaptureKind != CaptureKind.Text[\\s\\S]*?OpenSelectedCommand.Execute\\(null\\)",
            code);
        Assert.Matches(
            "CaptureKind == CaptureKind.Screenshot[\\s\\S]*?AnnotateExisting\\(item.Record\\)",
            code);
        Assert.Contains("IsReadOnly=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DetailTextEditor.Text = await File.ReadAllTextAsync", code, StringComparison.Ordinal);
        Assert.Matches(
            "catch \\(Exception exception\\)[\\s\\S]*?DetailTextEditor.Text = item.Preview",
            code);

        foreach (var transition in new[]
        {
            "DetailTextEditor.IsReadOnly = false",
            "DefaultActions.Visibility = Visibility.Collapsed",
            "TextEditActions.Visibility = Visibility.Visible",
            "DeleteButton.Visibility = Visibility.Collapsed",
            "SetTextEditSurroundingsEnabled(false)",
            "DetailTextEditor.Text = _textBeforeEdit",
            "DetailTextEditor.IsReadOnly = true",
            "DefaultActions.Visibility = Visibility.Visible",
            "TextEditActions.Visibility = Visibility.Collapsed",
            "DeleteButton.Visibility = Visibility.Visible",
            "SetTextEditSurroundingsEnabled(true)",
            "CaptureList.IsEnabled = enabled",
            "button.IsEnabled = enabled",
            "CaptureNav.IsEnabled = enabled",
            "SettingsNav.IsEnabled = enabled",
            "MaximizePreviewButton.IsEnabled = enabled",
            "requireFullText: true",
            "_textDetailLoadedFromFile = true",
            "Text cannot be empty",
            "Text could not be saved",
        })
        {
            Assert.Contains(transition, code, StringComparison.Ordinal);
        }
    }
}
