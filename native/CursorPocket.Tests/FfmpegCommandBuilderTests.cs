using CursorPocket.Core.Models;
using CursorPocket.Core.Services;

namespace CursorPocket.Tests;

public sealed class FfmpegCommandBuilderTests
{
    [Fact]
    public void OpensPhysicalDevicesBeforeDesktopDuplication()
    {
        var command = FfmpegCommandBuilder.Build("ffmpeg.exe", "output.mp4", new RecordingOptions
        {
            IncludeMicrophone = true,
            MicrophoneName = "Desk mic",
            IncludeCamera = true,
            CameraName = "Studio camera",
        }).ToList();

        Assert.True(command.IndexOf("audio=Desk mic") < command.FindIndex(value => value.StartsWith("ddagrab=")));
        Assert.True(command.IndexOf("video=Studio camera") < command.FindIndex(value => value.StartsWith("ddagrab=")));
        Assert.Contains(command, value => value.Contains("[screen][cam]overlay=W-w-32:H-h-32:shortest=0,format=nv12[video]", StringComparison.Ordinal));
    }

    [Fact]
    public void RegionPreservesNegativeCoordinatesAndEvenDimensions()
    {
        var command = FfmpegCommandBuilder.Build("ffmpeg.exe", "output.mp4", new RecordingOptions
        {
            SourceKind = VideoSourceKind.Region,
            Bounds = new CaptureBounds(-1919, -9, 3, 1082),
            IncludeMicrophone = false,
        }).ToList();

        Assert.Equal("-1919", command[command.IndexOf("-offset_x") + 1]);
        Assert.Equal("-9", command[command.IndexOf("-offset_y") + 1]);
        Assert.Equal("1922x1090", command[command.IndexOf("-video_size") + 1]);
        Assert.Contains("-an", command);
    }
}
