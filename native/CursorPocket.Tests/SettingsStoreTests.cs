using CursorPocket.Core.Models;
using CursorPocket.Core.Storage;

namespace CursorPocket.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CursorPocket.Settings.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task LoadsLegacySettingsAndNormalizesInvalidChoices()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "settings.json");
        await File.WriteAllTextAsync(path, """
            {"capture_dir":"C:\\Captures","follow_cursor":false,"video_fps":99,"video_source_kind":"unknown","video_camera_position":"middle"}
            """);

        var settings = await new SettingsStore(path).LoadAsync();

        Assert.Equal(@"C:\Captures", settings.CaptureDirectory);
        Assert.Equal(30, settings.VideoFramesPerSecond);
        Assert.Equal("display", settings.VideoSourceKind);
        Assert.Equal("bottom-right", settings.VideoCameraPosition);
        Assert.Equal("off", settings.CursorCompanionMode);
    }

    [Fact]
    public async Task SaveIsAtomicAndRoundTrips()
    {
        var path = Path.Combine(_root, "nested", "settings.json");
        var store = new SettingsStore(path);
        var expected = new AppSettings { VideoCameraEnabled = true, ActivationShortcut = "Win+Alt+Space" };

        await store.SaveAsync(expected);
        var actual = await store.LoadAsync();

        Assert.True(actual.VideoCameraEnabled);
        Assert.Equal("Win+Alt+Space", actual.ActivationShortcut);
        Assert.False(File.Exists(path + ".tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
