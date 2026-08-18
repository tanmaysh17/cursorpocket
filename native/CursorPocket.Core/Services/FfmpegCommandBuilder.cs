using CursorPocket.Core.Models;

namespace CursorPocket.Core.Services;

public static class FfmpegCommandBuilder
{
    public static IReadOnlyList<string> Build(string ffmpegPath, string outputPath, RecordingOptions options)
    {
        if (options.FramesPerSecond is not (15 or 24 or 30 or 60))
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Video frame rate must be 15, 24, 30, or 60 fps.");
        }
        if (options.Quality is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Video quality must be between 1 and 100.");
        }
        if (options.IncludeMicrophone && string.IsNullOrWhiteSpace(options.MicrophoneName))
        {
            throw new ArgumentException("Choose a microphone or turn microphone recording off.", nameof(options));
        }
        if (options.IncludeCamera && string.IsNullOrWhiteSpace(options.CameraName))
        {
            throw new ArgumentException("Choose a camera or turn webcam recording off.", nameof(options));
        }

        var command = new List<string> { ffmpegPath, "-n", "-hide_banner", "-loglevel", "warning" };
        int? microphoneIndex = null;
        int? cameraIndex = null;
        var inputIndex = 0;

        if (options.IncludeMicrophone)
        {
            microphoneIndex = inputIndex++;
            command.AddRange(["-thread_queue_size", "2048", "-rtbufsize", "64M", "-f", "dshow", "-audio_buffer_size", "50", "-i", $"audio={options.MicrophoneName}"]);
        }
        if (options.IncludeCamera)
        {
            cameraIndex = inputIndex++;
            command.AddRange(["-thread_queue_size", "2048", "-rtbufsize", "256M", "-f", "dshow", "-video_size", "640x360", "-framerate", Math.Min(options.FramesPerSecond, 30).ToString(), "-i", $"video={options.CameraName}"]);
        }

        var screenIndex = inputIndex;
        var drawMouse = options.DrawCursor ? "1" : "0";
        string screenFilter;
        switch (options.SourceKind)
        {
            case VideoSourceKind.Display:
                command.AddRange(["-thread_queue_size", "1024", "-f", "lavfi", "-i", $"ddagrab=output_idx={Math.Max(0, options.DisplayIndex)}:framerate={options.FramesPerSecond}:draw_mouse={drawMouse}"]);
                screenFilter = $"[{screenIndex}:v]hwdownload,format=bgra,setpts=PTS-STARTPTS,format=yuv420p[screen]";
                break;
            case VideoSourceKind.Region:
                var bounds = NormalizeBounds(options.Bounds);
                command.AddRange(["-thread_queue_size", "1024", "-f", "gdigrab", "-framerate", options.FramesPerSecond.ToString(), "-draw_mouse", drawMouse, "-offset_x", bounds.Left.ToString(), "-offset_y", bounds.Top.ToString(), "-video_size", $"{bounds.Width}x{bounds.Height}", "-i", "desktop"]);
                screenFilter = $"[{screenIndex}:v]setpts=PTS-STARTPTS,format=yuv420p[screen]";
                break;
            case VideoSourceKind.Window:
                if (options.WindowHandle is null or 0)
                {
                    throw new ArgumentException("Choose a window to record.", nameof(options));
                }
                command.AddRange(["-thread_queue_size", "1024", "-f", "gdigrab", "-framerate", options.FramesPerSecond.ToString(), "-draw_mouse", drawMouse, "-i", $"hwnd={options.WindowHandle}"]);
                screenFilter = $"[{screenIndex}:v]setpts=PTS-STARTPTS,format=yuv420p[screen]";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(options));
        }

        var filters = new List<string> { screenFilter };
        if (cameraIndex is not null)
        {
            var cameraWidth = Math.Clamp(options.CameraWidth, 160, 640);
            var cameraHeight = Math.Max(90, (int)Math.Round(cameraWidth * 9d / 16d));
            cameraHeight -= cameraHeight % 2;
            filters.Add($"[{cameraIndex}:v]setpts=PTS-STARTPTS,scale={cameraWidth}:{cameraHeight},format=yuv420p[cam]");
            filters.Add($"[screen][cam]overlay={OverlayPosition(options.CameraPosition)}:shortest=0,format=nv12[video]");
        }
        else
        {
            filters.Add("[screen]format=nv12[video]");
        }
        if (microphoneIndex is not null)
        {
            filters.Add($"[{microphoneIndex}:a]asetpts=PTS-STARTPTS,aresample=48000:async=1:first_pts=0[audio]");
        }

        command.AddRange(["-filter_complex", string.Join(';', filters), "-map", "[video]"]);
        if (microphoneIndex is not null)
        {
            command.AddRange(["-map", "[audio]"]);
        }
        command.AddRange(["-c:v", "h264_mf", "-rate_control", "quality", "-quality", options.Quality.ToString(), "-g", (options.FramesPerSecond * 2).ToString(), "-fps_mode", "cfr"]);
        command.AddRange(microphoneIndex is not null ? ["-c:a", "aac", "-b:a", "128k"] : ["-an"]);
        command.AddRange(["-movflags", "+frag_keyframe+empty_moov+default_base_moof", "-metadata", "title=CursorPocket walkthrough", "-progress", "pipe:2", "-nostats", outputPath]);
        return command;
    }

    private static CaptureBounds NormalizeBounds(CaptureBounds? bounds)
    {
        if (bounds is null || bounds.Width < 2 || bounds.Height < 2)
        {
            throw new ArgumentException("A non-empty recording region is required.", nameof(bounds));
        }
        return bounds with
        {
            Right = bounds.Left + bounds.Width - bounds.Width % 2,
            Bottom = bounds.Top + bounds.Height - bounds.Height % 2,
        };
    }

    private static string OverlayPosition(string value) => value switch
    {
        "top-left" => "32:32",
        "top-right" => "W-w-32:32",
        "bottom-left" => "32:H-h-32",
        _ => "W-w-32:H-h-32",
    };
}
