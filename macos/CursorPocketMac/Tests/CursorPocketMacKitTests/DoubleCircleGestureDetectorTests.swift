import Foundation
import XCTest
@testable import CursorPocketMacKit

/// Port of the Windows `DoubleCircleGestureDetectorTests` — same drawings,
/// same expectations, so the two detectors cannot drift apart silently.
final class DoubleCircleGestureDetectorTests: XCTestCase {
    /// Draws `turns` loops of the given radius, sampled at `samplesPerTurn`
    /// points spaced `secondsPerSample` apart, and returns how many times the
    /// gesture fired.
    @discardableResult
    private func drawCircles(
        radius: Double,
        turns: Double = 2,
        samplesPerTurn: Int = 24,
        secondsPerSample: Double = 0.025,
        centerX: Double = 400,
        centerY: Double = 300,
        clockwise: Bool = true,
        startSeconds: Double = 0,
        detector existing: DoubleCircleGestureDetector? = nil
    ) -> Int {
        let detector = existing ?? DoubleCircleGestureDetector()
        let samples = Int((Double(samplesPerTurn) * turns).rounded())
        var triggered = 0
        for index in 0...samples {
            let angle = Double(index) / Double(samplesPerTurn) * .pi * 2 * (clockwise ? 1 : -1)
            let x = centerX + (cos(angle) * radius).rounded()
            let y = centerY + (sin(angle) * radius).rounded()
            if detector.feed(x: x, y: y, now: startSeconds + Double(index) * secondsPerSample) {
                triggered += 1
            }
        }
        return triggered
    }

    func testTwoQuickCirclesTriggerOnce() {
        XCTAssertEqual(drawCircles(radius: 36), 1)
    }

    func testCirclesAcrossTheSupportedSizeRangeTrigger() {
        for radius in [12.0, 20, 36, 90, 180, 250] {
            XCTAssertEqual(drawCircles(radius: radius), 1, "radius \(radius)")
        }
    }

    func testCirclesAtAnyReasonableSpeedTrigger() {
        // A quick flick: two loops in a third of a second — and a slow,
        // deliberate sweep spread over nearly three seconds.
        let cases: [(secondsPerSample: Double, samplesPerTurn: Int)] = [
            (0.008, 20), (0.015, 20), (0.060, 24), (0.070, 20),
        ]
        for testCase in cases {
            XCTAssertEqual(
                drawCircles(
                    radius: 60,
                    samplesPerTurn: testCase.samplesPerTurn,
                    secondsPerSample: testCase.secondsPerSample),
                1,
                "\(testCase)")
        }
    }

    func testCirclesOutsideTheSupportedSizeRangeDoNotTrigger() {
        // Below the size floor, and a sweep so wide it is ordinary mouse travel.
        XCTAssertEqual(drawCircles(radius: 6), 0)
        XCTAssertEqual(drawCircles(radius: 400), 0)
    }

    func testAFlickTooBriefToBeDeliberateDoesNotTrigger() {
        // Two loops in under a fifth of a second is jitter, not a gesture.
        XCTAssertEqual(drawCircles(radius: 60, samplesPerTurn: 20, secondsPerSample: 0.002), 0)
    }

    func testCounterClockwiseCirclesTrigger() {
        XCTAssertEqual(drawCircles(radius: 40, clockwise: false), 1)
    }

    func testCoarselySampledCirclesStillTrigger() {
        // A fast sweep gives the monitor few points per loop; the shape is
        // still a circle.
        XCTAssertEqual(drawCircles(radius: 150, samplesPerTurn: 8, secondsPerSample: 0.02), 1)
    }

    func testSloppyOvalsStillTrigger() {
        let detector = DoubleCircleGestureDetector()
        var triggered = 0
        for index in 0...56 {
            let angle = Double(index) / 28 * .pi * 2
            // Squashed and slightly wobbly, the way a real hand draws it.
            let x = 500 + (cos(angle) * 70).rounded()
            let y = 400 + (sin(angle) * 42 + sin(angle * 3) * 4).rounded()
            if detector.feed(x: x, y: y, now: Double(index) * 0.03) {
                triggered += 1
            }
        }
        XCTAssertEqual(triggered, 1)
    }

    func testStraightMotionDoesNotTrigger() {
        let detector = DoubleCircleGestureDetector()
        for index in 0..<50 {
            XCTAssertFalse(detector.feed(
                x: Double(index * 4), y: Double(index * 2), now: Double(index) * 0.025))
        }
    }

    func testASingleCircleDoesNotTrigger() {
        XCTAssertEqual(drawCircles(radius: 40, turns: 1), 0)
    }

    func testAGentleArcDoesNotTrigger() {
        XCTAssertEqual(drawCircles(radius: 300, turns: 0.75, samplesPerTurn: 40), 0)
    }

    func testBackAndForthMotionDoesNotTrigger() {
        let detector = DoubleCircleGestureDetector()
        var triggered = 0
        for index in 0...80 {
            // Reverses direction repeatedly, so signed travel cancels out.
            let x = 400 + (sin(Double(index) / 6) * 60).rounded()
            if detector.feed(x: x, y: Double(300 + index % 3), now: Double(index) * 0.02) {
                triggered += 1
            }
        }
        XCTAssertEqual(triggered, 0)
    }

    func testCirclesDrawnFarApartInTimeDoNotTrigger() {
        // One circle now and another ten seconds later is not the gesture:
        // the first has aged out of the detection window by then.
        let detector = DoubleCircleGestureDetector()
        XCTAssertEqual(drawCircles(radius: 40, turns: 1, detector: detector), 0)
        XCTAssertEqual(drawCircles(radius: 40, turns: 1, startSeconds: 10, detector: detector), 0)
    }

    func testRepeatedGesturesAreRateLimitedByTheCooldown() {
        let detector = DoubleCircleGestureDetector()
        var triggered = 0
        for index in 0...240 {
            let angle = Double(index) / 24 * .pi * 2
            let x = 400 + (cos(angle) * 36).rounded()
            let y = 300 + (sin(angle) * 36).rounded()
            if detector.feed(x: x, y: y, now: Double(index) * 0.025) {
                triggered += 1
            }
        }
        // Ten continuous loops over six seconds: the cooldown keeps this from
        // reopening the palette on every extra turn.
        XCTAssertTrue((1...3).contains(triggered), "triggered \(triggered) times")
    }

    func testResetForgetsPartialGestures() {
        let detector = DoubleCircleGestureDetector()
        for index in 0..<20 {
            let angle = Double(index) / 24 * .pi * 2
            _ = detector.feed(
                x: 400 + (cos(angle) * 36).rounded(),
                y: 300 + (sin(angle) * 36).rounded(),
                now: Double(index) * 0.025)
        }
        detector.reset()
        var triggered = 0
        for index in 20...30 {
            let angle = Double(index) / 24 * .pi * 2
            if detector.feed(
                x: 400 + (cos(angle) * 36).rounded(),
                y: 300 + (sin(angle) * 36).rounded(),
                now: Double(index) * 0.025) {
                triggered += 1
            }
        }
        XCTAssertEqual(triggered, 0)
    }
}
