import Foundation

public enum LibraryFilter: String, CaseIterable, Equatable, Sendable {
    case all
    case screenshot
    case video
    case audio
    case text
    case link

    public var title: String {
        switch self {
        case .all: return "All"
        case .screenshot: return "Screenshots"
        case .video: return "Videos"
        case .audio: return "Audio"
        case .text: return "Text"
        case .link: return "Links"
        }
    }

    public func matches(_ record: CaptureRecord) -> Bool {
        switch self {
        case .all: return true
        default: return record.captureKind.storageValue == rawValue
        }
    }
}

public struct LibraryDayGroup: Equatable, Identifiable {
    public let dayKey: String
    public let label: String
    public let records: [CaptureRecord]
    public var id: String { dayKey }
}

public enum LibraryModel {
    public static func filter(
        _ records: [CaptureRecord], by filter: LibraryFilter, search: String
    ) -> [CaptureRecord] {
        let query = search.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        return records.filter { record in
            guard filter.matches(record) else { return false }
            guard !query.isEmpty else { return true }
            return record.preview.lowercased().contains(query)
                || record.relativePath.lowercased().contains(query)
        }
    }

    /// Groups newest-first records into day sections. `today` is injected so
    /// the labels are testable.
    public static func groupByDay(_ records: [CaptureRecord], today: Date, timeZone: TimeZone = .current) -> [LibraryDayGroup] {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = timeZone
        var groups: [LibraryDayGroup] = []
        var current: (key: String, records: [CaptureRecord])?

        for record in records {
            let key = dayKey(record.created, timeZone: timeZone)
            if current?.key == key {
                current?.records.append(record)
            } else {
                if let finished = current {
                    groups.append(makeGroup(finished, today: today, calendar: calendar, timeZone: timeZone))
                }
                current = (key, [record])
            }
        }
        if let finished = current {
            groups.append(makeGroup(finished, today: today, calendar: calendar, timeZone: timeZone))
        }
        return groups
    }

    public static func dayLabel(for date: Date, today: Date, timeZone: TimeZone = .current) -> String {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = timeZone
        if calendar.isDate(date, inSameDayAs: today) { return "Today" }
        if let yesterday = calendar.date(byAdding: .day, value: -1, to: today),
           calendar.isDate(date, inSameDayAs: yesterday) { return "Yesterday" }
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = timeZone
        formatter.dateFormat = "EEEE, MMM d"
        return formatter.string(from: date)
    }

    public static func formatFileSize(_ bytes: Int64) -> String {
        guard bytes >= 0 else { return "0 B" }
        let units = ["B", "KB", "MB", "GB"]
        var value = Double(bytes)
        var unit = 0
        while value >= 1024, unit < units.count - 1 {
            value /= 1024
            unit += 1
        }
        return unit == 0 ? "\(Int(value)) \(units[unit])" : String(format: "%.1f %@", value, units[unit])
    }

    private static func dayKey(_ date: Date, timeZone: TimeZone) -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = timeZone
        formatter.dateFormat = "yyyy-MM-dd"
        return formatter.string(from: date)
    }

    private static func makeGroup(
        _ pending: (key: String, records: [CaptureRecord]),
        today: Date,
        calendar: Calendar,
        timeZone: TimeZone
    ) -> LibraryDayGroup {
        let label = pending.records.first.map { dayLabel(for: $0.created, today: today, timeZone: timeZone) } ?? pending.key
        return LibraryDayGroup(dayKey: pending.key, label: label, records: pending.records)
    }
}
