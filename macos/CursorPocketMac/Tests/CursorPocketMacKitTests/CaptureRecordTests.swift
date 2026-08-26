import Foundation
import XCTest
@testable import CursorPocketMacKit

final class CaptureRecordTests: XCTestCase {
    func testDecodesWindowsWrittenManifestLine() throws {
        // Field names and shapes as the Windows app serializes them.
        let line = """
        {"schema_version":2,"id":"20260826T101530-abc123","kind":"screenshot","created_at":"2026-08-26T10:15:30.1234567-07:00","path":"2026-08-26/screenshots/10-15-30_screenshot_abc123.png","preview":"Region screenshot","metadata":{"width":1920,"height":1080,"recovered":false}}
        """
        let record = try JSONDecoder().decode(CaptureRecord.self, from: Data(line.utf8))
        XCTAssertEqual(record.schemaVersion, 2)
        XCTAssertEqual(record.id, "20260826T101530-abc123")
        XCTAssertEqual(record.captureKind, .screenshot)
        XCTAssertEqual(record.relativePath, "2026-08-26/screenshots/10-15-30_screenshot_abc123.png")
        XCTAssertEqual(record.metadata["width"], .number(1920))
        XCTAssertEqual(record.metadata["recovered"], .bool(false))
        XCTAssertNotEqual(record.created, .distantPast)
    }

    func testEncodesTheExactWireFieldNames() throws {
        let record = CaptureRecord(
            id: "x", kind: "link", createdAt: "2026-08-26T10:15:30.0000000+00:00",
            relativePath: "2026-08-26/links/a.url", preview: "p",
            metadata: ["url": .string("https://example.com/a")])
        let data = try JSONEncoder().encode(record)
        let object = try XCTUnwrap(JSONSerialization.jsonObject(with: data) as? [String: Any])
        XCTAssertEqual(
            Set(object.keys),
            ["schema_version", "id", "kind", "created_at", "path", "preview", "metadata"])
    }

    func testUnknownKindFallsBackToText() {
        XCTAssertEqual(CaptureKind.parseStorageValue("hologram"), .text)
        XCTAssertEqual(CaptureKind.parseStorageValue("VIDEO"), .video)
    }

    func testMissingOptionalFieldsGetDefaults() throws {
        let line = """
        {"id":"a","kind":"text","created_at":"2026-08-26T10:15:30+00:00","path":"d/text/a.txt","preview":"p"}
        """
        let record = try JSONDecoder().decode(CaptureRecord.self, from: Data(line.utf8))
        XCTAssertEqual(record.schemaVersion, 2)
        XCTAssertTrue(record.metadata.isEmpty)
    }
}

final class CaptureTimestampTests: XCTestCase {
    func testFormatsSevenFractionalDigitsWithNumericOffset() {
        let date = Date(timeIntervalSince1970: 1_787_322_609)
        let text = CaptureTimestamp.format(date, timeZone: TimeZone(identifier: "UTC")!)
        XCTAssertEqual(text, "2026-08-21T14:30:09.0000000+00:00")
    }

    func testParsesItsOwnOutput() {
        let date = Date(timeIntervalSince1970: 1_787_322_609)
        let text = CaptureTimestamp.format(date, timeZone: TimeZone(identifier: "America/Los_Angeles")!)
        let parsed = CaptureTimestamp.parse(text)
        XCTAssertNotNil(parsed)
        XCTAssertEqual(parsed!.timeIntervalSince1970, date.timeIntervalSince1970, accuracy: 0.01)
    }

    func testParsesDotNetRoundTripFormat() {
        let parsed = CaptureTimestamp.parse("2026-08-26T10:15:30.1234567-07:00")
        XCTAssertNotNil(parsed)
    }

    func testParsesWholeSecondTimestamps() {
        XCTAssertNotNil(CaptureTimestamp.parse("2026-08-26T10:15:30+00:00"))
    }
}
