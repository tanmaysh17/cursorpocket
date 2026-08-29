using System.Globalization;
using System.Text.RegularExpressions;

namespace CursorPocket.Tests;

public sealed class LightThemeContractTests
{
    [Theory]
    [InlineData("PocketInk", "PocketBase", 7.0)]
    [InlineData("PocketInkDim", "PocketBase", 4.5)]
    [InlineData("PocketMuted", "PocketBase", 4.5)]
    [InlineData("PocketGreen", "PocketBase", 4.5)]
    [InlineData("PocketOnGreen", "PocketGreen", 4.5)]
    [InlineData("PocketRed", "PocketBase", 4.5)]
    [InlineData("PocketBlue", "PocketBase", 4.5)]
    public void Light_theme_text_roles_meet_their_contrast_floor(
        string foregroundKey,
        string backgroundKey,
        double minimumRatio)
    {
        var light = ThemeBody("Light");
        var foreground = SolidColour(light, foregroundKey);
        var background = SolidColour(light, backgroundKey);

        Assert.True(
            ContrastRatio(foreground, background) >= minimumRatio,
            $"{foregroundKey} on {backgroundKey} must be at least {minimumRatio:0.0}:1.");
    }

    [Theory]
    [InlineData("PocketMediaInk", 7.0)]
    [InlineData("PocketMediaInkDim", 7.0)]
    [InlineData("PocketMediaMuted", 4.5)]
    public void Always_dark_media_chrome_keeps_chalk_text_in_light_mode(
        string foregroundKey,
        double minimumRatio)
    {
        var foreground = SolidColour(ThemeBody("Light"), foregroundKey);
        var denseFallback = Rgb.Parse("09110F");

        Assert.True(
            ContrastRatio(foreground, denseFallback) >= minimumRatio,
            $"{foregroundKey} must remain readable on the dense Pine media surface.");
    }

    [Fact]
    public void Common_resources_do_not_shadow_the_active_theme_dictionary()
    {
        var app = ReadFixture("App.xaml");
        var commonStart = app.IndexOf("</ResourceDictionary.ThemeDictionaries>", StringComparison.Ordinal);
        var commonEnd = app.IndexOf("<!--  Annotation ink.", commonStart, StringComparison.Ordinal);
        Assert.True(commonStart >= 0 && commonEnd > commonStart);
        var common = app[commonStart..commonEnd];

        foreach (var key in new[]
        {
            "PocketInk", "PocketInkDim", "PocketMuted", "PocketBase",
            "PocketSunken", "PocketSurface", "PocketRaised", "PocketGreen",
            "PocketRed", "PocketBlue", "PocketGlassPanel", "PocketGlassDense",
        })
        {
            Assert.DoesNotContain($"x:Key=\"{key}\"", common, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Native_controls_share_the_cursorpocket_state_accent()
    {
        var app = ReadFixture("App.xaml");

        Assert.Contains("x:Key=\"NavigationViewSelectionIndicatorForeground\" ResourceKey=\"PocketControlAccent\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ToggleSwitchFillOn\" ResourceKey=\"PocketControlAccent\"", app, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SliderTrackValueFill\" ResourceKey=\"PocketControlAccent\"", app, StringComparison.Ordinal);
        Assert.Contains("ApplyControlAccents();", ReadFixture("ThemeCoordinator.cs.txt"), StringComparison.Ordinal);
    }

    [Fact]
    public void Light_theme_programmatic_surfaces_resolve_fresh_theme_brushes()
    {
        var main = ReadFixture("MainPage.xaml.cs.txt");

        Assert.DoesNotContain("var resources = Application.Current.Resources;", main, StringComparison.Ordinal);
        Assert.Contains("button.Foreground = App.Theme.Brush(selected ? \"PocketInk\" : \"PocketMuted\")", main, StringComparison.Ordinal);
        Assert.Contains("ApplyFilterSelection(ViewModel.SelectedFilter);", main, StringComparison.Ordinal);
        Assert.Contains("ApplyThemeModeSelection();", main, StringComparison.Ordinal);
    }

    [Fact]
    public void Content_backed_dark_surfaces_use_media_chrome_tokens()
    {
        var hud = ReadFixture("RecordingHudWindow.xaml");
        var preflight = ReadFixture("VideoPreflightWindow.xaml");
        var pin = ReadFixture("PinnedCaptureWindow.xaml");

        Assert.Contains("PocketMediaInk", hud, StringComparison.Ordinal);
        Assert.Contains("PocketMediaRed", hud, StringComparison.Ordinal);
        Assert.Contains("PocketMediaMuted", preflight, StringComparison.Ordinal);
        Assert.Contains("PocketMediaLine", preflight, StringComparison.Ordinal);
        Assert.Contains("PocketMediaRaised", pin, StringComparison.Ordinal);
        Assert.Contains("PocketMediaInk", pin, StringComparison.Ordinal);
    }

    private static string ThemeBody(string theme)
    {
        var app = ReadFixture("App.xaml");
        var match = Regex.Match(
            app,
            $"<ResourceDictionary x:Key=\"{Regex.Escape(theme)}\">(?<body>[\\s\\S]*?)</ResourceDictionary>",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"The {theme} theme dictionary was not found.");
        return match.Groups["body"].Value;
    }

    private static Rgb SolidColour(string themeBody, string key)
    {
        var match = Regex.Match(
            themeBody,
            $"<SolidColorBrush x:Key=\"{Regex.Escape(key)}\" Color=\"#(?<argb>[0-9A-Fa-f]{{8}})\"",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success, $"The solid colour {key} was not found.");
        return Rgb.Parse(match.Groups["argb"].Value[2..]);
    }

    private static double ContrastRatio(Rgb first, Rgb second)
    {
        var lighter = Math.Max(first.Luminance, second.Luminance);
        var darker = Math.Min(first.Luminance, second.Luminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private readonly record struct Rgb(byte Red, byte Green, byte Blue)
    {
        public static Rgb Parse(string rgb) => new(
            byte.Parse(rgb[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(rgb.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(rgb.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));

        public double Luminance =>
            (0.2126 * Linear(Red)) + (0.7152 * Linear(Green)) + (0.0722 * Linear(Blue));

        private static double Linear(byte channel)
        {
            var value = channel / 255d;
            return value <= 0.04045
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
    }

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
