import Foundation
import XCTest
@testable import CursorPocketMacKit

final class AudioCleanupTests: XCTestCase {
    private let sampleRate: Double = 44_100

    // MARK: High-pass

    func testHighPassRemovesDcOffset() {
        // Half a second of pure DC. The first-order filter decays a constant
        // as a^n, so the tail must be effectively zero.
        let samples = [Float](repeating: 0.5, count: 22_050)
        let filtered = AudioCleanup.highPass(samples, sampleRate: sampleRate)
        XCTAssertEqual(filtered.count, samples.count)
        for sample in filtered.suffix(1_000) {
            XCTAssertLessThan(abs(sample), 1e-3)
        }
    }

    func testHighPassIsStableOnImpulse() {
        // An impulse excites every frequency at once; the response must stay
        // finite everywhere and decay back toward zero.
        var samples = [Float](repeating: 0, count: 4_410)
        samples[0] = 1
        let filtered = AudioCleanup.highPass(samples, sampleRate: sampleRate)
        XCTAssertEqual(filtered.count, samples.count)
        for sample in filtered {
            XCTAssertTrue(sample.isFinite)
            XCTAssertFalse(sample.isNaN)
        }
        XCTAssertLessThan(abs(filtered.last ?? 1), 1e-3)
    }

    func testHighPassLeavesSilenceAsSilence() {
        let silence = [Float](repeating: 0, count: 1_000)
        XCTAssertEqual(AudioCleanup.highPass(silence, sampleRate: sampleRate), silence)
    }

    func testHighPassPassesSpeechBandSignalMostlyThrough() {
        // A 1 kHz tone sits far above the 80 Hz corner; the filter must not
        // meaningfully attenuate it. Skip the first cycles of settling.
        let samples = (0..<44_100).map { n in
            Float(sin(2 * Double.pi * 1_000 * Double(n) / 44_100))
        }
        let filtered = AudioCleanup.highPass(samples, sampleRate: sampleRate)
        let steadyPeak = filtered.suffix(22_050).map(abs).max() ?? 0
        XCTAssertGreaterThan(steadyPeak, 0.95)
    }

    func testHighPassGuardsDegenerateInputs() {
        XCTAssertEqual(AudioCleanup.highPass([], sampleRate: sampleRate), [])
        let samples: [Float] = [0.1, 0.2, 0.3]
        // A zero sample rate cannot form coefficients; the input passes through.
        XCTAssertEqual(AudioCleanup.highPass(samples, sampleRate: 0), samples)
    }

    // MARK: Peak normalization

    func testQuietSignalIsNormalizedToTargetPeak() {
        let samples: [Float] = [0.05, -0.1, 0.02, -0.06]
        let normalized = AudioCleanup.normalizePeak(samples)
        let peak = normalized.map(abs).max() ?? 0
        let target = pow(10, AudioCleanup.targetPeakDbfs / 20)
        XCTAssertEqual(peak, target, accuracy: 1e-4)
        // Relative shape is preserved: normalization is a single gain.
        XCTAssertEqual(normalized[0] / normalized[1], samples[0] / samples[1], accuracy: 1e-5)
    }

    func testSilenceIsLeftUntouched() {
        let silence = [Float](repeating: 0, count: 100)
        XCTAssertEqual(AudioCleanup.normalizePeak(silence), silence)
    }

    func testAlreadyLoudSignalIsLeftUntouched() {
        // Peak 0.9 is -0.915 dBFS — inside the 0.5 dB tolerance around the
        // -1 dBFS target, so the samples must come back bit-identical.
        let samples: [Float] = [0.9, -0.5, 0.3, -0.9]
        XCTAssertEqual(AudioCleanup.normalizePeak(samples), samples)
    }

    func testOverFullScalePeakIsBroughtDownToTarget() {
        // Filter transients can overshoot full scale; normalization brings
        // the peak back to the target rather than leaving a clipped write.
        let samples: [Float] = [1.4, -0.2, 0.1]
        let normalized = AudioCleanup.normalizePeak(samples)
        let target = pow(10, AudioCleanup.targetPeakDbfs / 20)
        XCTAssertEqual(normalized.map(abs).max() ?? 0, target, accuracy: 1e-4)
    }

    func testNormalizationHandlesEmptyInput() {
        XCTAssertEqual(AudioCleanup.normalizePeak([]), [])
    }

    // MARK: Full chain

    func testProcessIsDeterministic() {
        // No clock, no randomness: the same take always cleans up the same.
        let samples = (0..<4_410).map { n in
            Float(0.3 + 0.2 * sin(2 * Double.pi * 440 * Double(n) / 44_100))
        }
        let first = AudioCleanup.process(samples, sampleRate: sampleRate)
        let second = AudioCleanup.process(samples, sampleRate: sampleRate)
        XCTAssertEqual(first, second)
    }

    func testProcessRemovesOffsetAndNormalizes() {
        // A quiet tone riding a DC offset: the offset goes, the tone is
        // boosted to the -1 dBFS target.
        let samples = (0..<44_100).map { n in
            Float(0.4 + 0.05 * sin(2 * Double.pi * 440 * Double(n) / 44_100))
        }
        let processed = AudioCleanup.process(samples, sampleRate: sampleRate)
        let target = pow(10, AudioCleanup.targetPeakDbfs / 20)
        XCTAssertEqual(processed.map(abs).max() ?? 0, target, accuracy: 1e-3)
        // The steady tail must be centered near zero — the offset is gone.
        let tail = processed.suffix(22_050)
        let mean = tail.reduce(Float(0), +) / Float(tail.count)
        XCTAssertLessThan(abs(mean), 1e-2)
    }
}
