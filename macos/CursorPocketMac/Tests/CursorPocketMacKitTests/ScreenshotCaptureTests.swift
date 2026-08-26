import Foundation
import XCTest
@testable import CursorPocketMacKit

final class ScreenshotCaptureTests: XCTestCase {
    private let documents = URL(fileURLWithPath: "/Users/example/Documents", isDirectory: true)

    func testCaptureFolderMatchesWindowsLayout() {
        let folder = ScreenshotCapture.captureFolder(inDocuments: documents)
        XCTAssertEqual(folder.path, "/Users/example/Documents/CursorPocket Captures")
        XCTAssertTrue(folder.hasDirectoryPath)
    }

    func testScreenshotDestinationUsesPinnedTimestampFormat() {
        let folder = ScreenshotCapture.captureFolder(inDocuments: documents)
        // 2026-08-21 14:30:09 UTC
        let date = Date(timeIntervalSince1970: 1_787_322_609)
        let destination = ScreenshotCapture.screenshotDestination(
            in: folder, at: date, timeZone: TimeZone(identifier: "UTC")!)
        XCTAssertEqual(destination.lastPathComponent, "2026-08-21_14-30-09_screenshot.png")
    }

    func testScreenshotDestinationIsLocaleIndependent() {
        let folder = ScreenshotCapture.captureFolder(inDocuments: documents)
        let date = Date(timeIntervalSince1970: 1_787_322_609)
        let utc = TimeZone(identifier: "UTC")!
        let a = ScreenshotCapture.screenshotDestination(in: folder, at: date, timeZone: utc)
        let b = ScreenshotCapture.screenshotDestination(in: folder, at: date, timeZone: utc)
        XCTAssertEqual(a, b)
        XCTAssertTrue(a.lastPathComponent.hasSuffix("_screenshot.png"))
    }

    func testScreencaptureArgumentsAreInteractiveAndSilent() {
        let arguments = ScreenshotCapture.screencaptureArguments(savingTo: "/tmp/shot.png")
        XCTAssertEqual(arguments, ["-i", "-x", "/tmp/shot.png"])
    }

    func testDidSaveRequiresBothZeroExitAndFileOnDisk() {
        XCTAssertTrue(ScreenshotCapture.didSave(terminationStatus: 0, fileExists: true))
        // Escape during interactive selection: exit 0 but no file written.
        XCTAssertFalse(ScreenshotCapture.didSave(terminationStatus: 0, fileExists: false))
        XCTAssertFalse(ScreenshotCapture.didSave(terminationStatus: 1, fileExists: true))
        XCTAssertFalse(ScreenshotCapture.didSave(terminationStatus: 1, fileExists: false))
    }

    func testStatusMessages() {
        let destination = URL(fileURLWithPath: "/tmp/2026-08-25_10-00-00_screenshot.png")
        XCTAssertEqual(
            ScreenshotCapture.statusMessage(saved: true, destination: destination),
            "Saved 2026-08-25_10-00-00_screenshot.png")
        XCTAssertEqual(
            ScreenshotCapture.statusMessage(saved: false, destination: destination),
            "Screenshot cancelled")
    }
}
