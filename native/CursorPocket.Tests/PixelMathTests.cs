using CursorPocket.Core.Media;

namespace CursorPocket.Tests;

public class PixelMathTests
{
    private static byte[] SolidFrame(int width, int height, byte value)
    {
        var pixels = new byte[width * height * 4];
        for (var index = 0; index < width * height; index++)
        {
            pixels[index * 4] = value;
            pixels[index * 4 + 1] = value;
            pixels[index * 4 + 2] = value;
            pixels[index * 4 + 3] = 255;
        }
        return pixels;
    }

    [Fact]
    public void NeutralLutLeavesPixelsWhereTheyWere()
    {
        var pixels = SolidFrame(8, 8, 137);
        new ColorAdjustLut(0, 0, 0).Apply(pixels, 8, 8, 32);
        Assert.All(Enumerable.Range(0, 64), index => Assert.Equal(137, pixels[index * 4 + 1]));
    }

    [Fact]
    public void LutHonoursTheStrideSoPaddedRowsAreNotCorrupted()
    {
        const int width = 4;
        const int height = 2;
        const int stride = 24; // 16 bytes of pixels plus 8 bytes of padding.
        var buffer = new byte[stride * height];
        Array.Fill(buffer, (byte)9);
        for (var row = 0; row < height; row++)
        {
            for (var x = 0; x < width * 4; x++)
            {
                buffer[row * stride + x] = 100;
            }
        }
        new ColorAdjustLut(100, 0, 0).Apply(buffer, width, height, stride);
        // Padding beyond the pixel data is untouched.
        Assert.Equal(9, buffer[width * 4 + 1]);
        Assert.True(buffer[1] > 100);
    }

    [Fact]
    public void LutClampsInsteadOfWrappingAtTheExtremes()
    {
        var bright = SolidFrame(4, 4, 250);
        new ColorAdjustLut(100, 100, 100).Apply(bright, 4, 4, 16);
        Assert.Equal(255, bright[1]);
        var dark = SolidFrame(4, 4, 5);
        new ColorAdjustLut(-100, 100, -100).Apply(dark, 4, 4, 16);
        Assert.Equal(0, dark[1]);
    }

    [Fact]
    public void ContrastPushesLightAndDarkApart()
    {
        var light = SolidFrame(4, 4, 200);
        var dark = SolidFrame(4, 4, 60);
        var lut = new ColorAdjustLut(0, 70, 0);
        lut.Apply(light, 4, 4, 16);
        lut.Apply(dark, 4, 4, 16);
        Assert.True(light[1] > 200);
        Assert.True(dark[1] < 60);
    }

    [Fact]
    public void OutOfRangeAdjustmentsAreClampedNotThrown()
    {
        var pixels = SolidFrame(4, 4, 128);
        new ColorAdjustLut(5000, -5000, 5000).Apply(pixels, 4, 4, 16);
        Assert.InRange(pixels[1], 0, 255);
    }

    [Fact]
    public void BlurOfAFlatFrameChangesNothing()
    {
        var pixels = SolidFrame(16, 16, 90);
        var scratch = new byte[pixels.Length];
        BoxBlur.Apply(pixels, 16, 16, 2, scratch, iterations: 3);
        Assert.All(Enumerable.Range(0, 256), index => Assert.Equal(90, pixels[index * 4 + 1]));
    }

    [Fact]
    public void BlurAveragesAwayAlternatingDetail()
    {
        const int size = 16;
        var pixels = new byte[size * size * 4];
        for (var index = 0; index < size * size; index++)
        {
            var value = index % 2 == 0 ? (byte)255 : (byte)0;
            pixels[index * 4] = value;
            pixels[index * 4 + 1] = value;
            pixels[index * 4 + 2] = value;
            pixels[index * 4 + 3] = 255;
        }
        var scratch = new byte[pixels.Length];
        BoxBlur.Apply(pixels, size, size, 2, scratch, iterations: 3);
        var center = ((size / 2) * size + size / 2) * 4 + 1;
        Assert.InRange(pixels[center], 100, 155);
    }

