using System.Text.RegularExpressions;
using CursorPocket.Core.Models;

namespace CursorPocket.Core.Services;

public static partial class FfmpegDeviceParser
{
    public static (IReadOnlyList<MediaDeviceDescriptor> Audio, IReadOnlyList<MediaDeviceDescriptor> Video) Parse(string output)
    {
        var audio = new List<MediaDeviceDescriptor>();
        var video = new List<MediaDeviceDescriptor>();
        string? section = null;

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Contains("DirectShow video devices", StringComparison.OrdinalIgnoreCase))
            {
                section = "video";
                continue;
            }
            if (line.Contains("DirectShow audio devices", StringComparison.OrdinalIgnoreCase))
            {
                section = "audio";
                continue;
            }

            var match = QuotedDeviceName().Match(line);
            if (!match.Success || line.Contains("Alternative name", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = match.Groups["name"].Value;
            var explicitKind = line.Contains("(video)", StringComparison.OrdinalIgnoreCase)
                ? "video"
                : line.Contains("(audio)", StringComparison.OrdinalIgnoreCase)
                    ? "audio"
                    : line.Contains("(none)", StringComparison.OrdinalIgnoreCase)
                        ? "video"
                    : section;

            if (explicitKind == "video" && video.All(device => !string.Equals(device.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                video.Add(new MediaDeviceDescriptor(name, name, "video", video.Count == 0));
            }
            else if (explicitKind == "audio" && audio.All(device => !string.Equals(device.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                audio.Add(new MediaDeviceDescriptor(name, name, "audio", audio.Count == 0));
            }
        }

        return (audio, video);
    }

    [GeneratedRegex("\\\"(?<name>[^\\\"]+)\\\"")]
    private static partial Regex QuotedDeviceName();
}
