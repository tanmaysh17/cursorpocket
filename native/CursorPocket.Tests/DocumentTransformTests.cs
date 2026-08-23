using CursorPocket.Core.Annotations;

namespace CursorPocket.Tests;

public sealed class DocumentTransformTests
{
    [Fact]
    public void An_untouched_document_maps_every_point_to_itself()
    {
        var transform = DocumentTransform.Build(800, 600, null, [], BackdropSettings.None);

        Assert.True(transform.IsIdentity);
        Assert.Equal(800, transform.OutputWidth);
        Assert.Equal(600, transform.OutputHeight);
        Assert.Equal(new AnnPoint(123, 456), transform.ToOutput(new AnnPoint(123, 456)));
        Assert.Equal(new AnnPoint(123, 456), transform.ToSource(new AnnPoint(123, 456)));
    }

    [Fact]
    public void A_crop_shifts_the_origin_and_sets_the_output_size()
    {
        var transform = DocumentTransform.Build(800, 600, new AnnRect(100, 50, 400, 300), [], BackdropSettings.None);

        Assert.Equal(400, transform.OutputWidth);
        Assert.Equal(300, transform.OutputHeight);
        Assert.Equal(new AnnPoint(0, 0), transform.ToOutput(new AnnPoint(100, 50)));
        Assert.Equal(new AnnPoint(50, 25), transform.ToOutput(new AnnPoint(150, 75)));
        Assert.False(transform.IsIdentity);
    }

    [Fact]
    public void A_crop_that_runs_off_the_image_is_clamped_to_it()
    {
        var transform = DocumentTransform.Build(200, 200, new AnnRect(-50, -50, 400, 400), [], BackdropSettings.None);

        Assert.Equal(200, transform.OutputWidth);
        Assert.Equal(200, transform.OutputHeight);
    }

    [Fact]
    public void A_cut_removes_its_band_and_pulls_everything_below_it_up()
    {
        var transform = DocumentTransform.Build(400, 300, null, [new CutBand(100, 50)], BackdropSettings.None);

        Assert.Equal(250, transform.OutputHeight);
        // Above the cut, nothing moves.
        Assert.Equal(new AnnPoint(10, 40), transform.ToOutput(new AnnPoint(10, 40)));
        // Below it, everything rises by exactly the band's length.
        Assert.Equal(new AnnPoint(10, 150), transform.ToOutput(new AnnPoint(10, 200)));
    }

    [Fact]
    public void A_row_inside_a_cut_collapses_onto_the_seam()
    {
        var transform = DocumentTransform.Build(400, 300, null, [new CutBand(100, 50)], BackdropSettings.None);

        Assert.True(transform.IsRemoved(120));
        Assert.False(transform.IsRemoved(99));
        Assert.False(transform.IsRemoved(150));
        // Everything in the band lands on the same output row: the seam itself.
        Assert.Equal(100, transform.ToOutput(new AnnPoint(0, 100)).Y, 6);
        Assert.Equal(100, transform.ToOutput(new AnnPoint(0, 149)).Y, 6);
    }

    [Fact]
    public void Overlapping_cuts_are_merged_rather_than_counted_twice()
    {
        // 100..150 and 120..200 overlap. Counted separately that removes 130 rows; merged
        // it removes the 100 that were actually covered.
        var transform = DocumentTransform.Build(400, 300, null, [new CutBand(100, 50), new CutBand(120, 80)], BackdropSettings.None);

        Assert.Equal(200, transform.OutputHeight);
    }

    [Fact]
    public void Touching_cuts_merge_into_one_seam()
    {
        var transform = DocumentTransform.Build(400, 300, null, [new CutBand(100, 50), new CutBand(150, 30)], BackdropSettings.None);

        Assert.Equal(220, transform.OutputHeight);
        Assert.Single(transform.SeamOffsets());
    }

    [Fact]
    public void Cuts_are_clipped_to_the_crop()
    {
        // The band starts above the crop and ends inside it, so only the overlap counts.
        var transform = DocumentTransform.Build(400, 400, new AnnRect(0, 100, 400, 200), [new CutBand(50, 100)], BackdropSettings.None);

        // Crop height 200, of which rows 100..150 are cut: 50 removed.
        Assert.Equal(150, transform.OutputHeight);
    }

    [Fact]
    public void A_cut_that_would_remove_everything_is_ignored()
    {
        var transform = DocumentTransform.Build(400, 300, null, [new CutBand(0, 300)], BackdropSettings.None);

        // Better to ignore the cut than to export a zero-height image.
        Assert.Equal(300, transform.OutputHeight);
        Assert.Empty(transform.SeamOffsets());
    }

