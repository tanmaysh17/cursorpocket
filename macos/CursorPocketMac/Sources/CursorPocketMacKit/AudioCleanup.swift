import Foundation

/// Finalize-time cleanup for audio notes, mirroring the Windows rule: the raw
/// take is already on disk before any of this runs, the output only replaces
/// it when every step succeeds, and everything defaults OFF. Pure `[Float]`
/// math — deterministic, no clock, no randomness — so the exact same input
/// always produces the exact same output.
public enum AudioCleanup {
    /// Rumble/handling-noise floor. 80 Hz sits below male speech fundamentals,
    /// matching the Windows `highpass=f=80` choice.
    public static let highPassCutoffHz: Double = 80

    /// Peak target. -1 dBFS leaves headroom against inter-sample overs when
    /// the WAV is later transcoded.
    public static let targetPeakDbfs: Float = -1

    /// Peaks already within this many dB of the target are left untouched —
    /// re-scaling by a fraction of a decibel only churns the sample values.
    public static let peakToleranceDb: Float = 0.5

    /// First-order RC high-pass, applied in place of FFmpeg's `highpass`.
    ///
    /// Discretizing the analog RC high-pass gives the standard recurrence
    ///     y[n] = a * (y[n-1] + x[n] - x[n-1])
    /// with `RC = 1 / (2π·fc)`, `dt = 1 / sampleRate`, and
    /// `a = RC / (RC + dt)` (0 < a < 1 for any positive fc and rate, which is
    /// what makes the filter unconditionally stable). State starts at silence
    /// (`x[-1] = 0`, `y[-1] = 0`): a take always begins from a quiet room, and
    /// a DC-offset signal then decays as `a^n`, reaching zero well inside the
    /// first second at any speech sample rate.
    public static func highPass(
        _ samples: [Float],
        sampleRate: Double,
        cutoffHz: Double = highPassCutoffHz
    ) -> [Float] {
        guard sampleRate > 0, cutoffHz > 0, !samples.isEmpty else { return samples }
        let rc = 1 / (2 * Double.pi * cutoffHz)
        let dt = 1 / sampleRate
        let a = Float(rc / (rc + dt))
        var output = [Float](repeating: 0, count: samples.count)
        var previousInput: Float = 0
        var previousOutput: Float = 0
        for index in samples.indices {
            let input = samples[index]
            let filtered = a * (previousOutput + input - previousInput)
            output[index] = filtered
            previousInput = input
            previousOutput = filtered
        }
        return output
    }

    /// Scales the whole take so its peak lands at `targetPeakDbfs`. No-ops:
    /// silence (there is nothing to scale, and any gain would be arbitrary)
    /// and a peak already within `peakToleranceDb` of the target — so an
    /// already-loud take passes through bit-identical.
    public static func normalizePeak(
        _ samples: [Float],
        targetDbfs: Float = targetPeakDbfs,
        toleranceDb: Float = peakToleranceDb
    ) -> [Float] {
        var peak: Float = 0
        for sample in samples {
            let magnitude = abs(sample)
            if magnitude > peak { peak = magnitude }
        }
        guard peak > 0, peak.isFinite else { return samples }
        let target = pow(10, targetDbfs / 20)
        let gainDb = 20 * log10(target / peak)
        guard abs(gainDb) > toleranceDb else { return samples }
        let gain = target / peak
        return samples.map { $0 * gain }
    }

    /// The full cleanup chain: high-pass, then peak-normalize the filtered
    /// result (in that order, so the normalization gain is not spent on
    /// rumble the filter is about to remove).
    public static func process(_ samples: [Float], sampleRate: Double) -> [Float] {
        normalizePeak(highPass(samples, sampleRate: sampleRate))
    }
}
