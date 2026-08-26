import Foundation

/// Capture kinds, mirroring the Windows storage contract exactly: the same
/// storage strings, per-day category folders, extensions, and the minimum
/// plausible sizes used by orphan recovery.
public enum CaptureKind: String, CaseIterable, Codable, Sendable {
    case screenshot
    case video
    case audio
    case text
    case link

    public var storageValue: String { rawValue }

    public static func parseStorageValue(_ value: String) -> CaptureKind {
        CaptureKind(rawValue: value.lowercased()) ?? .text
    }

    /// Folder name under the dated directory.
    public var category: String {
        switch self {
        case .screenshot: return "screenshots"
        case .video: return "videos"
        case .audio: return "audio"
        case .text: return "text"
        case .link: return "links"
        }
    }

    public var fileExtension: String {
        switch self {
        case .screenshot: return ".png"
        case .video: return ".mp4"
        case .audio: return ".wav"
        case .text: return ".txt"
        case .link: return ".url"
        }
    }

    /// A capture smaller than this is a failed write, not a recoverable orphan.
    public var minimumRecoverableBytes: Int64 {
        switch self {
        case .screenshot: return 8
        case .video: return 1024
        case .audio: return 44
        case .text: return 1
        case .link: return 1
        }
    }
}
