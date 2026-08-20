using System.Runtime.InteropServices;
using CursorPocket.Core.Media;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;

namespace CursorPocket_App.Services;

/// <summary>
/// Drives the effect-enabled camera preview: MediaFrameReader → Core
/// <see cref="CameraEffectPipeline"/> → SoftwareBitmapSource on an Image.
/// Shared by the recording self-view and the preflight preview so both show
/// exactly what will land in the file.
/// <para>
/// Latest-frame-wins: FrameArrived is gated by an interlocked flag that is
/// only released after the processed frame reaches the screen, so a slow
/// frame drops camera input instead of building up latency. When the average
/// frame cost climbs, inference is skipped on interleaved frames first (the
/// smoothed mask barely moves between frames) before anything visible drops.
/// </para>
/// </summary>
public sealed class CameraEffectRenderer : IDisposable
{
    private const int TargetProcessingWidth = 640;

    private volatile CameraEffectPipeline _pipeline;
    private SelfieSegmenter? _segmenter;
    private readonly Image _target;
    private readonly SoftwareBitmapSource _source = new();
    private readonly DispatcherQueue _dispatcher;

    private MediaFrameReader? _reader;
    private SoftwareBitmap? _output;
    private byte[] _packed = [];
    private byte[] _halved = [];
    private int _busy;
    /// <summary>
    /// Held only while the frame thread is inside the CPU work — reading the
    /// frame, running the pipeline (including ONNX inference), and writing the
    /// output bitmap. Teardown waits on *this*, never on <c>_busy</c>: _busy is
    /// released by the dispatcher continuation, so a UI-thread wait on it would
    /// block the very thread that has to run the release and self-deadlock.
    /// </summary>
    private int _processing;
    private int _frameIndex;
    private int _settingsRevision;
    private double _averageMilliseconds;
    private volatile bool _disposed;

    private CameraEffectRenderer(CameraEffectSettings settings, SelfieSegmenter? segmenter, Image target, DispatcherQueue dispatcher)
    {
        _segmenter = segmenter;
        _pipeline = new CameraEffectPipeline(settings, segmenter);
        _target = target;
        _dispatcher = dispatcher;
    }

    /// <summary>Whether blur/replacement can run (model present and loadable).</summary>
    public bool SegmentationAvailable => _pipeline.SegmentationAvailable;

    /// <summary>
    /// Swaps in a fresh pipeline mid-preview so the preflight controls update
    /// the live picture without reopening the camera. Cheap: a LUT rebuild and
    /// a few buffers that re-grow on the next frame.
    /// </summary>
    public async Task UpdateSettingsAsync(CameraEffectSettings settings)
    {
        // Loading a replacement image awaits a decode, so two quick changes can
        // finish out of order. The revision check makes the last request the
        // one that wins rather than the one that happened to decode fastest.
        var revision = Interlocked.Increment(ref _settingsRevision);
        if ((settings.NeedsSegmentation || settings.TouchUpLevel > 0) && _segmenter is null)
        {
            _segmenter = SelfieSegmenter.TryCreate(SelfieSegmenter.ResolveModelPath());
        }
        var pipeline = new CameraEffectPipeline(settings, _segmenter);
        if (settings.BackgroundMode == CameraEffectSettings.BackgroundImage)
        {
            await LoadBackgroundImageAsync(pipeline, settings.BackgroundImagePath);
        }
        if (Volatile.Read(ref _settingsRevision) == revision && !_disposed)
        {
            _pipeline = pipeline;
        }
    }

    /// <summary>
    /// Starts rendering the frame source into <paramref name="target"/>.
    /// Returns null when a frame reader cannot be created — the caller falls
    /// back to the plain MediaPlayer preview, exactly the pre-effects behavior.
    /// </summary>
    public static async Task<CameraEffectRenderer?> StartAsync(
        MediaCapture capture,
        MediaFrameSource frameSource,
        CameraEffectSettings settings,
        Image target,
        DispatcherQueue dispatcher)
    {
        SelfieSegmenter? segmenter = null;
        if (settings.NeedsSegmentation || settings.TouchUpLevel > 0)
        {
            segmenter = SelfieSegmenter.TryCreate(SelfieSegmenter.ResolveModelPath());
        }
        var renderer = new CameraEffectRenderer(settings, segmenter, target, dispatcher);
        try
        {
            if (settings.BackgroundMode == CameraEffectSettings.BackgroundImage)
            {
                await LoadBackgroundImageAsync(renderer._pipeline, settings.BackgroundImagePath);
            }
            await SelectProcessingFormatAsync(frameSource);
            var reader = await capture.CreateFrameReaderAsync(frameSource, MediaEncodingSubtypes.Bgra8);
            reader.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
            reader.FrameArrived += renderer.Reader_FrameArrived;
            renderer._reader = reader;
            if (await reader.StartAsync() != MediaFrameReaderStartStatus.Success)
            {
                renderer.Dispose();
                return null;
            }
            target.Source = renderer._source;
            return renderer;
        }
        catch (Exception)
        {
            renderer.Dispose();
            return null;
        }
    }

