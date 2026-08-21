namespace CursorPocket.Core.Services;

public enum MouseChordButton
{
    Left,
    Right,
}

/// <summary>
/// Opens command mode when both mouse buttons are held together for a moment.
/// <para>
/// Deliberately strict about the chord and the duration and permissive about
/// everything else: pointer movement never cancels it. Cancelling on drift would
/// not help — the app underneath has already received the drag either way — and
/// it would only add a way for a deliberate gesture to silently fail. The
/// protection against firing during ordinary work is that chording both buttons
/// is already something nothing else asks you to do.
/// </para>
/// <para>
/// This is a pure state machine so it can be tested without a mouse hook. It
/// does not read the clock: every method takes the caller's timestamp, and
/// <see cref="ShouldActivate"/> has to be polled, because a perfectly still
/// hold produces no mouse messages at all and would otherwise never be noticed.
/// </para>
/// </summary>
public sealed class ChordActivationDetector
{
    /// <summary>
    /// How long both buttons must be held. Chording both buttons is already
    /// deliberate enough to carry the false-positive protection on its own, so the
    /// hold is short: long enough to be unmistakably intentional, short enough that
    /// the app underneath has little chance to start a rubber-band drag first.
    /// </summary>
    public const double DefaultHoldSeconds = 0.7d;

    private readonly double _holdSeconds;
    private bool _leftDown;
    private bool _rightDown;
    private bool _fired;
    private double _chordStartedAt = double.NaN;

    public ChordActivationDetector(double holdSeconds = DefaultHoldSeconds)
    {
        if (!double.IsFinite(holdSeconds) || holdSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(holdSeconds), "The hold must be a positive duration.");
        }
        _holdSeconds = holdSeconds;
    }

    /// <summary>Whether both buttons are currently down.</summary>
    public bool IsChordHeld => _leftDown && _rightDown;

    /// <summary>
    /// Whether the chord has already opened command mode. The gesture arms again
    /// only once both buttons come back up, so one hold cannot fire twice.
    /// </summary>
    public bool HasFired => _fired;

    /// <summary>When the pending chord will fire, or null when no chord is waiting.</summary>
    public double? SecondsUntilActivation(double seconds) =>
        IsChordHeld && !_fired && double.IsFinite(_chordStartedAt)
            ? Math.Max(0, _chordStartedAt + _holdSeconds - seconds)
            : null;

    public void Press(MouseChordButton button, double seconds)
    {
        if (button == MouseChordButton.Left)
        {
            _leftDown = true;
        }
        else
        {
            _rightDown = true;
        }
        // The clock starts when the *second* button lands, not the first, so a
        // slow reach for the second button does not eat into the hold.
        if (IsChordHeld && !double.IsFinite(_chordStartedAt))
        {
            _chordStartedAt = seconds;
        }
    }

    public void Release(MouseChordButton button, double seconds)
    {
        if (button == MouseChordButton.Left)
        {
            _leftDown = false;
        }
        else
        {
            _rightDown = false;
        }
        _chordStartedAt = double.NaN;
        // Re-arm only when the hand is completely off, so releasing one button
        // and pressing it again cannot chain a second activation.
        if (!_leftDown && !_rightDown)
        {
            _fired = false;
        }
    }

    /// <summary>
    /// True exactly once per chord, the first time it is polled at or after the
    /// hold has elapsed. Poll this from a timer as well as from mouse events.
    /// </summary>
    public bool ShouldActivate(double seconds)
    {
        if (_fired || !IsChordHeld || !double.IsFinite(_chordStartedAt))
        {
            return false;
        }
        if (seconds - _chordStartedAt < _holdSeconds)
        {
            return false;
        }
        _fired = true;
        return true;
    }
}
