import AppKit
import ApplicationServices
import CursorPocketMacKit

/// Reads the focused application's selected text through the Accessibility
/// API. Deliberately never touches the clipboard: synthesizing ⌘C would both
/// clobber the user's pasteboard and put text there unasked.
final class TextCaptureService {
    private let store: () -> CaptureStore

    init(store: @escaping () -> CaptureStore) {
        self.store = store
    }

    enum TextCaptureError: LocalizedError {
        case accessibilityDenied
        case nothingSelected

        var errorDescription: String? {
            switch self {
            case .accessibilityDenied:
                return "Grant CursorPocket Accessibility access in System Settings to grab selected text."
            case .nothingSelected:
                return "Select some text in the active app first."
            }
        }
    }

    func captureSelectedText() throws -> CaptureRecord {
        guard AXIsProcessTrusted() else { throw TextCaptureError.accessibilityDenied }
        guard let text = Self.selectedText(), !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw TextCaptureError.nothingSelected
        }
        return try store().saveText(text)
    }

    static func requestAccessibilityIfNeeded() {
        let options = ["AXTrustedCheckOptionPrompt": true] as CFDictionary
        AXIsProcessTrustedWithOptions(options)
    }

    private static func selectedText() -> String? {
        let systemWide = AXUIElementCreateSystemWide()
        var focused: CFTypeRef?
        guard AXUIElementCopyAttributeValue(
            systemWide, kAXFocusedUIElementAttribute as CFString, &focused) == .success,
            let focusedRef = focused else { return nil }
        let element = focusedRef as! AXUIElement

        var selected: CFTypeRef?
        if AXUIElementCopyAttributeValue(
            element, kAXSelectedTextAttribute as CFString, &selected) == .success,
            let value = selected as? String, !value.isEmpty {
            return value
        }
        return nil
    }
}
