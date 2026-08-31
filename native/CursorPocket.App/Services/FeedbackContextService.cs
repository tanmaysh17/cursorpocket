using System.Runtime.InteropServices;
using System.Text;
using CursorPocket.Core.Feedback;
using CursorPocket.Core.Models;
using Microsoft.UI.Xaml;

namespace CursorPocket_App.Services;

internal static class FeedbackContextService
{
    private const int MaximumCrashLogBytesToRead = 32 * 1024;

    public static FeedbackSystemContext CreateSystemContext(Window window, string appVersion)
    {
        var scale = WindowPlacement.ScaleFor(window);
        return new FeedbackSystemContext(
            appVersion,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            (int)Math.Round(scale * 100));
    }

    public static FeedbackDiagnosticExcerpt? TryReadRecentCrash(AppSettings settings)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CursorPocket",
                "crash.log");
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var start = Math.Max(0, stream.Length - MaximumCrashLogBytesToRead);
            stream.Seek(start, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            if (start > 0)
            {
                _ = reader.ReadLine();
            }
            var logTail = reader.ReadToEnd();
            return FeedbackDiagnostics.FromCrashLog(
                logTail,
                [
                    Environment.UserName,
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    AppContext.BaseDirectory,
                    settings.CaptureDirectory,
                    settings.VideoMicrophoneName,
                    settings.VideoCameraName,
                    "captures.jsonl",
                    "settings.json",
                ]);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
