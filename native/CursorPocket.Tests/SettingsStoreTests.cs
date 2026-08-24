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
    public async Task RepairsOutOfRangeCameraEffectSettings()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "settings.json");
        await File.WriteAllTextAsync(path, """
            {"video_camera_shape":"octagon","video_camera_background":"hologram","video_camera_touch_up":9,"video_camera_brightness":5000,"video_camera_warmth":-5000,"video_camera_contrast":-101}
            """);

        var settings = await new SettingsStore(path).LoadAsync();

        Assert.Equal("rounded", settings.VideoCameraShape);
        Assert.Equal("none", settings.VideoCameraBackground);
        Assert.Equal(2, settings.VideoCameraTouchUp);
        Assert.Equal(100, settings.VideoCameraBrightness);
        Assert.Equal(-100, settings.VideoCameraWarmth);
        Assert.Equal(-100, settings.VideoCameraContrast);
    }

    /// <summary>
    /// Selecting an image background with no image to show would run
    /// segmentation on every frame and then composite nothing.
    /// </summary>
    [Fact]
    public void AnImageBackgroundWithNoImageIsNotARealSelection()
    {
        var repaired = SettingsStore.Normalize(new AppSettings
        {
            VideoCameraBackground = "image",
            VideoCameraBackgroundImage = "   ",
        });

        Assert.Equal("none", repaired.VideoCameraBackground);

        var kept = SettingsStore.Normalize(new AppSettings
        {
            VideoCameraBackground = "image",
            VideoCameraBackgroundImage = "asset:graphite",
        });

        Assert.Equal("image", kept.VideoCameraBackground);
    }

    [Fact]
    public void CameraEffectsAndAudioCleanupAreOffByDefault()
    {
        var settings = SettingsStore.Normalize(new AppSettings());

        Assert.Equal("rounded", settings.VideoCameraShape);
        Assert.Equal("none", settings.VideoCameraBackground);
        Assert.Equal(0, settings.VideoCameraTouchUp);
        Assert.Equal(0, settings.VideoCameraBrightness);
        Assert.False(settings.AudioNoiseSuppression);
        Assert.False(settings.AudioAutoLevel);
    }

    [Fact]
    public async Task KeepsValidCameraEffectChoices()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "settings.json");
        var store = new SettingsStore(path);
        var expected = new AppSettings
        {
            VideoCameraShape = "squircle",
            VideoCameraBackground = "blur",
            VideoCameraTouchUp = 1,
            VideoCameraBrightness = 40,
            AudioNoiseSuppression = true,
            AudioAutoLevel = true,
        };

        await store.SaveAsync(expected);
        var actual = await store.LoadAsync();

        Assert.Equal("squircle", actual.VideoCameraShape);
        Assert.Equal("blur", actual.VideoCameraBackground);
        Assert.Equal(1, actual.VideoCameraTouchUp);
        Assert.Equal(40, actual.VideoCameraBrightness);
        Assert.True(actual.AudioNoiseSuppression);
        Assert.True(actual.AudioAutoLevel);
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
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp"));
    }

    [Fact]
    public async Task OnboardingCompletionPersistsAcrossLaunches()
    {
        var path = Path.Combine(_root, "onboarding", "settings.json");
        var store = new SettingsStore(path);

        Assert.False((await store.LoadAsync()).OnboardingSeen);

        await store.SaveAsync(new AppSettings { OnboardingSeen = true });

        Assert.True((await store.LoadAsync()).OnboardingSeen);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
