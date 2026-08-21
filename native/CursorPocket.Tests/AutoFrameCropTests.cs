using CursorPocket.Core.Media;

namespace CursorPocket.Tests;

public sealed class AutoFrameCropTests
{
    [Fact]
    public void A_square_shape_trims_the_sides_of_a_four_three_camera()
    {
        var crop = AutoFrameCrop.Compute(640, 480, targetAspect: 1);

        Assert.Equal(480, crop.Width);
        Assert.Equal(480, crop.Height);
        // Centred by default: 160 px of width to lose, 80 from each side.
        Assert.Equal(80, crop.X);
        Assert.Equal(0, crop.Y);
    }

    [Fact]
    public void The_crop_follows_the_focus_point_and_stays_inside_the_frame()
    {
        // Someone sitting to the left should not be sliced in half.
        var left = AutoFrameCrop.Compute(640, 480, targetAspect: 1, focusX: 0.25);
        Assert.Equal(0, left.X);
        Assert.Equal(480, left.Width);

        var right = AutoFrameCrop.Compute(640, 480, targetAspect: 1, focusX: 0.9);
        Assert.Equal(640 - 480, right.X);

        // Slightly off centre moves proportionally rather than snapping to an edge.
        var nudged = AutoFrameCrop.Compute(640, 480, targetAspect: 1, focusX: 0.55);
        Assert.InRange(nudged.X, 81, 160);
    }

    [Fact]
    public void A_wide_shape_trims_the_top_and_bottom_instead()
    {
        var crop = AutoFrameCrop.Compute(640, 480, targetAspect: 16d / 9d);

        Assert.Equal(640, crop.Width);
        Assert.Equal(360, crop.Height);
        Assert.Equal(0, crop.X);
        Assert.Equal(60, crop.Y);
    }

    [Fact]
    public void A_matching_aspect_keeps_the_whole_frame()
    {
        var crop = AutoFrameCrop.Compute(640, 480, targetAspect: 640d / 480d);

        Assert.Equal(640, crop.Width);
        Assert.Equal(480, crop.Height);
        Assert.Equal(0, crop.X);
        Assert.Equal(0, crop.Y);
    }

    [Fact]
    public void Crop_dimensions_stay_even_so_the_downscale_stays_exact()
    {
        var crop = AutoFrameCrop.Compute(641, 481, targetAspect: 1.37);

        Assert.Equal(0, crop.Width % 2);
        Assert.Equal(0, crop.Height % 2);
        Assert.InRange(crop.X, 0, 641 - crop.Width);
        Assert.InRange(crop.Y, 0, 481 - crop.Height);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(0)]
    [InlineData(-2)]
    public void A_nonsensical_aspect_is_refused(double aspect)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AutoFrameCrop.Compute(640, 480, aspect));
    }

    [Fact]
    public void The_centroid_follows_the_subject_and_ignores_a_faint_halo()
    {
        const int width = 40;
        const int height = 20;
        var mask = new float[width * height];
        // A solid block on the left third.
        for (var y = 4; y < 16; y++)
        {
            for (var x = 6; x < 14; x++)
            {
                mask[y * width + x] = 1f;
            }
        }
        // A faint wash across everything, which must not drag the centre right.
        for (var index = 0; index < mask.Length; index++)
        {
            mask[index] = Math.Max(mask[index], 0.08f);
        }

        var centroid = AutoFrameCrop.HorizontalCentroid(mask, width, height);

        Assert.NotNull(centroid);
        Assert.InRange(centroid!.Value, 0.2, 0.3);
    }

    [Fact]
    public void An_empty_mask_reports_no_subject_rather_than_the_middle()
    {
        var mask = new float[64 * 64];

        // Null, not 0.5: "nobody here" and "somebody dead centre" must not look the
        // same, or the crop would drift to centre every time the mask drops out.
        Assert.Null(AutoFrameCrop.HorizontalCentroid(mask, 64, 64));
    }

    [Fact]
    public void Copying_a_crop_lifts_the_expected_pixels()
    {
        const int width = 4;
        const int height = 3;
        var source = new byte[width * height * 4];
        for (var index = 0; index < width * height; index++)
        {
            source[index * 4] = (byte)index; // blue channel carries the pixel index
        }
        var destination = new byte[2 * 2 * 4];

        AutoFrameCrop.CopyCrop(source, width, (X: 1, Y: 1, Width: 2, Height: 2), destination);

        // Row 1 columns 1-2 are indices 5,6; row 2 columns 1-2 are 9,10.
        Assert.Equal(5, destination[0]);
        Assert.Equal(6, destination[4]);
        Assert.Equal(9, destination[8]);
        Assert.Equal(10, destination[12]);
    }
}
