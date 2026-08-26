import CoreGraphics
import Foundation
import XCTest
@testable import CursorPocketMacKit

final class RecordingPlanTests: XCTestCase {
    func testPartialNamingStaysInsideTheVideosGlobButIsSkippedByRecovery() {
        let final = URL(fileURLWithPath: "/lib/2026-08-26/videos/10-00-00_video_abc123.mp4")
        let partial = RecordingPlan.partialURL(for: final)
        XCTAssertEqual(partial.lastPathComponent, "10-00-00_video_abc123.partial.mp4")
        XCTAssertTrue(RecordingPlan.isPartial(partial))
        XCTAssertFalse(RecordingPlan.isPartial(final))
    }

    func testBitrateHasALegibilityFloor()  {
        XCTAssertEqual(RecordingPlan.videoBitrate(width: 320, height: 240), 2_000_000)
        XCTAssertEqual(
            RecordingPlan.videoBitrate(width: 2560, height: 1440),
            2560 * 1440 * RecordingPlan.framesPerSecond / 10)
    }

    func testEvenPixelSize() {
        let size = RegionTestHelpers.evenSize(width: 1001, height: 601, scale: 1)
        XCTAssertEqual(size.width % 2, 0)
        XCTAssertEqual(size.height % 2, 0)
        let retina = RegionTestHelpers.evenSize(width: 500.5, height: 300.5, scale: 2)
        XCTAssertEqual(retina.width, 1000)
        XCTAssertEqual(retina.height, 600)
        let degenerate = RegionTestHelpers.evenSize(width: 0, height: 0, scale: 2)
        XCTAssertGreaterThanOrEqual(degenerate.width, 2)
        XCTAssertGreaterThanOrEqual(degenerate.height, 2)
    }

    func testElapsedAndDurationFormatting() {
        XCTAssertEqual(RecordingPlan.formatElapsed(0), "0:00")
        XCTAssertEqual(RecordingPlan.formatElapsed(65), "1:05")
        XCTAssertEqual(RecordingPlan.formatElapsed(3600 + 61), "1:01:01")
        XCTAssertEqual(AudioNotePlan.formatDuration(-5), "0:00")
    }

    func testPreviewDescribesTheTake() {
        let options = RecordingOptions(
            source: .display(1), microphoneEnabled: true, cameraEnabled: true)
        let preview = RecordingPlan.preview(for: options, durationSeconds: 65)
        XCTAssertTrue(preview.contains("display"))
        XCTAssertTrue(preview.contains("1:05"))
        XCTAssertTrue(preview.contains("narrated"))
        XCTAssertTrue(preview.contains("camera"))

        let silent = RecordingPlan.preview(
            for: RecordingOptions(source: .region(displayID: 1, rect: .zero)), durationSeconds: 5)
        XCTAssertTrue(silent.contains("region"))
        XCTAssertFalse(silent.contains("narrated"))

        let window = RecordingPlan.preview(
            for: RecordingOptions(source: .window(windowID: 42)), durationSeconds: 5)
        XCTAssertTrue(window.contains("window"))
    }
}

final class DisplayResolverTests: XCTestCase {
    private let displays: [(id: UInt32, frame: CGRect)] = [
        (id: 1, frame: CGRect(x: 0, y: 0, width: 1512, height: 982)),
        (id: 2, frame: CGRect(x: 1512, y: 0, width: 2560, height: 1440)),
    ]

    func testResolvesTheDisplayUnderThePointer() {
        XCTAssertEqual(DisplayResolver.displayUnderPointer(CGPoint(x: 100, y: 100), displays: displays), 1)
        XCTAssertEqual(DisplayResolver.displayUnderPointer(CGPoint(x: 2000, y: 500), displays: displays), 2)
    }

    func testPointerOffEveryDisplayFallsBackToTheFirst() {
        XCTAssertEqual(DisplayResolver.displayUnderPointer(CGPoint(x: -50, y: -50), displays: displays), 1)
    }

    func testNoDisplaysYieldsNil() {
        XCTAssertNil(DisplayResolver.displayUnderPointer(.zero, displays: []))
    }
}

enum RegionTestHelpers {
    static func evenSize(width: CGFloat, height: CGFloat, scale: CGFloat) -> (width: Int, height: Int) {
        RecordingPlan.evenPixelSize(width: width, height: height, scale: scale)
    }
}

final class CameraSelfViewPlacementTests: XCTestCase {
    private let recorded = CGRect(x: 100, y: 50, width: 1280, height: 800)

    func testSelfViewLandsFullyInsideTheRecordedRect() {
        for shape in CameraSelfViewShape.allCases {
            let rect = CameraSelfViewPlacement.compute(recordedRect: recorded, shape: shape)
            XCTAssertTrue(recorded.contains(rect), "\(shape) placed outside the recording")
        }
    }

    func testShapeDrivesAspect() {
        let squircle = CameraSelfViewPlacement.compute(recordedRect: recorded, shape: .squircle)
        XCTAssertEqual(squircle.width, squircle.height, accuracy: 0.5)
        let rounded = CameraSelfViewPlacement.compute(recordedRect: recorded, shape: .rounded)
        XCTAssertEqual(rounded.width / rounded.height, 16.0 / 9.0, accuracy: 0.01)
    }

    func testTinyRecordingStillGetsAnInsideWindow() {
        let tiny = CGRect(x: 0, y: 0, width: 200, height: 140)
        let rect = CameraSelfViewPlacement.compute(recordedRect: tiny, shape: .rounded)
        XCTAssertTrue(tiny.contains(rect))
        XCTAssertGreaterThan(rect.width, 0)
    }

    func testDragClampKeepsTheWindowInside() {
        let dragged = CGRect(x: -500, y: 9999, width: 200, height: 200)
        let clamped = CameraSelfViewPlacement.clamp(dragged, into: recorded)
        XCTAssertTrue(recorded.contains(clamped))
    }

    func testZeroRectYieldsZero() {
        XCTAssertEqual(CameraSelfViewPlacement.compute(recordedRect: .zero, shape: .squircle), .zero)
    }
}
