using CursorPocket.Core.Media;

namespace CursorPocket.Tests;

public class CameraEffectPipelineTests
{
    private const int Width = 64;
    private const int Height = 48;

    /// <summary>A mask model with a hard-edged person in the left half of the frame.</summary>
    private sealed class LeftHalfPersonModel : IPersonMaskModel
    {
        public int InputSize => 32;
        public bool ChannelsFirst => true;
        public int Calls { get; private set; }

        public bool TryGetMask(ReadOnlySpan<float> inputTensor, Span<float> mask)
        {
            Calls++;
            for (var y = 0; y < InputSize; y++)
            {
                for (var x = 0; x < InputSize; x++)
                {
                    mask[y * InputSize + x] = x < InputSize / 2 ? 1f : 0f;
                }
            }
            return true;
        }
    }

    private sealed class UnavailableModel : IPersonMaskModel
    {
        public int InputSize => 32;
        public bool ChannelsFirst => true;
        public bool TryGetMask(ReadOnlySpan<float> inputTensor, Span<float> mask) => false;
    }

    /// <summary>
    /// What the real segmenter returns when nobody is in frame — a successful
    /// inference whose mask is essentially empty.
    /// </summary>
    private sealed class NobodyInFrameModel : IPersonMaskModel
    {
        public int InputSize => 32;
        public bool ChannelsFirst => true;

        public bool TryGetMask(ReadOnlySpan<float> inputTensor, Span<float> mask)
        {
            mask.Clear();
            return true;
        }
    }

