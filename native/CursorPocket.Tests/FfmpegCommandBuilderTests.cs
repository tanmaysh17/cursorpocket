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
            DisplayOutputIndex = 0,
        }).ToList();

        Assert.True(command.IndexOf("audio=Desk mic") < command.FindIndex(value => value.StartsWith("ddagrab=")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void DesktopDuplicationUsesTheResolvedDxgiOutputIndex(int outputIndex)
    {
        var command = FfmpegCommandBuilder.Build("ffmpeg.exe", "output.mp4", new RecordingOptions
        {
            IncludeMicrophone = false,
            DisplayOutputIndex = outputIndex,
            Bounds = new CaptureBounds(1920, 0, 3840, 1080),
        }).ToList();

        Assert.Contains(command, value => value.Contains($"ddagrab=output_idx={outputIndex}:", StringComparison.Ordinal));
    }

    [Fact]
    public void ADisplayWithoutAUsableOutputIndexGrabsItsOwnRectangle()
    {
        // A monitor on another adapter has no output index on the default one. The
        // recording still has to be of that screen, so its rectangle is grabbed —
        // this is the case that used to silently record a different display.
        var command = FfmpegCommandBuilder.Build("ffmpeg.exe", "output.mp4", new RecordingOptions
        {
            IncludeMicrophone = false,
            DisplayOutputIndex = null,
            Bounds = new CaptureBounds(-1920, -120, 0, 960),
        }).ToList();

        Assert.DoesNotContain(command, value => value.StartsWith("ddagrab=", StringComparison.Ordinal));
        Assert.Contains("gdigrab", command);
        Assert.Equal("-1920", command[command.IndexOf("-offset_x") + 1]);
        Assert.Equal("-120", command[command.IndexOf("-offset_y") + 1]);
        Assert.Equal("1920x1080", command[command.IndexOf("-video_size") + 1]);
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