    /// <summary>
    /// The camera's native format can be far larger than the self-view window;
    /// ask for the smallest stream that still covers the processing width so
    /// the per-frame cost stays flat regardless of the camera.
    /// </summary>
    private static async Task SelectProcessingFormatAsync(MediaFrameSource frameSource)
    {
        try
        {
            var best = frameSource.SupportedFormats
                .Where(format => format.VideoFormat is not null && FrameRateOf(format) >= 15)
                .Where(format => format.VideoFormat.Width >= 320)
                .OrderBy(format => format.VideoFormat.Width < TargetProcessingWidth ? 1 : 0)
                .ThenBy(format => format.VideoFormat.Width)
                .ThenByDescending(FrameRateOf)
                .FirstOrDefault();
            if (best is not null && frameSource.CurrentFormat?.VideoFormat?.Width != best.VideoFormat.Width)
            {
                await frameSource.SetFormatAsync(best);
            }
        }
        catch (Exception)
        {
            // Keep the camera's current format; oversized frames are halved in software.
        }
    }

    private static double FrameRateOf(MediaFrameFormat format) =>
        format.FrameRate is { Denominator: > 0 } rate ? rate.Numerator / (double)rate.Denominator : 0;

    private void Reader_FrameArrived(MediaFrameReader sender, MediaFrameArrivedEventArgs args)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
        {
            return;
        }
        var released = false;
        Interlocked.Exchange(ref _processing, 1);
        try
        {
            if (_disposed)
            {
                return;
            }
            using var frame = sender.TryAcquireLatestFrame();
            var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
            if (bitmap is null)
            {
                return;
            }
            var started = Environment.TickCount64;
            using var bgra = bitmap.BitmapPixelFormat == BitmapPixelFormat.Bgra8
                ? null
                : SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var working = bgra ?? bitmap;

            var width = working.PixelWidth;
            var height = working.PixelHeight;
            EnsurePacked(ref _packed, width, height);
            SoftwareBitmapPixels.Read(working, _packed, width, height);

            var packed = _packed;
            // Format negotiation can fail; halve oversized frames so the effect
            // cost never scales with whatever the camera happened to deliver.
            if (width > 800)
            {
                EnsurePacked(ref _halved, width / 2 + 1, height / 2 + 1);
                PixelResizer.Downscale(_packed, width, height, 2, _halved, out width, out height);
                packed = _halved;
            }

            _frameIndex++;
            _pipeline.Process(packed.AsSpan(0, width * height * 4), width, height, runInference: _frameIndex % InferenceInterval() == 0);

            var output = _output;
            if (output is null || output.PixelWidth != width || output.PixelHeight != height)
            {
                output?.Dispose();
                output = new SoftwareBitmap(BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
                _output = output;
            }
            SoftwareBitmapPixels.Write(output, packed, width, height);

            var elapsed = Environment.TickCount64 - started;
            _averageMilliseconds = _averageMilliseconds <= 0 ? elapsed : _averageMilliseconds * 0.9 + elapsed * 0.1;

            // The CPU work is finished, so teardown is free to release the
            // segmenter and the output bitmap from here on.
            Interlocked.Exchange(ref _processing, 0);
            released = _dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    var current = _output;
                    if (!_disposed && current is not null)
                    {
                        await _source.SetBitmapAsync(current);
                    }
                }
                catch (Exception)
                {
                    // A torn-down source mid-shutdown is not worth a crash.
                }
                finally
                {
                    Interlocked.Exchange(ref _busy, 0);
                }
            });
        }
        catch (Exception)
        {
            // Never let a bad frame take down the preview; the next one retries.
        }
        finally
        {
            Interlocked.Exchange(ref _processing, 0);
            if (!released)
            {
                Interlocked.Exchange(ref _busy, 0);
            }
        }
    }

    /// <summary>Frames per inference: 1 normally, stretched as the average frame cost climbs.</summary>
    private int InferenceInterval() => _averageMilliseconds switch
    {
        > 60 => 3,
        > 40 => 2,
        _ => 1,
    };

    private static void EnsurePacked(ref byte[] buffer, int width, int height)
    {
        var required = width * height * 4;
        if (buffer.Length < required)
        {
            buffer = new byte[required];
        }
    }

    private static async Task LoadBackgroundImageAsync(CameraEffectPipeline pipeline, string path)
    {
        try
        {
            var resolved = ResolveBackgroundPath(path);
            if (resolved is null || !File.Exists(resolved))
            {
                return;
            }
            using var stream = File.OpenRead(resolved);
            var decoder = await BitmapDecoder.CreateAsync(stream.AsRandomAccessStream());
            using var decoded = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            var pixels = new byte[decoded.PixelWidth * decoded.PixelHeight * 4];
            SoftwareBitmapPixels.Read(decoded, pixels, decoded.PixelWidth, decoded.PixelHeight);
            pipeline.SetBackgroundImage(pixels, decoded.PixelWidth, decoded.PixelHeight);
        }
        catch (Exception)
        {
            // An unreadable image simply leaves the background unreplaced.
        }
    }

    /// <summary>Bundled backgrounds are addressed as <c>asset:name</c> so installs relocate cleanly.</summary>
    public static string? ResolveBackgroundPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }
        if (path.StartsWith("asset:", StringComparison.OrdinalIgnoreCase))
        {
            var name = path["asset:".Length..];
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return null;
            }
            return Path.Combine(AppContext.BaseDirectory, "Assets", "Backgrounds", name + ".png");
        }
        return path;
    }

    /// <summary>
    /// Shuts the preview down and does not return until the camera is actually
    /// released. Callers must await this before opening the same device again —
    /// DirectShow grants a single consumer exclusive use, so returning early is
    /// what makes the next preview or self-view find the camera busy.
    /// </summary>
    public async Task DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        var reader = _reader;
        _reader = null;
        if (reader is not null)
        {
            reader.FrameArrived -= Reader_FrameArrived;
            try
            {
                await reader.StopAsync();
            }
            catch (Exception)
            {
                // A device yanked mid-recording still has to be disposed below.
            }
            reader.Dispose();
        }
        // A frame dispatched just before the unsubscribe may still be inside the
        // pipeline — including native ONNX inference. Releasing the session or
        // the output bitmap under it is a use-after-free, so wait it out.
        await WaitForFrameWorkAsync();
        _output?.Dispose();
        _output = null;
        _segmenter?.Dispose();
        _segmenter = null;
        _target.Source = null;
    }

    private async Task WaitForFrameWorkAsync()
    {
        for (var attempt = 0; attempt < 100 && Volatile.Read(ref _processing) != 0; attempt++)
        {
            await Task.Delay(5);
        }
    }

    /// <summary>
    /// Best-effort teardown for paths that cannot await, such as a window's
    /// Closed handler. Prefer <see cref="DisposeAsync"/> anywhere the same
    /// camera is about to be reopened.
    /// </summary>
    public void Dispose() => _ = DisposeAsync();
}