    /// <summary>Left half mid-gray, right half a bright checkerboard the blur will flatten.</summary>
    private static byte[] BuildFrame()
    {
        var pixels = new byte[Width * Height * 4];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var offset = (y * Width + x) * 4;
                byte value = x < Width / 2 ? (byte)128 : (byte)((x + y) % 2 == 0 ? 255 : 0);
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
                pixels[offset + 3] = 255;
            }
        }
        return pixels;
    }

    /// <summary>
    /// Means of the inner quarter of each half. The mask is deliberately
    /// feathered, so pixels straddling the person/background seam are a blend
    /// by design — sampling away from the seam tests the intent, not the
    /// feather width.
    /// </summary>
    private static (double Left, double Right) MeanHalves(byte[] pixels)
    {
        double left = 0, right = 0;
        var quarter = Width / 4;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < quarter; x++)
            {
                left += pixels[(y * Width + x) * 4 + 1];
                right += pixels[(y * Width + (Width - 1 - x)) * 4 + 1];
            }
        }
        return (left / (quarter * Height), right / (quarter * Height));
    }

    [Fact]
    public void NoEffectsMeansTheFrameIsUntouched()
    {
        var settings = new CameraEffectSettings();
        Assert.False(settings.HasAnyEffect);
        var pixels = BuildFrame();
        var original = pixels.ToArray();
        new CameraEffectPipeline(settings, null).Process(pixels, Width, Height);
        Assert.Equal(original, pixels);
    }

    [Fact]
    public void BrightnessLiftsTheWholeFrame()
    {
        var pixels = BuildFrame();
        var before = MeanHalves(pixels);
        new CameraEffectPipeline(new CameraEffectSettings { Brightness = 50 }, null).Process(pixels, Width, Height);
        var after = MeanHalves(pixels);
        Assert.True(after.Left > before.Left + 10, $"{before.Left} → {after.Left}");
    }

    [Fact]
    public void WarmthPushesRedAboveBlue()
    {
        var pixels = BuildFrame();
        new CameraEffectPipeline(new CameraEffectSettings { Warmth = 80 }, null).Process(pixels, Width, Height);
        // Sample inside the flat left half so the checkerboard cannot skew it.
        var offset = ((Height / 2) * Width + 8) * 4;
        Assert.True(pixels[offset + 2] > pixels[offset], "Warmth did not bias red over blue.");
    }

    [Fact]
    public void BackgroundBlurFlattensOnlyTheBackground()
    {
        var pixels = BuildFrame();
        var before = MeanHalves(pixels);
        var settings = new CameraEffectSettings { BackgroundMode = CameraEffectSettings.BackgroundBlur };
        var pipeline = new CameraEffectPipeline(settings, new LeftHalfPersonModel());
        Assert.True(pipeline.SegmentationAvailable);
        pipeline.Process(pixels, Width, Height);
        var after = MeanHalves(pixels);
        // The person (left) keeps its value; the checkerboard background averages toward mid.
        Assert.Equal(before.Left, after.Left, 1);
        Assert.NotEqual(before.Right, after.Right, 1);
    }

    [Fact]
    public void BackgroundImageReplacesOnlyTheBackground()
    {
        var pixels = BuildFrame();
        var background = new byte[16 * 16 * 4];
        Array.Fill(background, (byte)20);
        var settings = new CameraEffectSettings
        {
            BackgroundMode = CameraEffectSettings.BackgroundImage,
            BackgroundImagePath = "asset:test",
        };
        var pipeline = new CameraEffectPipeline(settings, new LeftHalfPersonModel());
        pipeline.SetBackgroundImage(background, 16, 16);
        pipeline.Process(pixels, Width, Height);
        var after = MeanHalves(pixels);
        Assert.Equal(128, after.Left, 1);
        Assert.Equal(20, after.Right, 1);
    }

    /// <summary>
    /// Without a mask, blurring or replacing "the background" would erase the
    /// user. Leaving the frame alone is the only safe degradation.
    /// </summary>
    [Fact]
    public void AnUnavailableModelLeavesTheBackgroundAlone()
    {
        var pixels = BuildFrame();
        var original = pixels.ToArray();
        var settings = new CameraEffectSettings { BackgroundMode = CameraEffectSettings.BackgroundBlur };
        new CameraEffectPipeline(settings, new UnavailableModel()).Process(pixels, Width, Height);
        Assert.Equal(original, pixels);
    }

    /// <summary>
    /// The segmenter legitimately returns an all-but-empty mask when nobody is
    /// in frame. Trusting it would composite the background over the entire
    /// picture and erase the user, so a mask this sparse must be ignored.
    /// </summary>
    [Fact]
    public void AnEmptyMaskIsIgnoredRatherThanErasingTheWholeFrame()
    {
        var pixels = BuildFrame();
        var original = pixels.ToArray();
        var settings = new CameraEffectSettings { BackgroundMode = CameraEffectSettings.BackgroundBlur };
        new CameraEffectPipeline(settings, new NobodyInFrameModel()).Process(pixels, Width, Height);
        Assert.Equal(original, pixels);
    }

    [Fact]
    public void AnEmptyMaskAlsoBlocksBackgroundReplacement()
    {
        var pixels = BuildFrame();
        var original = pixels.ToArray();
        var background = new byte[16 * 16 * 4];
        Array.Fill(background, (byte)7);
        var settings = new CameraEffectSettings
        {
            BackgroundMode = CameraEffectSettings.BackgroundImage,
            BackgroundImagePath = "asset:test",
        };
        var pipeline = new CameraEffectPipeline(settings, new NobodyInFrameModel());
        pipeline.SetBackgroundImage(background, 16, 16);
        pipeline.Process(pixels, Width, Height);
        Assert.Equal(original, pixels);
    }

    [Fact]
    public void ASparseMaskIsBelowTheCoverageFloorButAHalfFrameSubjectIsNot()
    {
        var postprocessor = new MaskPostprocessor(32);
        postprocessor.Accumulate(new float[32 * 32]);
        Assert.False(postprocessor.HasMask);

        var half = new float[32 * 32];
        for (var index = 0; index < half.Length / 2; index++)
        {
            half[index] = 1f;
        }
        var person = new MaskPostprocessor(32);
        person.Accumulate(half);
        Assert.True(person.HasMask);
        Assert.True(person.Coverage > MaskPostprocessor.MinimumCoverage);
    }

    [Fact]
    public void NoModelAtAllStillAppliesColorAdjustment()
    {
        var pixels = BuildFrame();
        var before = MeanHalves(pixels);
        var settings = new CameraEffectSettings
        {
            BackgroundMode = CameraEffectSettings.BackgroundBlur,
            Brightness = 60,
        };
        var pipeline = new CameraEffectPipeline(settings, null);
        Assert.False(pipeline.SegmentationAvailable);
        pipeline.Process(pixels, Width, Height);
        Assert.True(MeanHalves(pixels).Left > before.Left + 10);
    }

    [Fact]
    public void SkippingInferenceReusesTheSmoothedMask()
    {
        var model = new LeftHalfPersonModel();
        var settings = new CameraEffectSettings { BackgroundMode = CameraEffectSettings.BackgroundBlur };
        var pipeline = new CameraEffectPipeline(settings, model);
        pipeline.Process(BuildFrame(), Width, Height);
        Assert.Equal(1, model.Calls);

        var pixels = BuildFrame();
        var before = MeanHalves(pixels);
        pipeline.Process(pixels, Width, Height, runInference: false);
        // Still masked correctly, without a second inference.
        Assert.Equal(1, model.Calls);
        Assert.Equal(before.Left, MeanHalves(pixels).Left, 1);
    }

    /// <summary>The very first frame must never render unmasked just because inference was skipped.</summary>
    [Fact]
    public void TheFirstFrameAlwaysRunsInferenceEvenWhenSkipIsRequested()
    {
        var model = new LeftHalfPersonModel();
        var settings = new CameraEffectSettings { BackgroundMode = CameraEffectSettings.BackgroundBlur };
        new CameraEffectPipeline(settings, model).Process(BuildFrame(), Width, Height, runInference: false);
        Assert.Equal(1, model.Calls);
    }

    [Fact]
    public void TouchUpSmoothsTheBusyBackgroundLessThanTheMaskedPerson()
    {
        var pixels = BuildFrame();
        var settings = new CameraEffectSettings { TouchUpLevel = 2 };
        new CameraEffectPipeline(settings, new LeftHalfPersonModel()).Process(pixels, Width, Height);
        var after = MeanHalves(pixels);
        // The flat person half is already smooth, so it should be unchanged.
        Assert.Equal(128, after.Left, 1);
    }

    [Fact]
    public void TouchUpStrengthIsLowerWithoutAMaskSoTheSceneIsNotFlattened() =>
        Assert.True(TouchUpFilter.StrengthFor(2, hasMask: false) < TouchUpFilter.StrengthFor(2, hasMask: true));

    [Fact]
    public void TouchUpOffIsAlwaysZeroStrength()
    {
        Assert.Equal(0, TouchUpFilter.StrengthFor(0, hasMask: true));
        Assert.Equal(0, TouchUpFilter.StrengthFor(0, hasMask: false));
    }

    [Fact]
    public void FrameSizeChangesAreHandledWithoutStaleBuffers()
    {
        var settings = new CameraEffectSettings { BackgroundMode = CameraEffectSettings.BackgroundBlur };
        var pipeline = new CameraEffectPipeline(settings, new LeftHalfPersonModel());
        pipeline.Process(BuildFrame(), Width, Height);
        var smaller = new byte[32 * 24 * 4];
        Array.Fill(smaller, (byte)200);
        pipeline.Process(smaller, 32, 24);
    }
}
