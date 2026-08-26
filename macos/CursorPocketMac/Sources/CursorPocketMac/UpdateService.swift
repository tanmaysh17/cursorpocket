import AppKit
import CursorPocketMacKit

/// Checks GitHub's latest-release endpoint at most once a day. Local-first
/// stays true: one anonymous GET, nothing sent, nothing downloaded, no
/// nagging — a newer version only surfaces as a status string and a button
/// the user can press to open the release page in the browser.
final class UpdateService: ObservableObject {
    @Published private(set) var availableRelease: UpdateCheckPlan.Release?
    @Published private(set) var statusMessage = ""

    /// Matches the shipped marketing version; used only when the bundle has
    /// no Info.plist (e.g. `swift run` during development).
    static let fallbackVersion = "0.4.6"
    private static let lastCheckKey = "lastUpdateCheck"
    private static let latestReleaseURL =
        URL(string: "https://api.github.com/repos/tanmaysh17/cursorpocket/releases/latest")!

    private let isEnabled: () -> Bool
    private var checkInFlight = false

    init(isEnabled: @escaping () -> Bool) {
        self.isEnabled = isEnabled
    }

    var currentVersion: String {
        (Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String)
            ?? Self.fallbackVersion
    }

    /// Launch-time entry point: respects the setting and the daily throttle.
    func checkIfDue() {
        guard isEnabled() else { return }
        let lastChecked = UserDefaults.standard.object(forKey: Self.lastCheckKey) as? Date
        guard UpdateCheckPlan.shouldCheck(lastChecked: lastChecked, now: Date()) else { return }
        performCheck()
    }

    /// The Settings button: an explicit request skips the throttle.
    func checkNow() {
        performCheck()
    }

    func openReleasePage() {
        guard let release = availableRelease, let url = URL(string: release.htmlURL) else { return }
        NSWorkspace.shared.open(url)
    }

    private func performCheck() {
        guard !checkInFlight else { return }
        checkInFlight = true
        statusMessage = "Checking for updates…"
        var request = URLRequest(url: Self.latestReleaseURL)
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        let current = currentVersion
        URLSession.shared.dataTask(with: request) { [weak self] data, response, error in
            DispatchQueue.main.async {
                self?.finishCheck(data: data, response: response, error: error, currentVersion: current)
            }
        }.resume()
    }

    private func finishCheck(data: Data?, response: URLResponse?, error: Error?, currentVersion: String) {
        checkInFlight = false
        // A completed attempt — success or not — spends the day's check, so a
        // broken network cannot turn the throttle into a retry loop.
        UserDefaults.standard.set(Date(), forKey: Self.lastCheckKey)
        if let error {
            statusMessage = "Update check failed: \(error.localizedDescription)"
            return
        }
        guard let data,
              let http = response as? HTTPURLResponse,
              http.statusCode == 200 else {
            statusMessage = "Update check failed."
            return
        }
        if let release = UpdateCheckPlan.availableUpdate(inReleaseData: data, currentVersion: currentVersion) {
            availableRelease = release
            statusMessage = "Version \(release.version) available"
        } else {
            availableRelease = nil
            statusMessage = "Up to date (\(currentVersion))"
        }
    }
}