    [Fact]
    public void A_backdrop_pads_the_output_without_moving_the_content_relative_to_itself()
    {
        var backdrop = new BackdropSettings(40, 16, new AnnColor(255, 11, 16, 15), 24, 0.5);
        var transform = DocumentTransform.Build(200, 100, null, [], backdrop);

        Assert.Equal(280, transform.OutputWidth);
        Assert.Equal(180, transform.OutputHeight);
        Assert.Equal(200, transform.ContentWidth);
        Assert.Equal(100, transform.ContentHeight);
        // The image's top-left now sits one padding in.
        Assert.Equal(new AnnPoint(40, 40), transform.ToOutput(new AnnPoint(0, 0)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(37, 91)]
    [InlineData(199, 249)]
    public void Mapping_out_and_back_returns_the_original_point(double x, double y)
    {
        var backdrop = new BackdropSettings(20, 8, new AnnColor(255, 0, 0, 0), 0, 0);
        var transform = DocumentTransform.Build(
            400,
            400,
            new AnnRect(0, 0, 400, 400),
            [new CutBand(80, 30), new CutBand(300, 20)],
            backdrop);

        var source = new AnnPoint(x, y);
        if (transform.IsRemoved(y))
        {
            // A cut row has no output row of its own, so there is nothing to round-trip.
            return;
        }

        var round = transform.ToSource(transform.ToOutput(source));
        Assert.Equal(source.X, round.X, 6);
        Assert.Equal(source.Y, round.Y, 6);
    }

    [Fact]
    public void The_row_map_never_goes_backwards()
    {
        var transform = DocumentTransform.Build(
            100,
            500,
            null,
            [new CutBand(50, 40), new CutBand(200, 60), new CutBand(400, 30)],
            BackdropSettings.None);

        var previous = double.NegativeInfinity;
        for (var y = 0; y < 500; y++)
        {
            var mapped = transform.ToOutput(new AnnPoint(0, y)).Y;
            Assert.True(mapped >= previous - 1e-9, $"row {y} mapped to {mapped}, below the previous {previous}");
            previous = mapped;
        }
    }

    [Fact]
    public void Slabs_cover_every_surviving_row_exactly_once()
    {
        var transform = DocumentTransform.Build(200, 300, null, [new CutBand(100, 50)], BackdropSettings.None);

        var slabs = transform.Slabs();
        Assert.Equal(2, slabs.Count);
        Assert.Equal(250, slabs.Sum(slab => slab.Source.Height), 6);
        // The two slabs meet with no gap in the output.
        Assert.Equal(slabs[0].Output.Bottom, slabs[1].Output.Y, 6);
    }

    [Fact]
    public void Slabs_start_inside_the_backdrop_padding()
    {
        var backdrop = new BackdropSettings(30, 0, new AnnColor(255, 0, 0, 0), 0, 0);
        var transform = DocumentTransform.Build(100, 100, null, [], backdrop);

        var slab = Assert.Single(transform.Slabs());
        Assert.Equal(30, slab.Output.X, 6);
        Assert.Equal(30, slab.Output.Y, 6);
    }

    [Fact]
    public void An_anchored_mark_keeps_its_size_when_it_straddles_a_seam()
    {
        var transform = DocumentTransform.Build(400, 300, null, [new CutBand(100, 50)], BackdropSettings.None);

        var box = transform.ToOutput(new AnnRect(10, 80, 60, 120));

        // The anchor rises with the rows above it; the size is untouched, so the box spans
        // the seam. A box is a callout, not a measurement.
        Assert.Equal(80, box.Y, 6);
        Assert.Equal(120, box.Height, 6);
    }
}

public sealed class SaveTargetTests
{
    [Fact]
    public void Marks_alone_on_a_fresh_capture_overwrite_it()
    {
        // Additive, and the pixels underneath are still visible. One capture, one file,
        // one Library row.
        Assert.Equal(
            AnnotationSaveMode.Overwrite,
            SaveTarget.For(marksChanged: true, geometryChanged: false, AnnotationOrigin.FreshCapture));
    }

    [Fact]
    public void A_geometry_change_writes_a_new_capture_even_on_a_fresh_one()
    {
        // Crop and cut delete pixels, and a save overwrites rather than deleting, so there
        // would be no Recycle Bin copy to go back to.
        Assert.Equal(
            AnnotationSaveMode.NewCapture,
            SaveTarget.For(marksChanged: false, geometryChanged: true, AnnotationOrigin.FreshCapture));
        Assert.Equal(
            AnnotationSaveMode.NewCapture,
            SaveTarget.For(marksChanged: true, geometryChanged: true, AnnotationOrigin.FreshCapture));
    }

    [Fact]
    public void Anything_already_kept_is_never_overwritten()
    {
        foreach (var geometry in new[] { true, false })
        {
            Assert.Equal(
                AnnotationSaveMode.NewCapture,
                SaveTarget.For(marksChanged: true, geometryChanged: geometry, AnnotationOrigin.ExistingCapture));
        }
    }

    [Fact]
    public void The_receipt_says_which_of_the_two_happened()
    {
        Assert.Contains("Edited copy", SaveTarget.Describe(AnnotationSaveMode.NewCapture, copied: false));
        Assert.Contains("Screenshot saved", SaveTarget.Describe(AnnotationSaveMode.Overwrite, copied: false));
        Assert.Contains("copied", SaveTarget.Describe(AnnotationSaveMode.Overwrite, copied: true));
    }
}
