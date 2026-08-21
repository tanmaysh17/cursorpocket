using System.Runtime.InteropServices.WindowsRuntime;
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

    /// <summary>
    /// Frame subtypes that arrive as a readable CPU bitmap. Matched case-insensitively
    /// because <c>MediaFrameFormat.Subtype</c> casing varies by driver.
    /// </summary>
    private static readonly HashSet<string> UncompressedSubtypes =
        new(StringComparer.OrdinalIgnoreCase) { "NV12", "YUY2", "ARGB32", "RGB32", "BGRA8", "RGB24", "YV12", "IYUV", "L8" };

    private volatile CameraEffectPipeline _pipeline;
    private SelfieSegmenter? _segmenter;
    private readonly Image _target;
    private readonly SoftwareBitmapSource _source = new();
    private readonly DispatcherQueue _dispatcher;

    private MediaFrameReader? _reader;
    private SoftwareBitmap? _output;
    private byte[] _packed = [];
    private byte[] _halved = [];
    private byte[] _cropped = [];
    /// <summary>
    /// Aspect (width / height) of the surface this renders into, so the frame can be
    /// cropped around the person instead of down the middle. Zero means no cropping.
    /// </summary>
    // Written from the UI thread on resize, read on the frame thread; double
    // cannot be volatile, so both sides go through Volatile/Interlocked.
    private double _targetAspect;
    private int _busy;
    /// <summary>
    /// Held only while the frame thread is inside the CPU work — reading the
    /// frame, running the pipeline (including ONNX inference), and writing the
    /// output bitmap. Teardown waits on *this*, never on <c>_busy</c>: _busy is
    /// released by the dispatcher continuation, so a UI-thread wait on it would
    /// block the very thread that has to run the release and self-deadlock.
    /// </summary>
    private int _processing;
    private int _framesArrived;
    private int _framesPresented;
    private volatile string? _skipReason;
    private int _frameIndex;
    private int _settingsRevision;
    private double _averageMilliseconds;
    private volatile bool _disposed;

    private CameraEffectRenderer(CameraEffectSettings settings, SelfieSegmenter? segmenter, Image target, DispatcherQueue dispatcher, double targetAspect)
    {
        _targetAspect = targetAspect;
        _segmenter = segmenter;
        _pipeline = new CameraEffectPipeline(settings, segmenter);
        _target = target;
        _dispatcher = dispatcher;
    }

    /// <summary>Whether blur/replacement can run (model present and loadable).</summary>
    public bool SegmentationAvailable => _pipeline.SegmentationAvailable;

    /// <summary>
    /// Updates the aspect the frame is cropped to, for when the surface is resized.
    /// </summary>
    public void SetTargetAspect(double targetAspect) =>
        Volatile.Write(ref _targetAspect, double.IsFinite(targetAspect) && targetAspect > 0 ? targetAspect : 0);

    /// <summary>Frames the reader has handed over since start.</summary>
    public int FramesArrived => Volatile.Read(ref _framesArrived);

    /// <summary>Frames that actually reached the screen.</summary>
    public int FramesPresented => Volatile.Read(ref _framesPresented);

    /// <summary>
    /// Why the last frame did not reach the screen, or null once one has. A blank
    /// preview is otherwise indistinguishable from a dead camera, so callers use
    /// this to say something truthful instead of showing nothing.
    /// </summary>
    public string? Diagnosis => FramesPresented > 0
        ? null
        : _skipReason ?? (FramesArrived == 0 ? "the camera has not delivered a frame" : null);

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
        DispatcherQueue dispatcher,
        double targetAspect = 0)
    {
        SelfieSegmenter? segmenter = null;
        if (settings.NeedsSegmentation || settings.TouchUpLevel > 0)
        {
            segmenter = SelfieSegmenter.TryCreate(SelfieSegmenter.ResolveModelPath());
        }
        var renderer = new CameraEffectRenderer(settings, segmenter, target, dispatcher, targetAspect);
        try
        {
            if (settings.BackgroundMode == CameraEffectSettings.BackgroundImage)
            {
                await LoadBackgroundImageAsync(renderer._pipeline, settings.BackgroundImagePath);
            }
            await SelectProcessingFormatAsync(frameSource);
            // Deliberately no requested subtype. Asking for Bgra8 here makes the
            // reader start successfully on cameras that only produce NV12, YUY2, or
            // MJPG and then deliver nothing at all — a preview that is blank forever
            // with no error. Take whatever the camera natively offers and convert in
            // software, which the frame loop already does.
            var reader = await capture.CreateFrameReaderAsync(frameSource);
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
                // Uncompressed only. A compressed format (MJPG, H264) is delivered
                // without a CPU bitmap, which the frame loop cannot read — the same
                // blank-preview failure as forcing a subtype the camera lacks.
                .Where(format => UncompressedSubtypes.Contains(format.Subtype))
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
            Interlocked.Increment(ref _framesArrived);
            using var frame = sender.TryAcquireLatestFrame();
            var bitmap = frame?.VideoMediaFrame?.SoftwareBitmap;
            if (bitmap is null)
            {
                // Never silent: a preview that stays blank has to be able to say why
                // rather than looking like a dead camera.
                _skipReason = frame is null
                    ? "no frame available from the reader"
                    : frame.VideoMediaFrame is null
                        ? "frame carried no video"
                        : "frame was GPU-backed with no CPU bitmap";
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

            // Crop to the surface's aspect around the tracked person. Doing it here
            // rather than leaving it to the Image's UniformToFill is the whole point:
            // XAML can only centre the crop on the frame, not on whoever is in it.
            var targetAspect = Volatile.Read(ref _targetAspect);
            if (targetAspect > 0)
            {
                var crop = CursorPocket.Core.Media.AutoFrameCrop.Compute(width, height, targetAspect, _pipeline.FocusX);
                if (crop.Width != width || crop.Height != height)
                {
                    EnsurePacked(ref _cropped, crop.Width, crop.Height);
                    CursorPocket.Core.Media.AutoFrameCrop.CopyCrop(packed, width, crop, _cropped);
                    packed = _cropped;
                    width = crop.Width;
                    height = crop.Height;
                }
            }

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
                        Interlocked.Increment(ref _framesPresented);
                        _skipReason = null;
                    }
                }
                catch (Exception error)
                {
                    // A torn-down source mid-shutdown is not worth a crash, but a
                    // preview that never appears has to leave a reason behind.
                    _skipReason = $"presenting the frame failed: {error.Message}";
                }
                finally
                {
                    Interlocked.Exchange(ref _busy, 0);
                }
            });
        }
        catch (Exception error)
        {
            // Never let a bad frame take down the preview; the next one retries.
            // But record why: a step that throws on *every* frame is otherwise
            // completely silent, and looks exactly like a dead camera. That is how
            // an InvalidCastException in the pixel copy went unnoticed.
            _skipReason = $"{error.GetType().Name} while processing the frame: {error.Message}";
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
                await reader.StopAsync().AsTask().ConfigureAwait(false);
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
        await WaitForFrameWorkAsync().ConfigureAwait(false);
        _output?.Dispose();
        _output = null;
        _segmenter?.Dispose();
        _segmenter = null;
        // Touching the Image needs the UI thread, and by now the window may be
        // closing. Best effort only: releasing the camera must not depend on it.
        _dispatcher.TryEnqueue(() =>
        {
            try
            {
                _target.Source = null;
            }
            catch (Exception)
            {
            }
        });
    }

    private async Task WaitForFrameWorkAsync()
    {
        for (var attempt = 0; attempt < 100 && Volatile.Read(ref _processing) != 0; attempt++)
        {
            await Task.Delay(5).ConfigureAwait(false);
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
    /// <summary>
    /// Copies a BGRA8 bitmap out to a packed array.
    /// <para>
    /// Uses <c>CopyToBuffer</c> rather than <c>LockBuffer</c> plus a hand-declared
    /// <c>IMemoryBufferByteAccess</c>. That older pattern does not work here: under
    /// CsWinRT the buffer reference arrives as a projected <c>WinRT.IInspectable</c>
    /// and casting it to a hand-declared COM-imported interface throws
    /// <see cref="InvalidCastException"/> on every frame. Because the frame loop
    /// deliberately swallows per-frame failures, that produced a preview that was
    /// blank forever while the camera light stayed on.
    /// </para>
    /// <para>
    /// The buffer is tightly packed — no stride padding — so the pipeline can treat
    /// it as width * 4 bytes per row throughout.
    /// </para>
    /// </summary>
    public static void Read(SoftwareBitmap bitmap, byte[] destination, int width, int height) =>
        bitmap.CopyToBuffer(destination.AsBuffer(0, width * height * 4));

    /// <summary>Copies a packed BGRA8 array back into a bitmap.</summary>
    public static void Write(SoftwareBitmap bitmap, byte[] source, int width, int height)
    {
        // The destination is Premultiplied and some cameras leave the fourth BGRA
        // byte undefined. A zero there renders the whole preview transparent, and an
        // invisible self-view means no webcam in the recording.
        var length = width * height * 4;
        for (var offset = 3; offset < length; offset += 4)
        {
            source[offset] = 255;
        }
        bitmap.CopyFromBuffer(source.AsBuffer(0, length));
    }
}
