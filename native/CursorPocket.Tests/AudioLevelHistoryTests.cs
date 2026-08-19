using CursorPocket.Core.Services;

namespace CursorPocket.Tests;

public sealed class AudioLevelHistoryTests
{
    [Fact]
    public void History_starts_silent()
    {
        var history = new AudioLevelHistory();

        for (var index = 0; index < AudioLevelHistory.Length; index++)
        {
            Assert.Equal(0, history[index]);
        }
    }

    [Fact]
    public void The_newest_sample_lands_at_the_end_and_older_ones_shift_left()
    {
        var history = new AudioLevelHistory();

        history.Push(0.2);
        history.Push(0.5);
        history.Push(0.9);

        Assert.Equal(0.9, history[AudioLevelHistory.Length - 1]);
        Assert.Equal(0.5, history[AudioLevelHistory.Length - 2]);
        Assert.Equal(0.2, history[AudioLevelHistory.Length - 3]);
        Assert.Equal(0, history[AudioLevelHistory.Length - 4]);
    }

    [Fact]
    public void Older_samples_fall_off_the_front()
    {
        var history = new AudioLevelHistory();
        for (var index = 0; index < AudioLevelHistory.Length + 5; index++)
        {
            history.Push(index / 100d);
        }

        // The first five pushes are gone; the window holds only the most recent run.
        Assert.Equal((AudioLevelHistory.Length + 4) / 100d, history[AudioLevelHistory.Length - 1]);
        Assert.Equal(5 / 100d, history[0]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Out_of_range_levels_are_clamped_rather_than_drawn(double level)
    {
        var history = new AudioLevelHistory();

        history.Push(level);

        Assert.InRange(history[AudioLevelHistory.Length - 1], 0, 1);
    }

    [Fact]
    public void Reading_outside_the_window_is_silence_rather_than_a_crash()
    {
        var history = new AudioLevelHistory();

        Assert.Equal(0, history[-1]);
        Assert.Equal(0, history[AudioLevelHistory.Length]);
    }

    [Fact]
    public void Silence_still_draws_a_visible_bar()
    {
        // A meter that disappears at silence looks broken rather than quiet.
        Assert.Equal(AudioLevelHistory.MinimumBarHeight, AudioLevelHistory.BarHeight(0, 24));
    }

    [Fact]
    public void A_full_level_fills_the_meter_and_never_overflows_it()
    {
        Assert.Equal(24, AudioLevelHistory.BarHeight(1, 24));
        Assert.InRange(AudioLevelHistory.BarHeight(5, 24), AudioLevelHistory.MinimumBarHeight, 24);
    }

    [Fact]
    public void Bar_height_rises_with_level_and_favours_quiet_speech()
    {
        var quiet = AudioLevelHistory.BarHeight(0.1, 24);
        var middle = AudioLevelHistory.BarHeight(0.5, 24);

        Assert.True(quiet < middle);
        Assert.True(middle < AudioLevelHistory.BarHeight(0.9, 24));
        // Square-root scaling: a tenth of full level has to be more than a tenth of
        // the meter, or normal speech looks like silence.
        Assert.True(quiet > AudioLevelHistory.MinimumBarHeight + (0.1 * (24 - AudioLevelHistory.MinimumBarHeight)));
    }

    [Fact]
    public void A_meter_with_no_room_still_returns_the_minimum()
    {
        Assert.Equal(AudioLevelHistory.MinimumBarHeight, AudioLevelHistory.BarHeight(1, 0));
    }
}