    [Fact]
    public void DownscaleAveragesEachBlock()
    {
        const int size = 8;
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var value = x < size / 2 ? (byte)0 : (byte)200;
                var offset = (y * size + x) * 4;
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
                pixels[offset + 3] = 255;
            }
        }
        var destination = new byte[size * size * 4];
        PixelResizer.Downscale(pixels, size, size, 2, destination, out var outWidth, out var outHeight);
        Assert.Equal(4, outWidth);
        Assert.Equal(4, outHeight);
        Assert.Equal(0, destination[1]);
        Assert.Equal(200, destination[(outWidth - 1) * 4 + 1]);
    }

    [Fact]
    public void UpscaleRoundTripsAFlatColour()
    {
        var small = SolidFrame(4, 4, 77);
        var large = new byte[16 * 16 * 4];
        PixelResizer.UpscaleBilinear(small, 4, 4, large, 16, 16);
        Assert.All(Enumerable.Range(0, 256), index => Assert.Equal(77, large[index * 4 + 1]));
    }

    [Fact]
    public void MaskResampleKeepsAConstantPlaneConstant()
    {
        var source = new float[16];
        Array.Fill(source, 0.4f);
        var destination = new float[100];
        PixelResizer.ResamplePlane(source, 4, 4, destination, 10, 10);
        Assert.All(destination, value => Assert.Equal(0.4f, value, 3));
    }

    [Fact]
    public void CropToFillPreservesAspectByCroppingTheLongSide()
    {
        // A 100x50 source into a 50x50 destination crops width, keeping the middle.
        var source = new byte[100 * 50 * 4];
        for (var y = 0; y < 50; y++)
        {
            for (var x = 0; x < 100; x++)
            {
                var value = x is >= 25 and < 75 ? (byte)255 : (byte)0;
                var offset = (y * 100 + x) * 4;
                source[offset] = value;
                source[offset + 1] = value;
                source[offset + 2] = value;
                source[offset + 3] = 255;
            }
        }
        var filled = PixelResizer.CropToFill(source, 100, 50, 50, 50);
        Assert.All(Enumerable.Range(0, 2500), index => Assert.Equal(255, filled[index * 4 + 1]));
    }

    [Fact]
    public void MaskSmoothingConvergesTowardTheLatestMask()
    {
        var postprocessor = new MaskPostprocessor(4);
        var zeros = new float[16];
        var ones = new float[16];
        Array.Fill(ones, 1f);

        // An empty mask resolves to empty and is not considered usable: nobody
        // is in frame, so there is nothing to composite against.
        Assert.False(postprocessor.HasMask);
        postprocessor.Accumulate(zeros);
        var alpha = new float[16];
        postprocessor.Resolve(alpha, 4, 4);
        Assert.All(alpha, value => Assert.Equal(0f, value, 2));
        Assert.False(postprocessor.HasMask);

        for (var frame = 0; frame < 12; frame++)
        {
            postprocessor.Accumulate(ones);
        }
        postprocessor.Resolve(alpha, 4, 4);
        Assert.All(alpha, value => Assert.True(value > 0.9f, $"Mask did not converge: {value}"));
        Assert.True(postprocessor.HasMask);
    }

    [Fact]
    public void SmoothingBlendsTowardTheNewMaskRatherThanJumpingToIt()
    {
        var postprocessor = new MaskPostprocessor(4);
        var ones = new float[16];
        Array.Fill(ones, 1f);
        postprocessor.Accumulate(new float[16]);
        postprocessor.Accumulate(ones);

        var alpha = new float[16];
        postprocessor.Resolve(alpha, 4, 4);
        // One frame of a full mask against an empty history lands mid-way, not at 1.
        Assert.All(alpha, value => Assert.InRange(value, 0.35f, 0.65f));
    }

    [Fact]
    public void ResettingTheMaskForcesTheNextFrameToSeedAgain()
    {
        var postprocessor = new MaskPostprocessor(4);
        var ones = new float[16];
        Array.Fill(ones, 1f);
        postprocessor.Accumulate(ones);
        Assert.True(postprocessor.HasMask);

        postprocessor.Reset();

        Assert.False(postprocessor.HasMask);
        Assert.Equal(0f, postprocessor.Coverage);
    }

    [Fact]
    public void ChannelsFirstTensorSeparatesThePlanes()
    {
        var pixels = new byte[4];
        pixels[0] = 10;  // blue
        pixels[1] = 20;  // green
        pixels[2] = 30;  // red
        pixels[3] = 255;
        var tensor = new float[1 * 1 * 3];
        SegmentationPreprocessor.FillTensor(pixels, 1, 1, 1, tensor, channelsFirst: true);
        Assert.Equal(30 / 255f, tensor[0], 4);
        Assert.Equal(20 / 255f, tensor[1], 4);
        Assert.Equal(10 / 255f, tensor[2], 4);
    }

    [Fact]
    public void ChannelsLastTensorInterleavesRgb()
    {
        var pixels = new byte[] { 10, 20, 30, 255 };
        var tensor = new float[3];
        SegmentationPreprocessor.FillTensor(pixels, 1, 1, 1, tensor, channelsFirst: false);
        Assert.Equal(30 / 255f, tensor[0], 4);
        Assert.Equal(20 / 255f, tensor[1], 4);
        Assert.Equal(10 / 255f, tensor[2], 4);
    }

    [Fact]
    public void CompositeIsATrueAlphaBlend()
    {
        var foreground = SolidFrame(2, 2, 200);
        var background = SolidFrame(2, 2, 100);
        var mask = new float[] { 1f, 0f, 0.5f, 0f };
        MaskCompositor.Composite(foreground, background, mask, 2, 2);
        Assert.Equal(200, foreground[1]);
        Assert.Equal(100, foreground[4 + 1]);
        Assert.InRange(foreground[8 + 1], 148, 152);
    }

    [Fact]
    public void CompositeClampsMaskValuesOutsideZeroToOne()
    {
        var foreground = SolidFrame(2, 1, 200);
        var background = SolidFrame(2, 1, 50);
        MaskCompositor.Composite(foreground, background, [5f, -3f], 2, 1);
        Assert.Equal(200, foreground[1]);
        Assert.Equal(50, foreground[4 + 1]);
    }
}
