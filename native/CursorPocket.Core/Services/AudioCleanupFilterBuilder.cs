namespace CursorPocket.Core.Services;

/// <summary>
/// Builds the FFmpeg <c>-af</c> chain for microphone cleanup. Applied at
/// finalize time (the video mux pass and the audio-note post pass) rather than
/// live, so a filter failure can never lose a recording — the raw capture is
/// always written first.
/// </summary>
public static class AudioCleanupFilterBuilder
{
    /// <summary>
    /// Rumble cut plus FFT denoise. All LGPL-core avfilters, no model files.
    /// Measured on the pinned build: 12 dB out of the noise floor with the
    /// speech band within a quarter of a dB of untouched.
    /// </summary>
    public const string NoiseSuppressionChain = "highpass=f=80,afftdn=nr=12:nf=-25";

    /// <summary>
    /// EBU R128 loudness normalization to the −16 LUFS speech target, with a
    /// true-peak ceiling so leveling can never clip.
    /// <para>
    /// <c>loudnorm</c> rather than <c>dynaudnorm</c>: measured against a clip
    /// drifting 19 dB between quiet and loud, this closes the gap to under 1 dB
    /// while <c>dynaudnorm</c> merely applied uniform gain and left the drift.
    /// The trailing <c>aresample</c> is required, not cosmetic — <c>loudnorm</c>
    /// works internally at 192 kHz and emits at that rate, which would
    /// quadruple every file and contradict the 48 kHz we record in metadata.
    /// </para>
    /// </summary>
    public const string AutoLevelChain = "loudnorm=I=-16:TP=-1.5:LRA=7,aresample=48000";

    /// <summary>Returns the combined chain, or null when no cleanup is enabled.</summary>
    public static string? Build(bool noiseSuppression, bool autoLevel)
    {
        if (noiseSuppression && autoLevel)
        {
            // Denoise before leveling so the normalizer does not amplify noise first.
            return $"{NoiseSuppressionChain},{AutoLevelChain}";
        }
        if (noiseSuppression)
        {
            return NoiseSuppressionChain;
        }
        return autoLevel ? AutoLevelChain : null;
    }
}