/// <summary>
/// Stride-aware copies between a SoftwareBitmap and a packed BGRA array. The
/// unsafe pointer work is confined here so the pipeline stays pure.
/// </summary>
internal static class SoftwareBitmapPixels
{
    [ComImport]
    [Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }

    public static unsafe void Read(SoftwareBitmap bitmap, byte[] destination, int width, int height)
    {
        using var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Read);
        using var reference = buffer.CreateReference();
        ((IMemoryBufferByteAccess)reference).GetBuffer(out var data, out _);
        var plane = buffer.GetPlaneDescription(0);
        for (var row = 0; row < height; row++)
        {
            new ReadOnlySpan<byte>(data + plane.StartIndex + row * plane.Stride, width * 4)
                .CopyTo(destination.AsSpan(row * width * 4));
        }
    }

    public static unsafe void Write(SoftwareBitmap bitmap, byte[] source, int width, int height)
    {
        using var buffer = bitmap.LockBuffer(BitmapBufferAccessMode.Write);
        using var reference = buffer.CreateReference();
        ((IMemoryBufferByteAccess)reference).GetBuffer(out var data, out _);
        var plane = buffer.GetPlaneDescription(0);
        for (var row = 0; row < height; row++)
        {
            var destination = new Span<byte>(data + plane.StartIndex + row * plane.Stride, width * 4);
            source.AsSpan(row * width * 4, width * 4).CopyTo(destination);
            // The destination is Premultiplied and some cameras leave the fourth
            // BGRA byte undefined. Passing a zero through would render the whole
            // preview transparent — and an invisible self-view means no webcam
            // in the recording.
            for (var offset = 3; offset < destination.Length; offset += 4)
            {
                destination[offset] = 255;
            }
        }
    }
}
