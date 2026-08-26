import AppKit
import CursorPocketMacKit

/// Asks the frontmost browser for its active tab's URL via Apple events.
/// Safari and the Chromium family cover the realistic set; anything else
/// reports a clear "not a browser" status instead of guessing.
final class LinkCaptureService {
    private let store: () -> CaptureStore

    init(store: @escaping () -> CaptureStore) {
        self.store = store
    }

    enum LinkCaptureError: LocalizedError {
        case noBrowserInFront(appName: String)
        case noPageOpen

        var errorDescription: String? {
            switch self {
            case .noBrowserInFront(let appName):
                return "\(appName) is not a supported browser — bring Safari or Chrome to the front."
            case .noPageOpen:
                return "The front browser window has no web page open."
            }
        }
    }

    private static let safariBundleIDs: Set<String> = [
        "com.apple.Safari", "com.apple.SafariTechnologyPreview",
    ]
    private static let chromiumBundleIDs: Set<String> = [
        "com.google.Chrome", "com.google.Chrome.canary", "com.microsoft.edgemac",
        "com.brave.Browser", "com.vivaldi.Vivaldi", "company.thebrowser.Browser",
    ]

    func captureFrontBrowserLink() throws -> CaptureRecord {
        guard let front = NSWorkspace.shared.frontmostApplication,
              let bundleID = front.bundleIdentifier else {
            throw LinkCaptureError.noBrowserInFront(appName: "The front app")
        }
        let script: String
        if Self.safariBundleIDs.contains(bundleID) {
            script = "tell application id \"\(bundleID)\" to return URL of front document"
        } else if Self.chromiumBundleIDs.contains(bundleID) {
            script = "tell application id \"\(bundleID)\" to return URL of active tab of front window"
        } else {
            throw LinkCaptureError.noBrowserInFront(appName: front.localizedName ?? "The front app")
        }

        var errorInfo: NSDictionary?
        let result = NSAppleScript(source: script)?.executeAndReturnError(&errorInfo)
        guard let url = result?.stringValue, LinkCapture.validate(url) != nil else {
            throw LinkCaptureError.noPageOpen
        }
        return try store().saveLink(url)
    }
}
