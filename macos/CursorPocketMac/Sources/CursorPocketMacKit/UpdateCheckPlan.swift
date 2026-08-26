import Foundation

/// Pure decisions behind the update checker: version ordering, the once-a-day
/// throttle, and parsing GitHub's "latest release" payload. The app side only
/// performs the GET and shows the result — availability itself is decided
/// here so it stays unit-testable.
public enum UpdateCheckPlan {
    /// One check per day. The check is a courtesy, not a sync — anything more
    /// frequent is noise against a public API.
    public static let checkInterval: TimeInterval = 24 * 60 * 60

    public struct Release: Equatable, Sendable {
        /// The raw `tag_name`, e.g. "v0.5.0".
        public let tag: String
        /// The tag with a single leading "v"/"V" stripped, e.g. "0.5.0".
        public let version: String
        /// The human release page — the only thing the app ever opens.
        public let htmlURL: String
        public let assetNames: [String]

        public init(tag: String, htmlURL: String, assetNames: [String]) {
            self.tag = tag
            self.version = Self.stripLeadingV(tag)
            self.htmlURL = htmlURL
            self.assetNames = assetNames
        }

        static func stripLeadingV(_ tag: String) -> String {
            let trimmed = tag.trimmingCharacters(in: .whitespaces)
            if let first = trimmed.first, first == "v" || first == "V" {
                return String(trimmed.dropFirst())
            }
            return trimmed
        }
    }

    // MARK: Version ordering

    /// Numeric per dot-separated segment — "0.4.10" beats "0.4.9", which a
    /// string compare gets wrong. Missing segments count as zero ("1.0" ==
    /// "1.0.0"); a segment's value is its leading digits, so "10-beta" reads
    /// as 10 and a fully non-numeric segment reads as zero.
    public static func compareVersions(_ a: String, _ b: String) -> Int {
        let left = segments(of: a)
        let right = segments(of: b)
        for index in 0..<max(left.count, right.count) {
            let l = index < left.count ? left[index] : 0
            let r = index < right.count ? right[index] : 0
            if l != r { return l < r ? -1 : 1 }
        }
        return 0
    }

    public static func isNewer(_ candidate: String, than current: String) -> Bool {
        compareVersions(
            Release.stripLeadingV(candidate),
            Release.stripLeadingV(current)) > 0
    }

    private static func segments(of version: String) -> [Int] {
        version.split(separator: ".").map { segment in
            Int(segment.prefix(while: { $0.isNumber })) ?? 0
        }
    }

    // MARK: Throttle

    /// Never checked, or a full interval has passed. A `lastChecked` in the
    /// future (clock set back) also allows the check — otherwise a bad clock
    /// would suppress checks indefinitely.
    public static func shouldCheck(lastChecked: Date?, now: Date) -> Bool {
        guard let lastChecked else { return true }
        let elapsed = now.timeIntervalSince(lastChecked)
        return elapsed < 0 || elapsed >= checkInterval
    }

    // MARK: GitHub payload

    private struct LatestReleasePayload: Decodable {
        struct Asset: Decodable {
            let name: String?
        }

        let tag_name: String?
        let html_url: String?
        let assets: [Asset]?
    }

    /// Parses the `releases/latest` JSON shape. Nil when the payload is not
    /// that shape or carries no tag — an API error body must read as "no
    /// update", never as one.
    public static func parseLatestRelease(_ data: Data) -> Release? {
        guard let payload = try? JSONDecoder().decode(LatestReleasePayload.self, from: data),
              let tag = payload.tag_name, !tag.isEmpty else {
            return nil
        }
        return Release(
            tag: tag,
            htmlURL: payload.html_url ?? "",
            assetNames: (payload.assets ?? []).compactMap(\.name))
    }

    /// The one decision the app acts on: a release exists and its version is
    /// strictly newer than the running one.
    public static func availableUpdate(inReleaseData data: Data, currentVersion: String) -> Release? {
        guard let release = parseLatestRelease(data),
              isNewer(release.version, than: currentVersion) else {
            return nil
        }
        return release
    }
}
