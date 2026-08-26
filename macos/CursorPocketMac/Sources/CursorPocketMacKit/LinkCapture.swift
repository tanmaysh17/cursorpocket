import Foundation

/// Validation and file format for browser-link captures. The `.url` body is
/// the same InternetShortcut format the Windows app writes, so links stay
/// double-clickable on both platforms.
public enum LinkCapture {
    public struct ValidatedLink: Equatable {
        public let url: String
        public let host: String
    }

    public static func validate(_ raw: String) -> ValidatedLink? {
        let value = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let components = URLComponents(string: value),
              let scheme = components.scheme?.lowercased(),
              scheme == "http" || scheme == "https",
              let host = components.host, !host.isEmpty else { return nil }
        let displayHost = host.lowercased().hasPrefix("www.") ? String(host.dropFirst(4)) : host
        return ValidatedLink(url: value, host: displayHost)
    }

    public static func internetShortcutBody(for url: String) -> String {
        "[InternetShortcut]\nURL=\(url)\n"
    }

    /// Reads the URL back out of a `.url` file written by either app.
    public static func url(fromInternetShortcut body: String) -> String? {
        for line in body.split(whereSeparator: \.isNewline) {
            let trimmed = line.trimmingCharacters(in: .whitespaces)
            if trimmed.uppercased().hasPrefix("URL=") {
                let value = String(trimmed.dropFirst(4)).trimmingCharacters(in: .whitespaces)
                return value.isEmpty ? nil : value
            }
        }
        return nil
    }
}
