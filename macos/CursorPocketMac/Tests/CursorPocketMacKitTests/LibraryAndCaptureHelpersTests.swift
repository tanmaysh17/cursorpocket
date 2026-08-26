import CoreGraphics
import Foundation
import XCTest
@testable import CursorPocketMacKit

final class LibraryModelTests: XCTestCase {
    private func record(_ preview: String, kind: CaptureKind, at iso: String) -> CaptureRecord {
        CaptureRecord(
            id: UUID().uuidString, kind: kind.storageValue, createdAt: iso,
            relativePath: "d/\(kind.category)/x\(kind.fileExtension)", preview: preview)
    }

    func testFilterAndSearchComposeAndIgnoreCase() {
        let records = [
            record("Region screenshot", kind: .screenshot, at: "2026-08-26T10:00:00+00:00"),
            record("standup notes", kind: .text, at: "2026-08-26T09:00:00+00:00"),
            record("https://example.com", kind: .link, at: "2026-08-26T08:00:00+00:00"),
        ]
        XCTAssertEqual(LibraryModel.filter(records, by: .text, search: "").count, 1)
        XCTAssertEqual(LibraryModel.filter(records, by: .all, search: "NOTES").map(\.preview), ["standup notes"])
        XCTAssertEqual(LibraryModel.filter(records, by: .screenshot, search: "notes").count, 0)
        XCTAssertEqual(LibraryModel.filter(records, by: .all, search: "  ").count, 3)
    }

    func testGroupsByDayWithHumanLabels() {
        let utc = TimeZone(identifier: "UTC")!
        let today = CaptureTimestamp.parse("2026-08-26T12:00:00+00:00")!
        let records = [
            record("a", kind: .text, at: "2026-08-26T10:00:00+00:00"),
            record("b", kind: .text, at: "2026-08-26T09:00:00+00:00"),
            record("c", kind: .text, at: "2026-08-25T22:00:00+00:00"),
            record("d", kind: .text, at: "2026-08-20T08:00:00+00:00"),
        ]
        let groups = LibraryModel.groupByDay(records, today: today, timeZone: utc)
        XCTAssertEqual(groups.count, 3)
        XCTAssertEqual(groups[0].label, "Today")
        XCTAssertEqual(groups[0].records.count, 2)
        XCTAssertEqual(groups[1].label, "Yesterday")
        XCTAssertEqual(groups[2].label, "Thursday, Aug 20")
    }

    func testFileSizeFormatting() {
        XCTAssertEqual(LibraryModel.formatFileSize(0), "0 B")
        XCTAssertEqual(LibraryModel.formatFileSize(999), "999 B")
        XCTAssertEqual(LibraryModel.formatFileSize(2048), "2.0 KB")
        XCTAssertEqual(LibraryModel.formatFileSize(5 * 1024 * 1024), "5.0 MB")
        XCTAssertEqual(LibraryModel.formatFileSize(-3), "0 B")
    }
}

final class LinkCaptureTests: XCTestCase {
    func testValidationRequiresHttpWithAHost() {
        XCTAssertEqual(LinkCapture.validate(" https://www.example.com/a ")?.host, "example.com")
        XCTAssertEqual(LinkCapture.validate("http://sub.example.com")?.host, "sub.example.com")
        XCTAssertNil(LinkCapture.validate("ftp://example.com"))
        XCTAssertNil(LinkCapture.validate("file:///etc/passwd"))
        XCTAssertNil(LinkCapture.validate("https://"))
        XCTAssertNil(LinkCapture.validate("just words"))
    }

    func testShortcutBodyRoundTrips() {
        let body = LinkCapture.internetShortcutBody(for: "https://example.com/x?y=1")
        XCTAssertEqual(LinkCapture.url(fromInternetShortcut: body), "https://example.com/x?y=1")
        // And the Windows-written CRLF variant parses too.
        XCTAssertEqual(
            LinkCapture.url(fromInternetShortcut: "[InternetShortcut]\r\nURL=https://a.b/c\r\n"),
            "https://a.b/c")
        XCTAssertNil(LinkCapture.url(fromInternetShortcut: "[InternetShortcut]\nURL=\n"))
    }
}

final class RegionSelectionTests: XCTestCase {
    func testRectNormalizesAnyDragDirection() {
        let expected = CGRect(x: 10, y: 20, width: 30, height: 40)
        XCTAssertEqual(RegionSelection.rect(from: CGPoint(x: 10, y: 20), to: CGPoint(x: 40, y: 60)), expected)
        XCTAssertEqual(RegionSelection.rect(from: CGPoint(x: 40, y: 60), to: CGPoint(x: 10, y: 20)), expected)
        XCTAssertEqual(RegionSelection.rect(from: CGPoint(x: 40, y: 20), to: CGPoint(x: 10, y: 60)), expected)
    }

