import CoreGraphics
import Foundation
import XCTest
@testable import CursorPocketMacKit

final class CursorCompanionPlacementTests: XCTestCase {
    private let screen = CGRect(x: 0, y: 0, width: 1440, height: 900)
    private let d = CursorCompanionPlacement.diameter
    private let gap = CursorCompanionPlacement.gap

    func testDotTrailsToTheLowerRightOfThePointer() {
        let origin = CursorCompanionPlacement.desiredOrigin(
            pointer: CGPoint(x: 400, y: 500), in: screen)
        XCTAssertEqual(origin.x, 400 + gap)
        XCTAssertEqual(origin.y, 500 - gap - d)
    }

    func testDotFlipsLeftAtTheRightEdge() {
        let origin = CursorCompanionPlacement.desiredOrigin(
            pointer: CGPoint(x: screen.maxX - 4, y: 500), in: screen)
        XCTAssertEqual(origin.x, screen.maxX - 4 - gap - d)
        XCTAssertLessThanOrEqual(origin.x + d, screen.maxX)
    }

    func testDotFlipsAboveAtTheBottomEdge() {
        let origin = CursorCompanionPlacement.desiredOrigin(
            pointer: CGPoint(x: 400, y: 4), in: screen)
        XCTAssertEqual(origin.y, 4 + gap)
        XCTAssertGreaterThanOrEqual(origin.y, screen.minY)
    }

    func testDotStaysInsideTheScreenEvenInATightCorner() {
        let origin = CursorCompanionPlacement.desiredOrigin(
            pointer: CGPoint(x: screen.maxX, y: screen.minY), in: screen)
        XCTAssertGreaterThanOrEqual(origin.x, screen.minX)
        XCTAssertGreaterThanOrEqual(origin.y, screen.minY)
        XCTAssertLessThanOrEqual(origin.x + d, screen.maxX)
        XCTAssertLessThanOrEqual(origin.y + d, screen.maxY)
    }

    func testEmptyBoundsSkipsClampingRatherThanCollapsingToZero() {
        let origin = CursorCompanionPlacement.desiredOrigin(
            pointer: CGPoint(x: 100, y: 100), in: .zero)
        XCTAssertEqual(origin.x, 100 + gap)
        XCTAssertEqual(origin.y, 100 - gap - d)
    }

    func testDotHoldsStillWhileThePointerIsOverIt() {
        // The dot must be clickable: if it fled the approaching pointer the
        // user could never land on it.
        let frame = CGRect(x: 200, y: 200, width: d, height: d)
        XCTAssertFalse(CursorCompanionPlacement.shouldMove(
            currentFrame: frame,
            pointer: CGPoint(x: 200 + d / 2, y: 200 + d / 2),
            target: CGPoint(x: 500, y: 500)))
        // Within the slop just outside the frame still counts as hovering.
        XCTAssertFalse(CursorCompanionPlacement.shouldMove(
            currentFrame: frame,
            pointer: CGPoint(x: 200 - CursorCompanionPlacement.hoverSlop / 2, y: 205),
            target: CGPoint(x: 500, y: 500)))
    }

    func testSubThresholdDeltaDoesNotMove() {
        let frame = CGRect(x: 200, y: 200, width: d, height: d)
        XCTAssertFalse(CursorCompanionPlacement.shouldMove(
            currentFrame: frame,
            pointer: CGPoint(x: 700, y: 700),
            target: CGPoint(x: 201.5, y: 201.5)))
        XCTAssertTrue(CursorCompanionPlacement.shouldMove(
            currentFrame: frame,
            pointer: CGPoint(x: 700, y: 700),
            target: CGPoint(x: 210, y: 200)))
    }
}

final class ReceiptPolicyTests: XCTestCase {
    func testAnnotateIsOfferedForScreenshotsOnly() {
        XCTAssertEqual(
            ReceiptPolicy.actions(for: .screenshot),
            [.open, .reveal, .annotate, .dismiss])
        for kind in CaptureKind.allCases where kind != .screenshot {
            XCTAssertEqual(ReceiptPolicy.actions(for: kind), [.open, .reveal, .dismiss], "\(kind)")
        }
    }

    func testReceiptKeysRequireControlOption() {
        // A receipt does not own the user's attention: without the full
        // Control+Option chord no key may reach it.
        for action in ReceiptAction.allCases {
            XCTAssertNil(ReceiptPolicy.action(
                forKey: action.key, kind: .screenshot, hasControlOption: false), "\(action)")
        }
        XCTAssertEqual(
            ReceiptPolicy.action(forKey: "o", kind: .screenshot, hasControlOption: true), .open)
        XCTAssertEqual(
            ReceiptPolicy.action(forKey: "R", kind: .video, hasControlOption: true), .reveal)
        XCTAssertEqual(
            ReceiptPolicy.action(forKey: "d", kind: .audio, hasControlOption: true), .dismiss)
    }

