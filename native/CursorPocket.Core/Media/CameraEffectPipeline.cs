namespace CursorPocket.Core.Media;

/// <summary>
/// Runs the enabled camera effects over one packed BGRA frame at a time:
/// color adjustment → person mask → touch-up → background blur/replacement.
/// Pure CPU math; the only external dependency is the injected
/// <see cref="IPersonMaskModel"/>, so the whole pipeline is unit-testable.
/// Not thread-safe — the renderer processes one frame at a time.
/// </summary>
public sealed class CameraEffectPipeline
{
    private readonly CameraEffectSettings _settings;
    private readonly IPersonMaskModel? _model;
    private readonly ColorAdjustLut? _lut;
    private readonly float[]? _inputTensor;
    private readonly float[]? _rawMask;
    private readonly MaskPostprocessor? _maskPostprocessor;

    private int _width;
    private int _height;
    private float[] _alpha = [];
    private byte[] _softened = [];
    private byte[] _small = [];
    private byte[] _smallScratch = [];
    private byte[] _background = [];
    private byte[]? _backgroundImage;
    private byte[]? _backgroundImageSource;
    private int _backgroundImageSourceWidth;
    private int _backgroundImageSourceHeight;

    public CameraEffectPipeline(CameraEffectSettings settings, IPersonMaskModel? model)
    {
        _settings = settings;
        _model = model;
        if (settings.HasColorAdjustment)
        {
            _lut = new ColorAdjustLut(settings.Brightness, settings.Contrast, settings.Warmth);
        }
        if (model is not null && (settings.NeedsSegmentation || settings.TouchUpLevel > 0))
        {
            _inputTensor = new float[model.InputSize * model.InputSize * 3];
            _rawMask = new float[model.InputSize * model.InputSize];
            _maskPostprocessor = new MaskPostprocessor(model.InputSize);
        }
    }

    /// <summary>Whether background blur/replacement can actually run.</summary>
    public bool SegmentationAvailable => _maskPostprocessor is not null;

    /// <summary>Supplies the decoded replacement image (packed BGRA) for "image" mode.</summary>
    public void SetBackgroundImage(byte[] packedBgra, int width, int height)
    {
        _backgroundImageSource = packedBgra;
        _backgroundImageSourceWidth = width;
        _backgroundImageSourceHeight = height;
        _backgroundImage = null;
    }

    /// <summary>
    /// Processes one packed BGRA frame in place. <paramref name="runInference"/>
    /// lets the caller skip the model on some frames (the smoothed mask from the
    /// previous frames is reused), which is the first degradation step when the
    /// CPU cannot keep up.
    /// </summary>
    public void Process(Span<byte> pixels, int width, int height, bool runInference = true)
    {
        EnsureBuffers(width, height);
        _lut?.Apply(pixels, width, height, width * 4);

        var hasMask = false;
        if (_maskPostprocessor is not null && _model is not null && _inputTensor is not null && _rawMask is not null)
        {
            if (runInference || !_maskPostprocessor.HasMask)
            {
                SegmentationPreprocessor.FillTensor(pixels, width, height, _model.InputSize, _inputTensor, _model.ChannelsFirst);
                if (_model.TryGetMask(_inputTensor, _rawMask))
                {
                    _maskPostprocessor.Accumulate(_rawMask);
                }
            }
            if (_maskPostprocessor.HasMask)
            {
                _maskPostprocessor.Resolve(_alpha, width, height);
                hasMask = true;
            }
        }

        if (_settings.TouchUpLevel > 0)
        {
            BuildSoftened(pixels, width, height, downscaleFactor: 2, blurRadius: 2, iterations: 1, _softened);
            TouchUpFilter.Apply(
                pixels,
                _softened,
                hasMask ? _alpha : [],
                width,
                height,
                TouchUpFilter.StrengthFor(_settings.TouchUpLevel, hasMask));
        }

        // Without a usable mask the safest output is the frame untouched:
        // blurring or replacing everything would erase the user, so skip
        // instead. This covers both "inference failed" and "the mask came back
        // empty because nobody is in frame" (see MaskPostprocessor.MinimumCoverage).
        if (!hasMask || !_settings.NeedsSegmentation)
        {
            return;
        }
        if (_settings.BackgroundMode == CameraEffectSettings.BackgroundBlur)
        {
            BuildSoftened(pixels, width, height, downscaleFactor: 4, blurRadius: 2, iterations: 3, _background);
            MaskCompositor.Composite(pixels, _background, _alpha, width, height);
        }
        else if (_settings.BackgroundMode == CameraEffectSettings.BackgroundImage && _backgroundImageSource is not null)
        {
            _backgroundImage ??= PixelResizer.CropToFill(
                _backgroundImageSource,
                _backgroundImageSourceWidth,
                _backgroundImageSourceHeight,
                width,
                height);
            MaskCompositor.Composite(pixels, _backgroundImage, _alpha, width, height);
        }
    }

    private void BuildSoftened(ReadOnlySpan<byte> pixels, int width, int height, int downscaleFactor, int blurRadius, int iterations, byte[] destination)
    {
        PixelResizer.Downscale(pixels, width, height, downscaleFactor, _small, out var smallWidth, out var smallHeight);
        BoxBlur.Apply(_small.AsSpan(0, smallWidth * smallHeight * 4), smallWidth, smallHeight, blurRadius, _smallScratch, iterations);
        PixelResizer.UpscaleBilinear(_small.AsSpan(0, smallWidth * smallHeight * 4), smallWidth, smallHeight, destination, width, height);
    }

    private void EnsureBuffers(int width, int height)
    {
        if (width == _width && height == _height)
        {
            return;
        }
        _width = width;
        _height = height;
        var pixelCount = width * height;
        _alpha = new float[pixelCount];
        _softened = new byte[pixelCount * 4];
        _background = new byte[pixelCount * 4];
        // Sized for the smallest downscale factor in use (2).
        var smallCount = (width / 2 + 1) * (height / 2 + 1) * 4;
        _small = new byte[smallCount];
        _smallScratch = new byte[smallCount];
        _backgroundImage = null;
    }
}
