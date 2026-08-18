using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using CursorPocket.Core.Models;
using CursorPocket.Core.Storage;
using NAudio.Wave;

namespace CursorPocket_App.Services;

public sealed class PreviewService(CaptureStore store, string ffmpegPath)
{
    public async Task<string?> GetPreviewAsync(CaptureRecord record, CancellationToken cancellationToken = default)
    {
        var path = store.AbsolutePath(record);
        if (!File.Exists(path))
        {
            return null;
        }
        if (record.CaptureKind == CaptureKind.Screenshot)
        {
            return path;
        }
        Directory.CreateDirectory(store.PreviewDirectory);
        var target = Path.Combine(store.PreviewDirectory, record.Id + ".png");
        if (File.Exists(target))
        {
            return target;
        }
        if (record.CaptureKind == CaptureKind.Video)
        {
            return await CreateVideoPosterAsync(path, target, cancellationToken) ? target : null;
        }
        if (record.CaptureKind == CaptureKind.Audio)
        {
            await Task.Run(() => CreateWaveform(path, target), cancellationToken);
            return File.Exists(target) ? target : null;
        }
        return null;
    }

    private async Task<bool> CreateVideoPosterAsync(string input, string output, CancellationToken cancellationToken)
    {
        if (!File.Exists(ffmpegPath))
        {
            return false;
        }
        var info = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "-y", "-hide_banner", "-loglevel", "error", "-ss", "0.25", "-i", input, "-frames:v", "1", "-vf", "scale=640:-2", output })
        {
            info.ArgumentList.Add(argument);
        }
        using var process = Process.Start(info);
        if (process is null)
        {
            return false;
        }
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode == 0 && File.Exists(output);
    }

    private static void CreateWaveform(string input, string output)
    {
        const int width = 960;
        const int height = 240;
        using var reader = new AudioFileReader(input);
        var totalSamples = Math.Max(1, reader.Length / sizeof(float));
        var samplesPerColumn = Math.Max(1, totalSamples / width);
        var buffer = new float[Math.Min(samplesPerColumn, 8192)];
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.FromArgb(255, 13, 19, 23));
        using var glowPen = new Pen(Color.FromArgb(70, 67, 224, 141), 7);
        using var linePen = new Pen(Color.FromArgb(255, 67, 224, 141), 2);
        for (var x = 0; x < width; x++)
        {
            var remaining = samplesPerColumn;
            var peak = 0f;
            while (remaining > 0)
            {
                var count = ((ISampleProvider)reader).Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (count == 0)
                {
                    break;
                }
                for (var index = 0; index < count; index++)
                {
                    peak = Math.Max(peak, Math.Abs(buffer[index]));
                }
                remaining -= count;
            }
            var magnitude = Math.Max(2, peak * (height * 0.42f));
            graphics.DrawLine(glowPen, x, height / 2f - magnitude, x, height / 2f + magnitude);
            graphics.DrawLine(linePen, x, height / 2f - magnitude, x, height / 2f + magnitude);
        }
        bitmap.Save(output, ImageFormat.Png);
    }
}
