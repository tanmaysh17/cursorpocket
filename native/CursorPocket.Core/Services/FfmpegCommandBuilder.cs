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
        var inputIndex = 0;

        if (options.IncludeMicrophone)
        {
            microphoneIndex = inputIndex++;
            command.AddRange(["-thread_queue_size", "2048", "-rtbufsize", "64M", "-f", "dshow", "-audio_buffer_size", "50", "-i", $"audio={options.MicrophoneName}"]);
        }
        // The camera is deliberately not an FFmpeg input. CursorPocket owns the
        // device and shows a live self-view inside the recorded area, so the webcam
        // reaches the file through the screen capture and the user can see it while
        // recording. See CameraSelfViewPlacement.
        var screenIndex = inputIndex;
        var drawMouse = options.DrawCursor ? "1" : "0";
        string screenFilter;
        switch (options.SourceKind)
        {
            case VideoSourceKind.Display when options.DisplayOutputIndex is int outputIndex:
                // output_idx is a DXGI output index resolved from the monitor itself.
                // Never pass a monitor enumeration index here: the two orderings
                // disagree, which silently records a different screen.
                command.AddRange(["-thread_queue_size", "1024", "-f", "lavfi", "-i", $"ddagrab=output_idx={Math.Max(0, outputIndex)}:framerate={options.FramesPerSecond}:draw_mouse={drawMouse}"]);
                screenFilter = $"[{screenIndex}:v]hwdownload,format=bgra,setpts=PTS-STARTPTS,format=yuv420p[screen]";
                break;
            case VideoSourceKind.Display when options.Bounds is null:
                // Neither an output index nor a rectangle: fall back to the first
                // output rather than failing, which is what callers got before a
                // monitor could be identified at all.
                command.AddRange(["-thread_queue_size", "1024", "-f", "lavfi", "-i", $"ddagrab=output_idx=0:framerate={options.FramesPerSecond}:draw_mouse={drawMouse}"]);
                screenFilter = $"[{screenIndex}:v]hwdownload,format=bgra,setpts=PTS-STARTPTS,format=yuv420p[screen]";
                break;
            case VideoSourceKind.Display:
                // Desktop Duplication cannot address this monitor — it hangs off
                // another adapter, or DXGI would not identify it. Grab its exact
                // rectangle so the correct screen is still what gets recorded.
                var display = NormalizeBounds(options.Bounds);
                command.AddRange(["-thread_queue_size", "1024", "-f", "gdigrab", "-framerate", options.FramesPerSecond.ToString(), "-draw_mouse", drawMouse, "-offset_x", display.Left.ToString(), "-offset_y", display.Top.ToString(), "-video_size", $"{display.Width}x{display.Height}", "-i", "desktop"]);
                screenFilter = $"[{screenIndex}:v]setpts=PTS-STARTPTS,format=yuv420p[screen]";
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

        var filters = new List<string> { screenFilter, "[screen]format=nv12[video]" };
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

}
