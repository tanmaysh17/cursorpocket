import Foundation

public struct CaptureReservation: Equatable, Sendable {
    public let id: String
    public let kind: CaptureKind
    public let createdAt: Date
    public let absoluteURL: URL
    public let relativePath: String
}

public enum CaptureStoreError: Error, Equatable {
    case emptyText
    case invalidLink
    case missingCaptureFile(String)
    case unsafePath(String)
    case notATextCapture(String)
    case notInIndex(String)
}

/// Reads and writes the exact library layout the Windows app owns:
/// `<root>/yyyy-MM-dd/<category>/HH-mm-ss_<kind>_<shortid><ext>` plus an
/// append-only `captures.jsonl` manifest at the root. Existing captures are
/// never moved or rewritten.
public final class CaptureStore {
    private let lock = NSLock()
    private let now: () -> Date
    private let shortId: () -> String
    private let encoder: JSONEncoder
    private let decoder = JSONDecoder()

    public let rootDirectory: URL
    public var manifestURL: URL { rootDirectory.appendingPathComponent("captures.jsonl") }

    /// A capture finishing is broadcast so the Library can refresh.
    public var captureCompleted: ((CaptureRecord, URL) -> Void)?

    public init(
        rootDirectory: URL,
        now: @escaping () -> Date = Date.init,
        shortId: @escaping () -> String = { String(UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased().prefix(6)) }
    ) {
        self.rootDirectory = rootDirectory.standardizedFileURL
        self.now = now
        self.shortId = shortId
        encoder = JSONEncoder()
        encoder.outputFormatting = [.withoutEscapingSlashes]
        try? FileManager.default.createDirectory(at: self.rootDirectory, withIntermediateDirectories: true)
    }

    // MARK: Reservation and registration

    public func reserve(kind: CaptureKind, suffix: String? = nil) -> CaptureReservation {
        let created = now()
        let short = shortId()
        let id = "\(Self.idStamp(created))-\(short)"
        let directory = rootDirectory
            .appendingPathComponent(Self.dayFolder(created), isDirectory: true)
            .appendingPathComponent(kind.category, isDirectory: true)
        try? FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
        let name = "\(Self.timeStamp(created))_\(kind.storageValue)_\(short)\(suffix ?? kind.fileExtension)"
        let absolute = directory.appendingPathComponent(name)
        let relative = "\(Self.dayFolder(created))/\(kind.category)/\(name)"
        return CaptureReservation(
            id: id, kind: kind, createdAt: created, absoluteURL: absolute, relativePath: relative)
    }

    @discardableResult
    public func registerReservation(
        _ reservation: CaptureReservation,
        preview: String,
        metadata: [String: JSONValue] = [:]
    ) throws -> CaptureRecord {
        guard FileManager.default.fileExists(atPath: reservation.absoluteURL.path) else {
            throw CaptureStoreError.missingCaptureFile(reservation.absoluteURL.path)
        }
        let record = CaptureRecord(
            id: reservation.id,
            kind: reservation.kind.storageValue,
            createdAt: CaptureTimestamp.format(reservation.createdAt),
            relativePath: reservation.relativePath,
            preview: Self.compact(preview),
            metadata: metadata)
        return try append(record)
    }

    @discardableResult
    public func registerExisting(
        kind: CaptureKind,
        at absoluteURL: URL,
        preview: String,
        metadata: [String: JSONValue] = [:]
    ) throws -> CaptureRecord {
        let standardized = absoluteURL.standardizedFileURL
        guard let relative = relativePathInsideRoot(standardized) else {
            throw CaptureStoreError.unsafePath(standardized.path)
        }
        guard FileManager.default.fileExists(atPath: standardized.path) else {
            throw CaptureStoreError.missingCaptureFile(standardized.path)
        }
        let created = now()
        let record = CaptureRecord(
            id: String("\(Self.idStamp(created))-\(UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased())".prefix(22)),
            kind: kind.storageValue,
            createdAt: CaptureTimestamp.format(created),
            relativePath: relative,
            preview: Self.compact(preview),
            metadata: metadata)
        return try append(record)
    }

    // MARK: Text and link captures

    @discardableResult
    public func saveText(_ text: String) throws -> CaptureRecord {
        let value = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !value.isEmpty else { throw CaptureStoreError.emptyText }
        let reservation = reserve(kind: .text)
        try (value + "\n").write(to: reservation.absoluteURL, atomically: true, encoding: .utf8)
        return try registerReservation(reservation, preview: value)
    }

