namespace CursorPocket.Core.Media;

/// <summary>
/// The seam between the pure pipeline and the ONNX runtime. The app implements
/// this over an ONNX session; tests implement it with canned masks.
/// </summary>
public interface IPersonMaskModel
{
    /// <summary>Square side of the tensor the model expects (256 for the selfie segmenter).</summary>
    int InputSize { get; }

    /// <summary>Whether the model wants NCHW input (PyTorch exports) rather than NHWC.</summary>
    bool ChannelsFirst { get; }

    /// <summary>
    /// Runs inference on an RGB float tensor (values 0..1, laid out per
    /// <see cref="ChannelsFirst"/> by <see cref="SegmentationPreprocessor"/>)
    /// and writes person probability 0..1 per pixel into <paramref name="mask"/>
    /// (InputSize² floats, row-major). Returns false when inference is
    /// unavailable; the pipeline then degrades rather than throwing.
    /// </summary>
    bool TryGetMask(ReadOnlySpan<float> inputTensor, Span<float> mask);
}

/// <summary>Packs a BGRA frame into the model's square RGB tensor (values 0..1).</summary>
public static class SegmentationPreprocessor
{
    /// <param name="channelsFirst">true = NCHW (PyTorch exports), false = NHWC.</param>
    public static void FillTensor(ReadOnlySpan<byte> bgra, int width, int height, int inputSize, Span<float> tensor, bool channelsFirst = true)
    {
        var xRatio = (width - 1d) / Math.Max(1, inputSize - 1);
        var yRatio = (height - 1d) / Math.Max(1, inputSize - 1);
        var plane = inputSize * inputSize;
        for (var y = 0; y < inputSize; y++)
        {
            var sourceY = (int)(y * yRatio);
            for (var x = 0; x < inputSize; x++)
            {
                var sourceIndex = (sourceY * width + (int)(x * xRatio)) * 4;
                var pixel = y * inputSize + x;
                var red = bgra[sourceIndex + 2] / 255f;
                var green = bgra[sourceIndex + 1] / 255f;
                var blue = bgra[sourceIndex] / 255f;
                if (channelsFirst)
                {
                    tensor[pixel] = red;
                    tensor[plane + pixel] = green;
                    tensor[2 * plane + pixel] = blue;
                }
                else
                {
                    tensor[pixel * 3] = red;
                    tensor[pixel * 3 + 1] = green;
                    tensor[pixel * 3 + 2] = blue;
                }
            }
        }
    }
}

/// <summary>
/// Turns raw per-frame masks into a stable full-resolution alpha plane:
/// exponential moving average against the previous mask (kills flicker), a
/// small feather (kills edge shimmer), then bilinear upscale to frame size.
/// </summary>
public sealed class MaskPostprocessor
{
    /// <summary>
    /// Least of the frame the person must cover for the mask to be trusted.
    /// The segmenter correctly returns an all-but-empty mask when nobody is in
    /// frame (covered lens, very dark room, user stepped away). Compositing
    /// against that mask would replace the whole picture and erase the user, so
    /// a mask this sparse is treated as no mask at all and the real camera
    /// image is left alone. A person filling less than this of a webcam
    /// self-view is not a usable self-view anyway.
    /// </summary>
    public const float MinimumCoverage = 0.04f;

    private readonly int _maskSize;
    private readonly float[] _smoothed;
    private readonly float[] _scratch;
    private bool _hasHistory;
    private float _coverage;

    public MaskPostprocessor(int maskSize)
    {
        _maskSize = maskSize;
        _smoothed = new float[maskSize * maskSize];
        _scratch = new float[maskSize * maskSize];
    }

    public void Accumulate(ReadOnlySpan<float> mask)
    {
        if (!_hasHistory)
        {
            mask.CopyTo(_smoothed);
            _hasHistory = true;
        }
        else
        {
            for (var index = 0; index < _smoothed.Length; index++)
            {
                _smoothed[index] = 0.5f * mask[index] + 0.5f * _smoothed[index];
            }
        }
        BoxBlur.FeatherPlane(_smoothed, _maskSize, _maskSize, 1, _scratch);

        var total = 0f;
        foreach (var value in _smoothed)
        {
            total += value;
        }
        _coverage = total / _smoothed.Length;
    }

    /// <summary>
    /// Whether a mask has been accumulated that is actually usable — present
    /// and covering a plausible amount of the frame (see <see cref="MinimumCoverage"/>).
    /// </summary>
    public bool HasMask => _hasHistory && _coverage >= MinimumCoverage;

    /// <summary>How much of the frame the smoothed mask currently claims, 0..1.</summary>
    public float Coverage => _hasHistory ? _coverage : 0f;

    public void Reset()
    {
        _hasHistory = false;
        _coverage = 0f;
    }

    /// <summary>Writes the current smoothed mask, upscaled to width×height, into <paramref name="alpha"/>.</summary>
    public void Resolve(Span<float> alpha, int width, int height) =>
        PixelResizer.ResamplePlane(_smoothed, _maskSize, _maskSize, alpha, width, height);
}
