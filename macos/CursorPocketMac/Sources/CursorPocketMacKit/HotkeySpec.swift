import Foundation

public struct HotkeyModifiers: OptionSet, Equatable, Codable, Sendable {
    public let rawValue: Int
    public init(rawValue: Int) { self.rawValue = rawValue }

    public static let control = HotkeyModifiers(rawValue: 1 << 0)
    public static let option = HotkeyModifiers(rawValue: 1 << 1)
    public static let shift = HotkeyModifiers(rawValue: 1 << 2)
    public static let command = HotkeyModifiers(rawValue: 1 << 3)

    /// Carbon `RegisterEventHotKey` modifier mask.
    public var carbonFlags: UInt32 {
        var flags: UInt32 = 0
        if contains(.command) { flags |= 0x0100 } // cmdKey
        if contains(.shift) { flags |= 0x0200 }   // shiftKey
        if contains(.option) { flags |= 0x0800 }  // optionKey
        if contains(.control) { flags |= 0x1000 } // controlKey
        return flags
    }

    public var displayString: String {
        var text = ""
        if contains(.control) { text += "⌃" }
        if contains(.option) { text += "⌥" }
        if contains(.shift) { text += "⇧" }
        if contains(.command) { text += "⌘" }
        return text
    }
}

public struct HotkeySpec: Equatable, Codable, Sendable {
    public var keyCode: UInt32
    public var modifiers: HotkeyModifiers
    public var keyLabel: String

    public init(keyCode: UInt32, modifiers: HotkeyModifiers, keyLabel: String) {
        self.keyCode = keyCode
        self.modifiers = modifiers
        self.keyLabel = keyLabel
    }

    public var displayString: String { modifiers.displayString + keyLabel }
}

public enum HotkeyAction: String, CaseIterable, Codable, Sendable {
    case screenshot
    case video
    case audioNote
    case textCapture
    case linkCapture
    case commandPalette
    case openLibrary
}

/// Default global hotkeys. Every default carries ⌃⌥ — a bare global key would
/// steal ordinary typing from every application, which is only acceptable for
/// keys registered while a surface that owns the user's attention is visible.
public enum HotkeyDefaults {
    // Carbon virtual key codes (kVK_ANSI_*): fixed hardware positions.
    static let keyS: UInt32 = 1
    static let keyV: UInt32 = 9
    static let keyA: UInt32 = 0
    static let keyT: UInt32 = 17
    static let keyL: UInt32 = 37
    static let keyO: UInt32 = 31
    static let keySpace: UInt32 = 49
    public static let keyEscape: UInt32 = 53

    public static func spec(for action: HotkeyAction) -> HotkeySpec {
        let controlOption: HotkeyModifiers = [.control, .option]
        switch action {
        case .screenshot: return HotkeySpec(keyCode: keyS, modifiers: controlOption, keyLabel: "S")
        case .video: return HotkeySpec(keyCode: keyV, modifiers: controlOption, keyLabel: "V")
        case .audioNote: return HotkeySpec(keyCode: keyA, modifiers: controlOption, keyLabel: "A")
        case .textCapture: return HotkeySpec(keyCode: keyT, modifiers: controlOption, keyLabel: "T")
        case .linkCapture: return HotkeySpec(keyCode: keyL, modifiers: controlOption, keyLabel: "L")
        case .commandPalette: return HotkeySpec(keyCode: keySpace, modifiers: controlOption, keyLabel: "Space")
        case .openLibrary: return HotkeySpec(keyCode: keyO, modifiers: controlOption, keyLabel: "O")
        }
    }
}
