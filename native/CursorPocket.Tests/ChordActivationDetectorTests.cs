using CursorPocket.Core.Services;

namespace CursorPocket.Tests;

public sealed class ChordActivationDetectorTests
{
    [Fact]
    public void Both_buttons_held_past_the_threshold_activates()
    {
        var detector = new ChordActivationDetector();

        detector.Press(MouseChordButton.Left, 0);
        detector.Press(MouseChordButton.Right, 0.05);
        Assert.True(detector.IsChordHeld);
        Assert.False(detector.ShouldActivate(0.5));
        Assert.True(detector.ShouldActivate(0.8));
    }

    [Fact]
    public void One_button_alone_never_activates_however_long_it_is_held()
    {
        var detector = new ChordActivationDetector();

        detector.Press(MouseChordButton.Left, 0);
        Assert.False(detector.ShouldActivate(10));
        Assert.False(detector.IsChordHeld);

        // The other button alone is no different — this is what keeps an ordinary
        // click-and-hold, or a long right-press, from opening command mode.
        detector.Release(MouseChordButton.Left, 10);
        detector.Press(MouseChordButton.Right, 11);
        Assert.False(detector.ShouldActivate(30));
    }

    [Fact]
    public void Releasing_early_cancels_and_a_later_chord_starts_over()
    {
        var detector = new ChordActivationDetector();

        detector.Press(MouseChordButton.Left, 0);
        detector.Press(MouseChordButton.Right, 0);
        detector.Release(MouseChordButton.Right, 0.4);
        Assert.False(detector.ShouldActivate(5));

        detector.Press(MouseChordButton.Right, 5);
        // The hold is measured from the new chord, not from the abandoned one.
        Assert.False(detector.ShouldActivate(5.3));
        Assert.True(detector.ShouldActivate(5.75));
    }

    [Fact]
    public void The_hold_is_measured_from_the_second_button_landing()
    {
        var detector = new ChordActivationDetector();

        // A slow reach for the second button must not count toward the hold.
        detector.Press(MouseChordButton.Left, 0);
        detector.Press(MouseChordButton.Right, 3);
        Assert.False(detector.ShouldActivate(3.5));
        Assert.True(detector.ShouldActivate(3.7));
    }

    [Fact]
    public void One_hold_fires_once_and_rearms_only_when_the_hand_comes_off()
    {
        var detector = new ChordActivationDetector();

        detector.Press(MouseChordButton.Left, 0);
        detector.Press(MouseChordButton.Right, 0);
        Assert.True(detector.ShouldActivate(1));
        Assert.False(detector.ShouldActivate(2));
        Assert.True(detector.HasFired);

        // Lifting one button and pressing it again must not chain a second
        // activation while the other is still down.
        detector.Release(MouseChordButton.Right, 2);
        detector.Press(MouseChordButton.Right, 2.1);
        Assert.False(detector.ShouldActivate(4));

        // Fully releasing re-arms it.
        detector.Release(MouseChordButton.Left, 4);
        detector.Release(MouseChordButton.Right, 4);
        Assert.False(detector.HasFired);
        detector.Press(MouseChordButton.Left, 5);
        detector.Press(MouseChordButton.Right, 5);
        Assert.True(detector.ShouldActivate(5.8));
    }

    [Fact]
    public void The_countdown_is_reported_while_a_chord_is_pending()
    {
        var detector = new ChordActivationDetector(holdSeconds: 1);

        Assert.Null(detector.SecondsUntilActivation(0));
        detector.Press(MouseChordButton.Left, 0);
        Assert.Null(detector.SecondsUntilActivation(0));
        detector.Press(MouseChordButton.Right, 0);
        Assert.Equal(0.6, detector.SecondsUntilActivation(0.4)!.Value, 3);
        Assert.Equal(0, detector.SecondsUntilActivation(9)!.Value, 3);

        Assert.True(detector.ShouldActivate(9));
        // Nothing is pending once it has fired.
        Assert.Null(detector.SecondsUntilActivation(9));
    }

    [Fact]
    public void The_default_hold_is_seven_hundred_milliseconds()
    {
        Assert.Equal(0.7, ChordActivationDetector.DefaultHoldSeconds, 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void A_nonsensical_hold_is_refused(double holdSeconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChordActivationDetector(holdSeconds));
    }
}