    func testClampAndUsability() {
        let bounds = CGRect(x: 0, y: 0, width: 100, height: 100)
        let clamped = RegionSelection.clamp(CGRect(x: 90, y: 90, width: 50, height: 50), to: bounds)
        XCTAssertEqual(clamped, CGRect(x: 90, y: 90, width: 10, height: 10))
        XCTAssertEqual(RegionSelection.clamp(CGRect(x: 500, y: 500, width: 5, height: 5), to: bounds), .zero)
        XCTAssertTrue(RegionSelection.isUsable(clamped))
        XCTAssertFalse(RegionSelection.isUsable(CGRect(x: 0, y: 0, width: 3, height: 100)))
    }

    func testCaptureArgumentIsIntegral() {
        XCTAssertEqual(
            RegionSelection.captureArgument(for: CGRect(x: 10.4, y: 20.6, width: 100.2, height: 50.9)),
            "10,20,101,52")
    }
}

final class ScreenshotModeTests: XCTestCase {
    func testModeArguments() {
        XCTAssertEqual(
            ScreenshotCapture.screencaptureArguments(mode: .interactive, savingTo: "/t/a.png"),
            ["-i", "-x", "/t/a.png"])
        XCTAssertEqual(
            ScreenshotCapture.screencaptureArguments(mode: .window, savingTo: "/t/a.png"),
            ["-i", "-W", "-x", "-o", "/t/a.png"])
        XCTAssertEqual(
            ScreenshotCapture.screencaptureArguments(mode: .display(2), savingTo: "/t/a.png"),
            ["-D", "2", "-x", "/t/a.png"])
        XCTAssertEqual(
            ScreenshotCapture.screencaptureArguments(mode: .display(0), savingTo: "/t/a.png"),
            ["-D", "1", "-x", "/t/a.png"])
        XCTAssertEqual(
            ScreenshotCapture.screencaptureArguments(
                mode: .rect(CGRect(x: 1, y: 2, width: 3, height: 4)), savingTo: "/t/a.png"),
            ["-R", "1,2,3,4", "-x", "/t/a.png"])
    }
}

final class SettingsStoreTests: XCTestCase {
    private func makeStore() -> (MacSettingsStore, URL) {
        let directory = FileManager.default.temporaryDirectory
            .appendingPathComponent("cp-settings-\(UUID().uuidString)")
        let store = MacSettingsStore(
            settingsURL: directory.appendingPathComponent("settings.json"),
            defaultCaptureRoot: URL(fileURLWithPath: "/Users/example/Documents/CursorPocket Captures"))
        return (store, directory)
    }

    func testMissingFileYieldsNormalizedDefaults() {
        let (store, directory) = makeStore()
        defer { try? FileManager.default.removeItem(at: directory) }
        let settings = store.load()
        XCTAssertTrue(settings.hotkeysEnabled)
        XCTAssertFalse(settings.lastMicrophoneEnabled, "Recording inputs default to off")
        XCTAssertFalse(settings.lastCameraEnabled, "Recording inputs default to off")
        XCTAssertNil(settings.captureRootPath)
        XCTAssertEqual(
            store.captureRoot(for: settings).path,
            "/Users/example/Documents/CursorPocket Captures")
    }

    func testSaveLoadRoundTripAndNormalization() throws {
        let (store, directory) = makeStore()
        defer { try? FileManager.default.removeItem(at: directory) }
        var settings = MacAppSettings()
        settings.captureRootPath = "   "
        settings.palettePlacement = PalettePlacement(xFraction: 9, yFraction: -9)
        settings.cameraShape = .rounded
        try store.save(settings)

        let loaded = store.load()
        XCTAssertNil(loaded.captureRootPath)
        XCTAssertEqual(loaded.palettePlacement, PalettePlacement(xFraction: 1, yFraction: 0))
        XCTAssertEqual(loaded.cameraShape, .rounded)
    }

    func testPartialSettingsFileGetsDefaultsForMissingKeys() throws {
        let (store, directory) = makeStore()
        defer { try? FileManager.default.removeItem(at: directory) }
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        try Data(#"{"captureRootPath":"/tmp/lib"}"#.utf8).write(to: store.settingsURL)
        let loaded = store.load()
        XCTAssertEqual(loaded.captureRootPath, "/tmp/lib")
        XCTAssertTrue(loaded.hotkeysEnabled)
        XCTAssertTrue(loaded.gestureEnabled, "The double-circle gesture defaults on, as on Windows")
        XCTAssertEqual(store.captureRoot(for: loaded).path, "/tmp/lib")
    }
}
