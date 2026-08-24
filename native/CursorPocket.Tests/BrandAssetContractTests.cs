using System.Buffers.Binary;
using System.Drawing;

namespace CursorPocket.Tests;

public sealed class BrandAssetContractTests
{
    [Theory]
    [InlineData("AppIcon.ico", "16,20,24,32,48,64,128,256")]
    [InlineData("AppIconRecording.ico", "16,20,24,32,48,64,128,256")]
    [InlineData("TrayReady.ico", "16,20,24,32,48,64")]
    [InlineData("TrayRecording.ico", "16,20,24,32,48,64")]
    public void Runtime_icons_contain_every_required_frame(string filename, string expectedCsv)
    {
        var bytes = File.ReadAllBytes(Fixture(filename));
        Assert.True(bytes.Length >= 6);
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0, 2)));
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2, 2)));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4, 2));
        Assert.True(bytes.Length >= 6 + count * 16);

        var frames = new List<int>();
        for (var index = 0; index < count; index++)
        {
            var entry = bytes.AsSpan(6 + index * 16, 16);
            var width = entry[0] == 0 ? 256 : entry[0];
            var height = entry[1] == 0 ? 256 : entry[1];
            Assert.Equal(width, height);
            frames.Add(width);
        }

        var expected = expectedCsv.Split(',').Select(int.Parse).ToArray();
        Assert.Equal(expected, frames.Order().ToArray());
    }

    [Theory]
    [InlineData("AppIcon.ico", "AppIconRecording.ico")]
    [InlineData("TrayReady.ico", "TrayRecording.ico")]
    public void Brand_logo_one_is_the_stable_installed_identity(string first, string second)
    {
        Assert.Equal(File.ReadAllBytes(Fixture(first)), File.ReadAllBytes(Fixture(second)));
    }

    [Theory]
    [InlineData("CursorPocketLogo.png", 256, 256)]
    [InlineData("SplashScreen.scale-200.png", 1240, 600)]
    [InlineData("Square150x150Logo.scale-200.png", 300, 300)]
    [InlineData("Square44x44Logo.scale-200.png", 88, 88)]
    [InlineData("StoreLogo.png", 50, 50)]
    [InlineData("Wide310x150Logo.scale-200.png", 620, 300)]
    public void Runtime_pngs_match_windows_asset_dimensions(string filename, int width, int height)
    {
        using var image = Image.FromFile(Fixture(filename));

        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        Assert.True(Image.IsAlphaPixelFormat(image.PixelFormat));
    }

    [Fact]
    public void Dark_runtime_tokens_match_the_approved_brand_palette()
    {
        var xaml = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "App.xaml"));
        var coordinator = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ThemeCoordinator.cs.txt"));
        var companion = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NativeCompanionWindow.cs.txt"));

        foreach (var hex in new[] { "F6F4EC", "CBD7D1", "8EA099", "07130F", "36E58C", "FF5964", "7AA7FF" })
        {
            Assert.Contains(hex, xaml, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Contains("#CBD7D1", coordinator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#36E58C", coordinator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#FF5964", coordinator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#7AA7FF", coordinator, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#FF5964", companion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Production_msix_manifest_consumes_the_branded_splash()
    {
        var manifest = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AppxManifest.xml"));

        Assert.Contains("<uap:SplashScreen", manifest, StringComparison.Ordinal);
        Assert.Contains("Assets\\SplashScreen.png", manifest, StringComparison.Ordinal);
        Assert.Contains("#07130F", manifest, StringComparison.OrdinalIgnoreCase);
    }

    private static string Fixture(string filename) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Brand", filename);
}
