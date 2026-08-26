import Foundation
import XCTest
@testable import CursorPocketMacKit

/// Reads the cross-platform golden fixtures in `spec/capture-manifest/`.
/// The Windows test suite reads the exact same files, so either side
/// drifting from the shared storage contract breaks a CI.
final class GoldenContractTests: XCTestCase {
    private func specDirectory() throws -> URL {
        // Walk up from this source file until the repo root (the directory
        // containing `spec`) — the tests run from an arbitrary build folder.
        var url = URL(fileURLWithPath: #filePath)
        while url.pathComponents.count > 1 {
            url.deleteLastPathComponent()
            let spec = url.appendingPathComponent("spec", isDirectory: true)
            var isDirectory: ObjCBool = false
            if FileManager.default.fileExists(atPath: spec.path, isDirectory: &isDirectory),
               isDirectory.boolValue {
                return spec.appendingPathComponent("capture-manifest", isDirectory: true)
            }
        }
        // A skip here would let contract drift pass silently, so fail hard.
        struct SpecDirectoryNotFound: Error {}
        XCTFail("Repo root with spec/ not found above \(#filePath)")
        throw SpecDirectoryNotFound()
    }

    private func goldenLines() throws -> [String] {
        let url = try specDirectory().appendingPathComponent("golden.jsonl")
        let text = try String(contentsOf: url, encoding: .utf8)
        return text.split(whereSeparator: \.isNewline).map(String.init)
    }

    func testGoldenManifestCoversEveryKindAndDecodesWithTheRealRecord() throws {
        let lines = try goldenLines()
        XCTAssertEqual(lines.count, 5)
        let decoder = JSONDecoder()
        var records: [CaptureRecord] = []
        for line in lines {
            records.append(try decoder.decode(CaptureRecord.self, from: Data(line.utf8)))
        }
        XCTAssertEqual(
            records.map(\.captureKind),
            [.screenshot, .video, .audio, .text, .link])
        for record in records {
            XCTAssertEqual(record.schemaVersion, 2)
            XCTAssertFalse(record.id.isEmpty)
            XCTAssertFalse(record.preview.isEmpty)
            // Windows-shaped values: forward-slash relative paths under a
            // dated folder, in the kind's category, with the kind's extension.
            XCTAssertFalse(record.relativePath.contains("\\"))
            XCTAssertTrue(record.relativePath.hasPrefix("2026-08-18/"))
            XCTAssertTrue(record.relativePath.contains("/\(record.captureKind.category)/"))
            XCTAssertTrue(record.relativePath.hasSuffix(record.captureKind.fileExtension))
            XCTAssertTrue(CaptureStore.isSafeRelativePath(record.relativePath))
            // .NET "O" timestamps (seven fractional digits, numeric offset)
            // must parse to a real instant.
            XCTAssertNotEqual(record.created, .distantPast, "createdAt failed to parse: \(record.createdAt)")
        }
    }

    func testGoldenMetadataValueShapesSurvive() throws {
        let decoder = JSONDecoder()
        let records = try goldenLines().map {
            try decoder.decode(CaptureRecord.self, from: Data($0.utf8))
        }
        XCTAssertEqual(records[0].metadata["width"], .number(1920))
        XCTAssertEqual(records[0].metadata["height"], .number(1080))
        XCTAssertEqual(records[0].metadata["recovered"], .bool(false))
        XCTAssertEqual(records[1].metadata["duration_seconds"], .number(42.5))
        XCTAssertEqual(records[1].metadata["recovered"], .bool(true))
        XCTAssertEqual(records[2].metadata["duration_seconds"], .number(14.2))
        XCTAssertTrue(records[3].metadata.isEmpty)
        XCTAssertEqual(records[4].metadata["url"], .string("https://www.example.com/docs/page"))
        XCTAssertEqual(records[4].metadata["host"], .string("example.com"))
    }

    func testReencodingKeepsTheExactWireKeySet() throws {
        let decoder = JSONDecoder()
        let encoder = JSONEncoder()
        for line in try goldenLines() {
            let original = try XCTUnwrap(
                try JSONSerialization.jsonObject(with: Data(line.utf8)) as? [String: Any])
            let record = try decoder.decode(CaptureRecord.self, from: Data(line.utf8))
            let reencoded = try XCTUnwrap(
                try JSONSerialization.jsonObject(with: encoder.encode(record)) as? [String: Any])
            XCTAssertEqual(Set(reencoded.keys), Set(original.keys), "Key drift in line: \(line)")
            let originalMetadata = try XCTUnwrap(original["metadata"] as? [String: Any])
            let reencodedMetadata = try XCTUnwrap(reencoded["metadata"] as? [String: Any])
            XCTAssertEqual(Set(reencodedMetadata.keys), Set(originalMetadata.keys))
        }
    }

    func testGoldenUrlBodyParsesWithLinkCapture() throws {
        let url = try specDirectory().appendingPathComponent("golden.url")
        let body = try String(contentsOf: url, encoding: .utf8)
        XCTAssertTrue(body.hasPrefix("[InternetShortcut]"))
        XCTAssertEqual(
            LinkCapture.url(fromInternetShortcut: body),
            "https://www.example.com/docs/page")
        // The canonical body is exactly what this app would write back.
        XCTAssertEqual(
            LinkCapture.internetShortcutBody(for: "https://www.example.com/docs/page"), body)
    }
}
