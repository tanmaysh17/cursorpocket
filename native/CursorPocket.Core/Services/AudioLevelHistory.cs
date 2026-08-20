namespace CursorPocket.Core.Services;

/// <summary>
/// A short rolling window of microphone levels, newest last.
/// <para>
/// The recorder reports one level at a time, which a single bar can only show as a
/// line growing and shrinking. Keeping the recent history lets the HUD draw those
/// samples as a moving waveform, which reads as "audio is being captured" at a
/// glance instead of needing to be watched.
/// </para>
/// </summary>
public sealed class AudioLevelHistory
{
    public const int Length = 22;

    /// <summary>Kept visible at silence so the meter reads as present, not broken.</summary>
    public const double MinimumBarHeight = 2;

    private readonly double[] _levels = new double[Length];

    public double this[int index] => index >= 0 && index < Length ? _levels[index] : 0;

    public void Push(double level)
    {
        var clamped = double.IsFinite(level) ? Math.Clamp(level, 0, 1) : 0;
        Array.Copy(_levels, 1, _levels, 0, Length - 1);
        _levels[Length - 1] = clamped;
    }

    public static double BarHeight(double level, double maximum)
    {
        var span = Math.Max(0, maximum - MinimumBarHeight);
        var clamped = double.IsFinite(level) ? Math.Clamp(level, 0, 1) : 0;
        // Levels bunch up low, so square-root the value to make quiet speech visible
        // without letting loud passages peg every bar to the top.
        return MinimumBarHeight + (Math.Sqrt(clamped) * span);
    }
}
