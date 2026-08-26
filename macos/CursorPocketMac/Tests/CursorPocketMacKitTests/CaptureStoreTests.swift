import Foundation
import XCTest
@testable import CursorPocketMacKit

final class CaptureStoreTests: XCTestCase {
    private var root: URL!
    private var store: CaptureStore!
    // 2026-08-21 14:30:09 UTC
    private let fixedDate = Date(timeIntervalSince1970: 1_787_322_609)

    override func setUpWithError() throws {
        root = FileManager.default.temporaryDirectory
            .appendingPathComponent("cursorpocket-tests-\(UUID().uuidString)")
        var counter = 0
        store = CaptureStore(
            rootDirectory: root,
            now: { self.fixedDate },
            shortId: { counter += 1; return String(format: "id%04d", counter) })
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: root)
    }

    func testReserveUsesTheWindowsFolderLayout() {
        let reservation = store.reserve(kind: .screenshot)
        let expectedDay = CaptureStore.dayFolder(fixedDate)
        let expectedTime = CaptureStore.timeStamp(fixedDate)
        XCTAssertEqual(
            reservation.relativePath,
            "\(expectedDay)/screenshots/\(expectedTime)_screenshot_id0001.png")
        XCTAssertTrue(reservation.absoluteURL.path.hasPrefix(root.standardizedFileURL.path))
        XCTAssertTrue(
            FileManager.default.fileExists(atPath: reservation.absoluteURL.deletingLastPathComponent().path),
            "Reserve must create the category directory.")
    }

    func testEveryKindMapsToItsCategoryFolder() {
        XCTAssertTrue(store.reserve(kind: .video).relativePath.contains("/videos/"))
        XCTAssertTrue(store.reserve(kind: .audio).relativePath.contains("/audio/"))
        XCTAssertTrue(store.reserve(kind: .text).relativePath.contains("/text/"))
        XCTAssertTrue(store.reserve(kind: .link).relativePath.contains("/links/"))
    }

    func testRegisterReservationAppendsOneManifestLine() throws {
        let reservation = store.reserve(kind: .screenshot)
        try Data([0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4]).write(to: reservation.absoluteURL)
        var completed: CaptureRecord?
        store.captureCompleted = { record, _ in completed = record }

        let record = try store.registerReservation(reservation, preview: "Region screenshot")

        XCTAssertEqual(completed?.id, record.id)
        let manifest = try String(contentsOf: store.manifestURL, encoding: .utf8)
        XCTAssertEqual(manifest.split(separator: "\n").count, 1)
        let object = try XCTUnwrap(
            JSONSerialization.jsonObject(with: Data(manifest.split(separator: "\n")[0].utf8)) as? [String: Any])
        XCTAssertEqual(object["kind"] as? String, "screenshot")
        XCTAssertEqual(object["path"] as? String, reservation.relativePath)
        XCTAssertEqual(object["schema_version"] as? Int, 2)
    }

    func testRegisterReservationRequiresTheFileOnDisk() {
        let reservation = store.reserve(kind: .screenshot)
        XCTAssertThrowsError(try store.registerReservation(reservation, preview: "missing"))
    }

    func testSaveTextWritesTrailingNewlineAndRejectsEmpty() throws {
        let record = try store.saveText("  hello capture  ")
        let url = try store.absoluteURL(for: record)
        XCTAssertEqual(try String(contentsOf: url, encoding: .utf8), "hello capture\n")
        XCTAssertEqual(record.preview, "hello capture")
        XCTAssertThrowsError(try store.saveText("   \n "))
    }

    func testSaveLinkWritesInternetShortcutAndHostMetadata() throws {
        let record = try store.saveLink("https://www.example.com/docs?a=1")
        let url = try store.absoluteURL(for: record)
        let body = try String(contentsOf: url, encoding: .utf8)
        XCTAssertEqual(body, "[InternetShortcut]\nURL=https://www.example.com/docs?a=1\n")
        XCTAssertEqual(record.metadata["host"], .string("example.com"))
        XCTAssertThrowsError(try store.saveLink("ftp://example.com/file"))
        XCTAssertThrowsError(try store.saveLink("not a url"))
    }

    // MARK: Text editing

    func testUpdateTextRewritesFilePreviewAndManifestLineInPlace() throws {
        _ = try store.saveText("first")
        let target = try store.saveText("second")
        _ = try store.saveText("third")

        let updated = try store.updateText(record: target, newText: "  edited body  ")

        // Same id, same path, recompacted preview.
        XCTAssertEqual(updated.id, target.id)
        XCTAssertEqual(updated.relativePath, target.relativePath)
        XCTAssertEqual(updated.preview, "edited body")

        // The file holds the trimmed content with the store's trailing newline.
        let url = try store.absoluteURL(for: target)
        XCTAssertEqual(try String(contentsOf: url, encoding: .utf8), "edited body\n")

        // Still exactly one manifest line per capture, in the original order —
        // the edited record's line was replaced, not dropped and re-appended.
        let manifest = try String(contentsOf: store.manifestURL, encoding: .utf8)
        XCTAssertEqual(manifest.split(separator: "\n").count, 3)
        XCTAssertEqual(store.recent().map(\.preview), ["third", "edited body", "first"])
        XCTAssertEqual(
            store.recent().filter { $0.id == target.id }.count, 1,
            "The same Library row updates; no duplicate is appended")
    }

    func testUpdateTextRejectsEmptyTextAndLeavesTheCaptureUntouched() throws {
        let record = try store.saveText("keep me")
        XCTAssertThrowsError(try store.updateText(record: record, newText: "  \n ")) { error in
            XCTAssertEqual(error as? CaptureStoreError, .emptyText)
        }
        let url = try store.absoluteURL(for: record)
        XCTAssertEqual(try String(contentsOf: url, encoding: .utf8), "keep me\n")
        XCTAssertEqual(store.recent().map(\.preview), ["keep me"])
    }

    func testUpdateTextRejectsNonTextCaptures() throws {
        let reservation = store.reserve(kind: .screenshot)
        try Data([0x89, 0x50, 0x4E, 0x47]).write(to: reservation.absoluteURL)
        let record = try store.registerReservation(reservation, preview: "shot")
        XCTAssertThrowsError(try store.updateText(record: record, newText: "text")) { error in
            XCTAssertEqual(error as? CaptureStoreError, .notATextCapture(record.id))
        }
    }

    func testUpdateTextRequiresTheFileOnDisk() throws {
        let record = try store.saveText("soon gone")
        let url = try store.absoluteURL(for: record)
        try FileManager.default.removeItem(at: url)
        XCTAssertThrowsError(try store.updateText(record: record, newText: "new text")) { error in
            XCTAssertEqual(error as? CaptureStoreError, .missingCaptureFile(url.path))
        }
        // The manifest still says what it said before the failed edit.
        XCTAssertEqual(store.recent().map(\.preview), ["soon gone"])
    }

    func testUpdateTextRequiresTheRecordInTheIndex() throws {
        let record = try store.saveText("orphaned")
        try store.removeFromIndex(id: record.id)
        XCTAssertThrowsError(try store.updateText(record: record, newText: "new text")) { error in
            XCTAssertEqual(error as? CaptureStoreError, .notInIndex(record.id))
        }
        // The content file was left exactly as it was.
        let url = try store.absoluteURL(for: record)
        XCTAssertEqual(try String(contentsOf: url, encoding: .utf8), "orphaned\n")
    }

    func testRecentReturnsNewestFirstAndSkipsCorruptLines() throws {
        _ = try store.saveText("first")
        _ = try store.saveText("second")
        let handle = try FileHandle(forWritingTo: store.manifestURL)
        try handle.seekToEnd()
        try handle.write(contentsOf: Data("{not json}\n".utf8))
        try handle.close()
        _ = try store.saveText("third")

        let records = store.recent(limit: 10)
        XCTAssertEqual(records.map(\.preview), ["third", "second", "first"])
        XCTAssertEqual(store.recent(limit: 1).map(\.preview), ["third"])
    }

    func testRecentRejectsUnsafeRelativePaths() throws {
        let hostile = CaptureRecord(
            id: "evil", kind: "text", createdAt: "2026-08-26T00:00:00+00:00",
            relativePath: "../../outside.txt", preview: "evil")
        let line = try JSONEncoder().encode(hostile)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        try (line + Data("\n".utf8)).write(to: store.manifestURL)
        XCTAssertTrue(store.recent().isEmpty)
    }

    func testRemoveFromIndexKeepsTheFileOnDisk() throws {
        let record = try store.saveText("keep my file")
        let url = try store.absoluteURL(for: record)
        try store.removeFromIndex(id: record.id)
        XCTAssertTrue(store.recent().isEmpty)
        XCTAssertTrue(
            FileManager.default.fileExists(atPath: url.path),
            "Removing from the index must never delete the capture itself.")
    }

    func testRecoveryRegistersOrphansAndSkipsPartialsAndTinyFiles() throws {
        // A healthy orphan: registered file removed from index.
        let orphan = store.reserve(kind: .screenshot)
        try Data(repeating: 7, count: 64).write(to: orphan.absoluteURL)

        // A partial video must be skipped.
        let video = store.reserve(kind: .video)
        let partial = RecordingPlan.partialURL(for: video.absoluteURL)
        try Data(repeating: 7, count: 4096).write(to: partial)

        // A too-small video is a failed write, not a capture.
        let tiny = store.reserve(kind: .video)
        try Data(repeating: 7, count: 10).write(to: tiny.absoluteURL)

        let recovered = store.recoverOrphanedMedia()
        XCTAssertEqual(recovered.count, 1)
        XCTAssertEqual(recovered[0].relativePath, orphan.relativePath)
        XCTAssertEqual(recovered[0].metadata["recovered"], .bool(true))

        // Running again recovers nothing new.
        XCTAssertTrue(store.recoverOrphanedMedia().isEmpty)
    }

    func testImportFileCopiesRatherThanMoves() throws {
        let source = FileManager.default.temporaryDirectory
            .appendingPathComponent("import-\(UUID().uuidString).png")
        try Data(repeating: 1, count: 32).write(to: source)
        defer { try? FileManager.default.removeItem(at: source) }

        let record = try store.importFile(kind: .screenshot, from: source, preview: "Imported")
        XCTAssertTrue(FileManager.default.fileExists(atPath: source.path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: try store.absoluteURL(for: record).path))
    }

    func testCompactCollapsesWhitespaceAndBoundsLength() {
        XCTAssertEqual(CaptureStore.compact("a\n b\t\tc"), "a b c")
        let long = String(repeating: "word ", count: 40)
        let compacted = CaptureStore.compact(long)
        XCTAssertLessThanOrEqual(compacted.count, 96)
        XCTAssertTrue(compacted.hasSuffix("…"))
    }

    func testSafeRelativePathRules() {
        XCTAssertTrue(CaptureStore.isSafeRelativePath("2026-08-26/text/a.txt"))
        XCTAssertFalse(CaptureStore.isSafeRelativePath("/etc/passwd"))
        XCTAssertFalse(CaptureStore.isSafeRelativePath("a/../../b"))
        XCTAssertFalse(CaptureStore.isSafeRelativePath("  "))
        XCTAssertFalse(CaptureStore.isSafeRelativePath("a\\..\\b"))
    }
}
