import Foundation
import ServiceManagement

/// Start-at-login via the system login-items list (`SMAppService.mainApp`,
/// macOS 13+). Registration can legitimately fail — most commonly when the
/// app is not running from a stable installed location like /Applications —
/// so failure surfaces as a string for Settings to show, never a crash.
final class LoginItemService: ObservableObject {
    @Published private(set) var lastError: String?

    /// What the system actually has on file, which can drift from the saved
    /// setting (the user can toggle it in System Settings too).
    var isRegistered: Bool {
        SMAppService.mainApp.status == .enabled
    }

    /// Applies the toggle. Returns false and sets `lastError` on failure,
    /// leaving the system state as it was.
    @discardableResult
    func setEnabled(_ enabled: Bool) -> Bool {
        do {
            if enabled {
                try SMAppService.mainApp.register()
            } else {
                // Already-unregistered is success, not an error to surface.
                if SMAppService.mainApp.status != .notRegistered {
                    try SMAppService.mainApp.unregister()
                }
            }
            lastError = nil
            return true
        } catch {
            lastError = enabled
                ? "Could not enable start at login — install CursorPocket in /Applications and try again. (\(error.localizedDescription))"
                : "Could not disable start at login: \(error.localizedDescription)"
            return false
        }
    }
}