    func testKeysForUnofferedActionsDoNothing() {
        XCTAssertNil(ReceiptPolicy.action(forKey: "a", kind: .video, hasControlOption: true))
        XCTAssertEqual(
            ReceiptPolicy.action(forKey: "a", kind: .screenshot, hasControlOption: true), .annotate)
        XCTAssertNil(ReceiptPolicy.action(forKey: "x", kind: .screenshot, hasControlOption: true))
    }

    func testReceiptLandsBottomRightOfTheVisibleFrame() {
        let frame = CGRect(x: 0, y: 25, width: 1440, height: 875)
        let size = CGSize(width: 360, height: 88)
        let origin = ReceiptPolicy.origin(inVisibleFrame: frame, panelSize: size)
        XCTAssertEqual(origin.x, frame.maxX - ReceiptPolicy.margin - size.width)
        XCTAssertEqual(origin.y, frame.minY + ReceiptPolicy.margin)
    }

    func testEmptyFrameYieldsZeroOrigin() {
        XCTAssertEqual(
            ReceiptPolicy.origin(inVisibleFrame: .zero, panelSize: CGSize(width: 360, height: 88)),
            .zero)
    }

    func testAutoDismissIsSixSeconds() {
        XCTAssertEqual(ReceiptPolicy.autoDismissSeconds, 6, accuracy: 0.001)
    }
}

final class PinnedCapturePlacementTests: XCTestCase {
    func testLargeImageIsScaledIntoTheBoxPreservingAspect() {
        let size = PinnedCapturePlacement.fitSize(imageSize: CGSize(width: 3600, height: 2800))
        XCTAssertLessThanOrEqual(size.width, PinnedCapturePlacement.maxWidth)
        XCTAssertLessThanOrEqual(size.height, PinnedCapturePlacement.maxHeight)
        XCTAssertEqual(size.width / size.height, 3600.0 / 2800.0, accuracy: 0.001)
    }

    func testSmallImageIsNeverUpscaled() {
        let size = PinnedCapturePlacement.fitSize(imageSize: CGSize(width: 200, height: 150))
        XCTAssertEqual(size, CGSize(width: 200, height: 150))
    }

    func testSliverGrowsToAGrabbableSizeButNeverDwarfsTheBox() {
        let size = PinnedCapturePlacement.fitSize(imageSize: CGSize(width: 1000, height: 20))
        XCTAssertGreaterThan(size.height, 20 * (PinnedCapturePlacement.maxWidth / 1000))
        XCTAssertLessThanOrEqual(size.width, PinnedCapturePlacement.maxWidth * 2)
        XCTAssertLessThanOrEqual(size.height, PinnedCapturePlacement.maxHeight * 2)
    }

    func testDegenerateImageYieldsZero() {
        XCTAssertEqual(PinnedCapturePlacement.fitSize(imageSize: .zero), .zero)
        XCTAssertEqual(
            PinnedCapturePlacement.fitSize(imageSize: CGSize(width: -10, height: 40)), .zero)
    }

    func testPinsCascadeDownLeftFromTheTopRight() {
        let frame = CGRect(x: 0, y: 25, width: 1440, height: 875)
        let size = CGSize(width: 300, height: 200)
        let first = PinnedCapturePlacement.origin(inVisibleFrame: frame, panelSize: size, pinIndex: 0)
        let second = PinnedCapturePlacement.origin(inVisibleFrame: frame, panelSize: size, pinIndex: 1)
        XCTAssertEqual(first.x, frame.maxX - PinnedCapturePlacement.margin - size.width)
        XCTAssertEqual(first.y, frame.maxY - PinnedCapturePlacement.margin - size.height)
        XCTAssertEqual(second.x, first.x - PinnedCapturePlacement.cascadeStep)
        XCTAssertEqual(second.y, first.y - PinnedCapturePlacement.cascadeStep)
    }

    func testALongCascadeStaysOnScreen() {
        let frame = CGRect(x: 0, y: 25, width: 1440, height: 875)
        let size = CGSize(width: 300, height: 200)
        let origin = PinnedCapturePlacement.origin(
            inVisibleFrame: frame, panelSize: size, pinIndex: 60)
        XCTAssertGreaterThanOrEqual(origin.x, frame.minX)
        XCTAssertGreaterThanOrEqual(origin.y, frame.minY)
        XCTAssertLessThanOrEqual(origin.x + size.width, frame.maxX)
        XCTAssertLessThanOrEqual(origin.y + size.height, frame.maxY)
    }
}
