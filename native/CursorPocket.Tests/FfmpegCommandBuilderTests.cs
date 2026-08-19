using CursorPocket.Core.Models;
using CursorPocket.Core.Services;

namespace CursorPocket.Tests;

public sealed class FfmpegCommandBuilderTests
{
    [Fact]
    public void OpensTheMicrophoneBeforeDesktopDuplication()
    {
        var command = FfmpegCommandBuilder.Build("ffmpeg.exe", "output.mp4", new RecordingOptions
        {
            IncludeMicrophone = true,
            MicrophoneName = "Desk mic",
        }).ToList();

        Assert.True(command.IndexOf("audio=Desk mic") < command.FindIndex(value => value.StartsWith("ddagrab=")));
    }

    [Fact]
    public void TheCameraIsNeverAnFfmpegInput()
    {
        // CursorPocket owns the camera and shows a live self-view inside the
        // recorded area instead. Handing the same device to FFmpeg's dshow demuxer
        // would take it away from that preview, since DirectShow grants exclusive use.
        var command = FfmpegCommandBuilder.Build("ffmpeg.exe", "output.mp4", new RecordingOptions
        {
            IncludeMicrophone = true,
            MicrophoneName = "Desk mic",
            IncludeCamera = true,
            CameraName = "Studio camera",
            CameraPosition = "top-left",
            CameraWidth = 480,
        }).ToList();

        Assert.DoesNotContain("video=Studio camera", command);
        Assert.DoesNotContain(command, value => value.Contains("overlay=", StringComparison.Ordinal));
        Assert.DoesNotContain(command, value => value.Contains("[cam]", StringComparison.Ordinal));
        Assert.Contains(command, value => value.Contains("[screen]format=nv12[video]", StringComparison.Ordinal));
    }

    [Fact]
    public void ANamedCameraIsStillRequiredForTheSelfView()
    {
        Assert.Throws<ArgumentException>(() => FfmpegCommandBuilder.Build("ffmpeg.exe", "output.mp4", new RecordingOptions
        {
            IncludeCamera = true,
            CameraName = "   ",
        }));
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
