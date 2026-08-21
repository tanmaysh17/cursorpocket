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
    private double _focusX = 0.5;

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
                UpdateFocus(width, height);
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
            // Tuned down from 4x/radius 2/3 passes, which read as frosted glass and
            // erased the room rather than de-emphasising it. Halving the downscale
            // is what does most of the work: the blur radius is applied to a buffer
            // twice as wide, so its reach in full-resolution terms drops with it.
            BuildSoftened(pixels, width, height, downscaleFactor: 2, blurRadius: 2, iterations: 2, _background);
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

    /// <summary>
    /// Where to centre the crop when the self-view's shape is a different aspect
    /// than the camera: the smoothed horizontal centre of the person, or 0.5 when
    /// there is no mask to go on. Fed to <see cref="AutoFrameCrop.Compute"/>.
    /// </summary>
    public double FocusX => _focusX;

    private void UpdateFocus(int width, int height)
    {
        var centroid = AutoFrameCrop.HorizontalCentroid(_alpha, width, height);
        if (centroid is null)
        {
            return;
        }
        // Heavily smoothed: the crop window moving is far more noticeable than the
        // mask wobbling, so it should drift rather than track.
        _focusX = _focusX * 0.92 + centroid.Value * 0.08;
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