    @discardableResult
    public func saveLink(_ url: String) throws -> CaptureRecord {
        let value = url.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let validated = LinkCapture.validate(value) else { throw CaptureStoreError.invalidLink }
        let reservation = reserve(kind: .link)
        try LinkCapture.internetShortcutBody(for: validated.url)
            .write(to: reservation.absoluteURL, atomically: true, encoding: .utf8)
        return try registerReservation(
            reservation,
            preview: validated.url,
            metadata: ["url": .string(validated.url), "host": .string(validated.host)])
    }

    @discardableResult
    public func importFile(
        kind: CaptureKind,
        from sourceURL: URL,
        preview: String,
        metadata: [String: JSONValue] = [:]
    ) throws -> CaptureRecord {
        guard FileManager.default.fileExists(atPath: sourceURL.path) else {
            throw CaptureStoreError.missingCaptureFile(sourceURL.path)
        }
        let suffix = sourceURL.pathExtension.isEmpty ? kind.fileExtension : "." + sourceURL.pathExtension
        let reservation = reserve(kind: kind, suffix: suffix)
        try FileManager.default.copyItem(at: sourceURL, to: reservation.absoluteURL)
        return try registerReservation(reservation, preview: preview, metadata: metadata)
    }

    /// Rewrites a text capture's content and its manifest line in place —
    /// same id, same path, same position in the index — so the Library shows
    /// the same row with a recompacted preview. Content is staged to a
    /// sibling temp file and swapped atomically, and a manifest failure rolls
    /// the content back, so a fault leaves both files exactly as they were.
    /// Mirrors the Windows `CaptureStore.UpdateTextAsync` guarantees.
    @discardableResult
    public func updateText(record: CaptureRecord, newText: String) throws -> CaptureRecord {
        guard record.captureKind == .text else {
            throw CaptureStoreError.notATextCapture(record.id)
        }
        let value = newText.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !value.isEmpty else { throw CaptureStoreError.emptyText }
        let fileURL = try absoluteURL(for: record)

        lock.lock()
        defer { lock.unlock() }
        guard FileManager.default.fileExists(atPath: fileURL.path) else {
            throw CaptureStoreError.missingCaptureFile(fileURL.path)
        }
        guard let manifestData = try? Data(contentsOf: manifestURL),
              let manifestText = String(data: manifestData, encoding: .utf8) else {
            throw CaptureStoreError.notInIndex(record.id)
        }

        var updated = record
        updated.preview = Self.compact(value)

        var lines = manifestText.split(separator: "\n", omittingEmptySubsequences: true).map(String.init)
        var lineIndex: Int?
        for index in lines.indices {
            guard let lineData = lines[index].data(using: .utf8),
                  let parsed = try? decoder.decode(CaptureRecord.self, from: lineData),
                  parsed.id == record.id else { continue }
            lineIndex = index
            break
        }
        guard let index = lineIndex else { throw CaptureStoreError.notInIndex(record.id) }
        lines[index] = String(decoding: try encoder.encode(updated), as: UTF8.self)

        // `write(atomically: true)` stages to a temp file then renames, so a
        // fault mid-write never leaves a half-written capture or manifest.
        let originalContent = try Data(contentsOf: fileURL)
        try (value + "\n").write(to: fileURL, atomically: true, encoding: .utf8)
        do {
            try (lines.joined(separator: "\n") + "\n")
                .write(to: manifestURL, atomically: true, encoding: .utf8)
        } catch {
            // Restore the content so the file and the index never disagree.
            try? originalContent.write(to: fileURL, options: .atomic)
            throw error
        }
        return updated
    }

    // MARK: Reading

    public func recent(limit: Int = 250) -> [CaptureRecord] {
        guard limit > 0, let data = try? Data(contentsOf: manifestURL),
              let text = String(data: data, encoding: .utf8) else { return [] }
        let lines = text.split(separator: "\n", omittingEmptySubsequences: true)
        var records: [CaptureRecord] = []
        for line in lines.reversed() {
            guard records.count < limit else { break }
            guard let lineData = line.data(using: .utf8),
                  let record = try? decoder.decode(CaptureRecord.self, from: lineData),
                  Self.isSafeRelativePath(record.relativePath) else { continue }
            records.append(record)
        }
        return records
    }

    public func absoluteURL(for record: CaptureRecord) throws -> URL {
        guard Self.isSafeRelativePath(record.relativePath) else {
            throw CaptureStoreError.unsafePath(record.relativePath)
        }
        let url = rootDirectory.appendingPathComponent(record.relativePath).standardizedFileURL
        guard relativePathInsideRoot(url) != nil else {
            throw CaptureStoreError.unsafePath(record.relativePath)
        }
        return url
    }

