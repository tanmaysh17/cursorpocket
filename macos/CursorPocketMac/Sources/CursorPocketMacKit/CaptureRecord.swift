import Foundation

/// One line of `captures.jsonl`. Field names match the Windows
/// `CaptureRecord` exactly so both apps can read the same library.
public struct CaptureRecord: Codable, Equatable, Identifiable, Sendable {
    public var schemaVersion: Int
    public var id: String
    public var kind: String
    public var createdAt: String
    public var relativePath: String
    public var preview: String
    public var metadata: [String: JSONValue]

    enum CodingKeys: String, CodingKey {
        case schemaVersion = "schema_version"
        case id
        case kind
        case createdAt = "created_at"
        case relativePath = "path"
        case preview
        case metadata
    }

    public init(
        schemaVersion: Int = 2,
        id: String,
        kind: String,
        createdAt: String,
        relativePath: String,
        preview: String,
        metadata: [String: JSONValue] = [:]
    ) {
        self.schemaVersion = schemaVersion
        self.id = id
        self.kind = kind
        self.createdAt = createdAt
        self.relativePath = relativePath
        self.preview = preview
        self.metadata = metadata
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        schemaVersion = try container.decodeIfPresent(Int.self, forKey: .schemaVersion) ?? 2
        id = try container.decode(String.self, forKey: .id)
        kind = try container.decode(String.self, forKey: .kind)
        createdAt = try container.decode(String.self, forKey: .createdAt)
        relativePath = try container.decode(String.self, forKey: .relativePath)
        preview = try container.decode(String.self, forKey: .preview)
        metadata = try container.decodeIfPresent([String: JSONValue].self, forKey: .metadata) ?? [:]
    }

    public var captureKind: CaptureKind { CaptureKind.parseStorageValue(kind) }

    public var created: Date { CaptureTimestamp.parse(createdAt) ?? .distantPast }
}

/// The Windows app writes `DateTimeOffset.ToString("O")` — ISO 8601 with seven
/// fractional digits and a numeric offset. Both directions must round-trip.
public enum CaptureTimestamp {
    public static func format(_ date: Date, timeZone: TimeZone = .current) -> String {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = timeZone
        let parts = calendar.dateComponents(
            [.year, .month, .day, .hour, .minute, .second, .nanosecond], from: date)
        let offsetSeconds = timeZone.secondsFromGMT(for: date)
        let sign = offsetSeconds < 0 ? "-" : "+"
        let magnitude = abs(offsetSeconds)
        // .NET writes seven fractional digits; nanoseconds give nine.
        let fraction = String(format: "%07d", (parts.nanosecond ?? 0) / 100)
        return String(
            format: "%04d-%02d-%02dT%02d:%02d:%02d.%@%@%02d:%02d",
            parts.year ?? 1, parts.month ?? 1, parts.day ?? 1,
            parts.hour ?? 0, parts.minute ?? 0, parts.second ?? 0,
            fraction, sign, magnitude / 3600, (magnitude % 3600) / 60)
    }

    public static func parse(_ value: String) -> Date? {
        // ISO8601DateFormatter accepts at most millisecond precision reliably,
        // so trim extra fractional digits before parsing.
        let trimmed = trimFraction(value)
        let withFraction = ISO8601DateFormatter()
        withFraction.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let date = withFraction.date(from: trimmed) { return date }
        let plain = ISO8601DateFormatter()
        plain.formatOptions = [.withInternetDateTime]
        return plain.date(from: trimmed)
    }

    private static func trimFraction(_ value: String) -> String {
        guard let dot = value.firstIndex(of: ".") else { return value }
        let afterDot = value.index(after: dot)
        var end = afterDot
        while end < value.endIndex, value[end].isNumber { end = value.index(after: end) }
        let digits = value[afterDot..<end]
        guard digits.count > 3 else { return value }
        return String(value[..<afterDot]) + digits.prefix(3) + String(value[end...])
    }
}
