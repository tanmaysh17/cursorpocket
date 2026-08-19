using CursorPocket.Core.Services;

namespace CursorPocket.Tests;

public sealed class FfmpegDeviceParserTests
{
    [Fact]
    public void ParsesLegacySectionedOutput()
    {
        const string output = "[dshow] DirectShow video devices\n[dshow] \"Desk camera\"\n[dshow] DirectShow audio devices\n[dshow] \"Desk mic\"";

        var devices = FfmpegDeviceParser.Parse(output);

        Assert.Equal("Desk camera", Assert.Single(devices.Video).Name);
        Assert.Equal("Desk mic", Assert.Single(devices.Audio).Name);
    }

    [Fact]
    public void ParsesModernKindMarkersWithoutSectionHeaders()
    {
        const string output = "[dshow] \"Webcam\" (video)\n[dshow] \"Microphone\" (audio)\n[dshow] Alternative name \"@device_pnp_ignored\"";

        var devices = FfmpegDeviceParser.Parse(output);

        Assert.Equal("Webcam", Assert.Single(devices.Video).Name);
        Assert.Equal("Microphone", Assert.Single(devices.Audio).Name);
    }

    [Fact]
    public void TreatsFfmpegEightNoneMarkerAsVideoFallback()
    {
        const string output = "[in#0] \"Integrated Webcam\" (none)\n[in#0] Alternative name \"@device_pnp_ignored\"";

        var devices = FfmpegDeviceParser.Parse(output);

        Assert.Equal("Integrated Webcam", Assert.Single(devices.Video).Name);
        Assert.Empty(devices.Audio);
    }
}
