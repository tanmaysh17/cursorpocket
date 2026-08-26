import Foundation
import XCTest
@testable import CursorPocketMacKit

/// Port of the Windows `ChordActivationDetectorTests` — same presses, same
/// timestamps, same expectations, so the two detectors cannot drift apart
/// silently.
final class ChordActivationDetectorTests: XCTestCase {
    func testBothButtonsHeldPastTheThresholdActivates() {
        let detector = ChordActivationDetector()

        detector.press(.left, at: 0)
        detector.press(.right, at: 0.05)
        XCTAssertTrue(detector.isChordHeld)
        XCTAssertFalse(detector.shouldActivate(at: 0.5))
        XCTAssertTrue(detector.shouldActivate(at: 0.8))
    }

    func testOneButtonAloneNeverActivatesHoweverLongItIsHeld() {
        let detector = ChordActivationDetector()

        detector.press(.left, at: 0)
        XCTAssertFalse(detector.shouldActivate(at: 10))
        XCTAssertFalse(detector.isChordHeld)

        // The other button alone is no different — this is what keeps an
        // ordinary click-and-hold, or a long right-press, from opening
        // command mode.
        detector.release(.left, at: 10)
        detector.press(.right, at: 11)
        XCTAssertFalse(detector.shouldActivate(at: 30))
    }

    func testReleasingEarlyCancelsAndALaterChordStartsOver() {
        let detector = ChordActivationDetector()

        detector.press(.left, at: 0)
        detector.press(.right, at: 0)
        detector.release(.right, at: 0.4)
        XCTAssertFalse(detector.shouldActivate(at: 5))

        detector.press(.right, at: 5)
        // The hold is measured from the new chord, not from the abandoned one.
        XCTAssertFalse(detector.shouldActivate(at: 5.3))
        XCTAssertTrue(detector.shouldActivate(at: 5.75))
    }

    func testTheHoldIsMeasuredFromTheSecondButtonLanding() {
        let detector = ChordActivationDetector()

        // A slow reach for the second button must not count toward the hold.
        detector.press(.left, at: 0)
        detector.press(.right, at: 3)
        XCTAssertFalse(detector.shouldActivate(at: 3.5))
        XCTAssertTrue(detector.shouldActivate(at: 3.7))
    }

    func testOneHoldFiresOnceAndRearmsOnlyWhenTheHandComesOff() {
        let detector = ChordActivationDetector()

        detector.press(.left, at: 0)
        detector.press(.right, at: 0)
        XCTAssertTrue(detector.shouldActivate(at: 1))
        XCTAssertFalse(detector.shouldActivate(at: 2))
        XCTAssertTrue(detector.hasFired)

        // Lifting one button and pressing it again must not chain a second
        // activation while the other is still down.
        detector.release(.right, at: 2)
        detector.press(.right, at: 2.1)
        XCTAssertFalse(detector.shouldActivate(at: 4))

        // Fully releasing re-arms it.
        detector.release(.left, at: 4)
        detector.release(.right, at: 4)
        XCTAssertFalse(detector.hasFired)
        detector.press(.left, at: 5)
        detector.press(.right, at: 5)
        XCTAssertTrue(detector.shouldActivate(at: 5.8))
    }

    func testTheCountdownIsReportedWhileAChordIsPending() {
        let detector = ChordActivationDetector(holdSeconds: 1)!

        XCTAssertNil(detector.secondsUntilActivation(at: 0))
        detector.press(.left, at: 0)
        XCTAssertNil(detector.secondsUntilActivation(at: 0))
        detector.press(.right, at: 0)
        XCTAssertEqual(detector.secondsUntilActivation(at: 0.4)!, 0.6, accuracy: 0.001)
        XCTAssertEqual(detector.secondsUntilActivation(at: 9)!, 0, accuracy: 0.001)

        XCTAssertTrue(detector.shouldActivate(at: 9))
        // Nothing is pending once it has fired.
        XCTAssertNil(detector.secondsUntilActivation(at: 9))
    }

    func testTheDefaultHoldIsSevenHundredMilliseconds() {
        XCTAssertEqual(ChordActivationDetector.defaultHoldSeconds, 0.7, accuracy: 0.001)
    }

    func testANonsensicalHoldIsRefused() {
        for holdSeconds in [0, -1, Double.nan] {
            XCTAssertNil(ChordActivationDetector(holdSeconds: holdSeconds), "\(holdSeconds)")
        }
    }
}
