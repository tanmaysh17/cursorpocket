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
    private readonly SemaphoreSlim _generationGate = new(1, 1);

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

        await _generationGate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(target))
            {
                return target;
            }

            var temporary = Path.Combine(store.PreviewDirectory, $"{record.Id}.{Guid.NewGuid():N}.tmp.png");
            try
            {
                var created = record.CaptureKind switch
                {
                    CaptureKind.Video => await CreateVideoPosterAsync(path, temporary, cancellationToken),
                    CaptureKind.Audio => await CreateWaveformAsync(path, temporary, cancellationToken),
                    _ => false,
                };
                if (!created || !File.Exists(temporary))
                {
                    return null;
                }

                try
                {
                    File.Move(temporary, target, false);
                }
                catch (IOException) when (File.Exists(target))
                {
                    // Another completed request won the atomic publish race.
                }
                return File.Exists(target) ? target : null;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A preview is derived UI data. The capture remains valid and
                // the app must stay usable if a codec or GDI resource fails.
                return null;
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { }
            }
        }
        finally
        {
            _generationGate.Release();
        }
    }

    private static async Task<bool> CreateWaveformAsync(string input, string output, CancellationToken cancellationToken)
    {
        await Task.Run(() => CreateWaveform(input, output), cancellationToken);
        return File.Exists(output);
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
