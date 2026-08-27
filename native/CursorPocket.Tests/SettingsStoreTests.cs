using CursorPocket.Core.Models;
using CursorPocket.Core.Services;
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
        Assert.Equal(MouseGestureSensitivity.Balanced, settings.MouseGestureSensitivity);
        Assert.Equal(GlassTransparencyLevel.Balanced, settings.GlassTransparency);
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
    public async Task CircleGestureSensitivityRoundTripsAndInvalidValuesNormalizeToBalanced()
    {
        var path = Path.Combine(_root, "gesture", "settings.json");
        var store = new SettingsStore(path);

        await store.SaveAsync(new AppSettings { MouseGestureSensitivity = MouseGestureSensitivity.High });
        Assert.Equal(MouseGestureSensitivity.High, (await store.LoadAsync()).MouseGestureSensitivity);

        var repaired = SettingsStore.Normalize(new AppSettings
        {
            MouseGestureSensitivity = (MouseGestureSensitivity)999,
        });
        Assert.Equal(MouseGestureSensitivity.Balanced, repaired.MouseGestureSensitivity);

        await File.WriteAllTextAsync(path, """
            {"capture_dir":"C:\\Still Here","mouse_gesture_sensitivity":"Extreme"}
            """);
        var invalidName = await store.LoadAsync();
        Assert.Equal(@"C:\Still Here", invalidName.CaptureDirectory);
        Assert.Equal(MouseGestureSensitivity.Balanced, invalidName.MouseGestureSensitivity);
    }

    [Fact]
    public async Task GlassTransparencyRoundTripsAndInvalidValuesNormalizeToBalanced()
    {
        var path = Path.Combine(_root, "glass", "settings.json");
        var store = new SettingsStore(path);

        await store.SaveAsync(new AppSettings { GlassTransparency = GlassTransparencyLevel.Clear });
        Assert.Equal(GlassTransparencyLevel.Clear, (await store.LoadAsync()).GlassTransparency);

        var repaired = SettingsStore.Normalize(new AppSettings
        {
            GlassTransparency = (GlassTransparencyLevel)999,
        });
        Assert.Equal(GlassTransparencyLevel.Balanced, repaired.GlassTransparency);

        await File.WriteAllTextAsync(path, """
            {"capture_dir":"C:\\Still Here","glass_transparency":"Invisible"}
            """);
        var invalidName = await store.LoadAsync();
        Assert.Equal(@"C:\Still Here", invalidName.CaptureDirectory);
        Assert.Equal(GlassTransparencyLevel.Balanced, invalidName.GlassTransparency);
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

    [Fact]
    public async Task Legacy_onboarding_completion_migrates_to_the_current_version()
    {
        var path = Path.Combine(_root, "onboarding-migration.json");
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(path, "{\"onboarding_seen\":true}");

        var settings = await new SettingsStore(path).LoadAsync();

        Assert.Equal(OnboardingFlow.CurrentVersion, settings.OnboardingVersion);
        Assert.True(settings.OnboardingSeen);
    }

    [Fact]
    public async Task Update_checks_default_on_and_persist_the_last_success()
    {
        var path = Path.Combine(_root, "updates.json");
        var checkedAt = new DateTimeOffset(2026, 8, 23, 12, 30, 0, TimeSpan.Zero);
        var store = new SettingsStore(path);

        Assert.True((await store.LoadAsync()).AutomaticallyCheckForUpdates);
        await store.SaveAsync(new AppSettings
        {
            AutomaticallyCheckForUpdates = false,
            LastUpdateCheckAt = checkedAt,
        });
        var reloaded = await store.LoadAsync();

        Assert.False(reloaded.AutomaticallyCheckForUpdates);
        Assert.Equal(checkedAt, reloaded.LastUpdateCheckAt);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }
}