    public func removeFromIndex(id: String) throws {
        lock.lock()
        defer { lock.unlock() }
        guard let data = try? Data(contentsOf: manifestURL),
              let text = String(data: data, encoding: .utf8) else { return }
        let retained = text.split(separator: "\n", omittingEmptySubsequences: true).filter { line in
            guard let lineData = line.data(using: .utf8),
                  let record = try? decoder.decode(CaptureRecord.self, from: lineData) else { return true }
            return record.id != id
        }
        let body = retained.isEmpty ? "" : retained.joined(separator: "\n") + "\n"
        try body.write(to: manifestURL, atomically: true, encoding: .utf8)
    }

    // MARK: Recovery

    /// Registers finished capture files that never made it into the manifest —
    /// e.g. after a crash between finalize and append. Only the per-day
    /// category folders are scanned, and in-flight `.partial` media is skipped.
    @discardableResult
    public func recoverOrphanedMedia() -> [CaptureRecord] {
        let fileManager = FileManager.default
        guard let days = try? fileManager.contentsOfDirectory(
            at: rootDirectory, includingPropertiesForKeys: nil) else { return [] }

        var candidates: [(url: URL, kind: CaptureKind)] = []
        for day in days where !day.lastPathComponent.hasPrefix(".") {
            for kind in CaptureKind.allCases {
                let category = day.appendingPathComponent(kind.category, isDirectory: true)
                guard let files = try? fileManager.contentsOfDirectory(
                    at: category, includingPropertiesForKeys: [.fileSizeKey]) else { continue }
                for file in files where file.path.hasSuffix(kind.fileExtension) {
                    if file.lastPathComponent.contains(".partial") { continue }
                    let size = (try? file.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0
                    if Int64(size) >= kind.minimumRecoverableBytes {
                        candidates.append((file.standardizedFileURL, kind))
                    }
                }
            }
        }
        guard !candidates.isEmpty else { return [] }

        var indexed = Set<String>()
        for record in recent(limit: .max) {
            if let url = try? absoluteURL(for: record) { indexed.insert(url.path) }
        }

        var recovered: [CaptureRecord] = []
        for candidate in candidates where !indexed.contains(candidate.url.path) {
            if let record = try? registerExisting(
                kind: candidate.kind,
                at: candidate.url,
                preview: "Recovered \(candidate.kind.storageValue)",
                metadata: ["recovered": .bool(true)]) {
                indexed.insert(candidate.url.path)
                recovered.append(record)
            }
        }
        return recovered
    }

    // MARK: Internals

    private func append(_ record: CaptureRecord) throws -> CaptureRecord {
        lock.lock()
        do {
            try FileManager.default.createDirectory(at: rootDirectory, withIntermediateDirectories: true)
            let line = try encoder.encode(record)
            if let handle = FileHandle(forWritingAtPath: manifestURL.path) {
                defer { try? handle.close() }
                try handle.seekToEnd()
                try handle.write(contentsOf: line + Data("\n".utf8))
            } else {
                try (line + Data("\n".utf8)).write(to: manifestURL)
            }
            lock.unlock()
        } catch {
            lock.unlock()
            throw error
        }
        if let absolute = try? absoluteURL(for: record) {
            captureCompleted?(record, absolute)
        }
        return record
    }

    private func relativePathInsideRoot(_ url: URL) -> String? {
        let rootPath = rootDirectory.path.hasSuffix("/") ? rootDirectory.path : rootDirectory.path + "/"
        guard url.path.hasPrefix(rootPath) else { return nil }
        return String(url.path.dropFirst(rootPath.count))
    }

    static func isSafeRelativePath(_ relativePath: String) -> Bool {
        guard !relativePath.trimmingCharacters(in: .whitespaces).isEmpty,
              !relativePath.hasPrefix("/") else { return false }
        let parts = relativePath.replacingOccurrences(of: "\\", with: "/").split(separator: "/")
        return !parts.contains("..")
    }

    /// Manifest previews are single-line and bounded, matching the Windows rule.
    public static func compact(_ value: String, limit: Int = 96) -> String {
        let clean = value.split(whereSeparator: { $0.isWhitespace }).joined(separator: " ")
        guard clean.count > limit else { return clean }
        var head = String(clean.prefix(limit - 1))
        while let last = head.last, last.isWhitespace { head.removeLast() }
        return head + "…"
    }

    static func dayFolder(_ date: Date) -> String {
        stamp(date, format: "yyyy-MM-dd")
    }

    static func timeStamp(_ date: Date) -> String {
        stamp(date, format: "HH-mm-ss")
    }

    static func idStamp(_ date: Date) -> String {
        stamp(date, format: "yyyyMMdd'T'HHmmss")
    }

    private static func stamp(_ date: Date, format: String) -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.calendar = Calendar(identifier: .gregorian)
        formatter.timeZone = .current
        formatter.dateFormat = format
        return formatter.string(from: date)
    }
}
