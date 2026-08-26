import Foundation
import XCTest
@testable import CursorPocketMacKit

final class UpdateCheckPlanTests: XCTestCase {
    // MARK: Version ordering

    func testCompareIsNumericPerSegmentNotLexicographic() {
        // The exact trap: "0.4.10" < "0.4.9" as strings.
        XCTAssertEqual(UpdateCheckPlan.compareVersions("0.4.10", "0.4.9"), 1)
        XCTAssertEqual(UpdateCheckPlan.compareVersions("0.10.0", "0.9.0"), 1)
        XCTAssertEqual(UpdateCheckPlan.compareVersions("0.4.9", "0.4.10"), -1)
        XCTAssertEqual(UpdateCheckPlan.compareVersions("1.2.3", "1.2.3"), 0)
    }

    func testMissingSegmentsCountAsZero() {
        XCTAssertEqual(UpdateCheckPlan.compareVersions("1.0", "1.0.0"), 0)
        XCTAssertEqual(UpdateCheckPlan.compareVersions("1.0.1", "1.0"), 1)
    }

    func testNonNumericSegmentTailsAreIgnored() {
        XCTAssertEqual(UpdateCheckPlan.compareVersions("1.0.10-beta", "1.0.9"), 1)
        XCTAssertEqual(UpdateCheckPlan.compareVersions("abc", "0"), 0)
    }

    func testIsNewerStripsLeadingV() {
        XCTAssertTrue(UpdateCheckPlan.isNewer("v0.5.0", than: "0.4.9"))
        XCTAssertTrue(UpdateCheckPlan.isNewer("V0.5.0", than: "v0.4.9"))
        XCTAssertFalse(UpdateCheckPlan.isNewer("v0.4.9", than: "0.4.9"))
        XCTAssertFalse(UpdateCheckPlan.isNewer("0.4.8", than: "0.4.9"))
    }

    // MARK: Daily throttle

    func testShouldCheckWhenNeverChecked() {
        XCTAssertTrue(UpdateCheckPlan.shouldCheck(lastChecked: nil, now: Date(timeIntervalSince1970: 0)))
    }

    func testShouldNotCheckWithinADay() {
        let now = Date(timeIntervalSince1970: 1_000_000)
        let oneHourAgo = now.addingTimeInterval(-3_600)
        XCTAssertFalse(UpdateCheckPlan.shouldCheck(lastChecked: oneHourAgo, now: now))
        XCTAssertFalse(UpdateCheckPlan.shouldCheck(lastChecked: now, now: now))
    }

    func testShouldCheckAfterADay() {
        let now = Date(timeIntervalSince1970: 1_000_000)
        XCTAssertTrue(UpdateCheckPlan.shouldCheck(lastChecked: now.addingTimeInterval(-86_400), now: now))
        XCTAssertTrue(UpdateCheckPlan.shouldCheck(lastChecked: now.addingTimeInterval(-100_000), now: now))
    }

    func testFutureLastCheckedAllowsTheCheck() {
        // A clock set back must not suppress checks indefinitely.
        let now = Date(timeIntervalSince1970: 1_000_000)
        XCTAssertTrue(UpdateCheckPlan.shouldCheck(lastChecked: now.addingTimeInterval(3_600), now: now))
    }

    // MARK: GitHub payload

    private let latestReleaseJSON = Data("""
    {
      "tag_name": "v0.5.0",
      "html_url": "https://github.com/tanmaysh17/cursorpocket/releases/tag/v0.5.0",
      "assets": [
        { "name": "CursorPocketMac-0.5.0.zip" },
        { "name": "CursorPocket-Setup-0.5.0.exe" }
      ]
    }
    """.utf8)

    func testParsesTheLatestReleaseShape() {
        let release = UpdateCheckPlan.parseLatestRelease(latestReleaseJSON)
        XCTAssertEqual(release?.tag, "v0.5.0")
        XCTAssertEqual(release?.version, "0.5.0")
        XCTAssertEqual(release?.htmlURL, "https://github.com/tanmaysh17/cursorpocket/releases/tag/v0.5.0")
        XCTAssertEqual(release?.assetNames, ["CursorPocketMac-0.5.0.zip", "CursorPocket-Setup-0.5.0.exe"])
    }

    func testMalformedOrErrorPayloadReadsAsNoRelease() {
        XCTAssertNil(UpdateCheckPlan.parseLatestRelease(Data("not json".utf8)))
        // GitHub's own error shape has no tag_name — must not read as a release.
        XCTAssertNil(UpdateCheckPlan.parseLatestRelease(Data(#"{"message": "Not Found"}"#.utf8)))
    }

    func testMissingAssetsAndUrlStillParse() {
        let release = UpdateCheckPlan.parseLatestRelease(Data(#"{"tag_name": "0.5.0"}"#.utf8))
        XCTAssertEqual(release?.version, "0.5.0")
        XCTAssertEqual(release?.htmlURL, "")
        XCTAssertEqual(release?.assetNames, [])
    }

    func testAvailabilityComparesTagAgainstCurrentVersion() {
        XCTAssertNotNil(UpdateCheckPlan.availableUpdate(inReleaseData: latestReleaseJSON, currentVersion: "0.4.9"))
        XCTAssertNil(UpdateCheckPlan.availableUpdate(inReleaseData: latestReleaseJSON, currentVersion: "0.5.0"))
        XCTAssertNil(UpdateCheckPlan.availableUpdate(inReleaseData: latestReleaseJSON, currentVersion: "0.6.0"))
        XCTAssertNil(UpdateCheckPlan.availableUpdate(inReleaseData: Data("{}".utf8), currentVersion: "0.4.9"))
    }
}
