import Foundation

public struct MacAppSettings: Codable, Equatable, Sendable {
    public var captureRootPath: String?
    public var hotkeysEnabled: Bool
    public var gestureEnabled: Bool
    public var companionEnabled: Bool
    public var chordEnabled: Bool
    public var lastMicrophoneEnabled: Bool
    public var lastCameraEnabled: Bool
    public var cameraShape: CameraSelfViewShape
    public var palettePlacement: PalettePlacement
    /// Off by default: cleanup must be a choice, never a surprise on a take.
    public var audioCleanupEnabled: Bool
    public var updateCheckEnabled: Bool
    public var startAtLogin: Bool
    // Camera effects: every effect defaults OFF (Windows-parity invariant).
    public var cameraEffectsBlur: Bool
    public var cameraEffectsReplace: Bool
    public var cameraBrightness: Double
    public var cameraContrast: Double
    public var cameraWarmth: Double

    /// The effect settings handed to the self-view; always clamped, and
    /// blur wins if both background effects somehow persist.
    public var cameraEffects: CameraEffectSettings {
        CameraEffectSettings(
            backgroundBlurEnabled: cameraEffectsBlur,
            backgroundReplaceEnabled: cameraEffectsReplace,
            brightness: cameraBrightness,
            contrast: cameraContrast,
            warmth: cameraWarmth
        ).clamped()
    }

    public init(
        captureRootPath: String? = nil,
        hotkeysEnabled: Bool = true,
        gestureEnabled: Bool = true,
        companionEnabled: Bool = true,
        chordEnabled: Bool = true,
        lastMicrophoneEnabled: Bool = false,
        lastCameraEnabled: Bool = false,
        cameraShape: CameraSelfViewShape = .squircle,
        palettePlacement: PalettePlacement = PalettePlacement(),
        audioCleanupEnabled: Bool = false,
        updateCheckEnabled: Bool = true,
        startAtLogin: Bool = false,
        cameraEffectsBlur: Bool = false,
        cameraEffectsReplace: Bool = false,
        cameraBrightness: Double = 0,
        cameraContrast: Double = 1,
        cameraWarmth: Double = 0
    ) {
        self.captureRootPath = captureRootPath
        self.hotkeysEnabled = hotkeysEnabled
        self.gestureEnabled = gestureEnabled
        self.companionEnabled = companionEnabled
        self.chordEnabled = chordEnabled
        self.lastMicrophoneEnabled = lastMicrophoneEnabled
        self.lastCameraEnabled = lastCameraEnabled
        self.cameraShape = cameraShape
        self.palettePlacement = palettePlacement
        self.audioCleanupEnabled = audioCleanupEnabled
        self.updateCheckEnabled = updateCheckEnabled
        self.startAtLogin = startAtLogin
        self.cameraEffectsBlur = cameraEffectsBlur
        self.cameraEffectsReplace = cameraEffectsReplace
        self.cameraBrightness = cameraBrightness
        self.cameraContrast = cameraContrast
        self.cameraWarmth = cameraWarmth
    }

    public init(from decoder: Decoder) throws {
        let container = try decoder.container(keyedBy: CodingKeys.self)
        captureRootPath = try container.decodeIfPresent(String.self, forKey: .captureRootPath)
        hotkeysEnabled = try container.decodeIfPresent(Bool.self, forKey: .hotkeysEnabled) ?? true
        gestureEnabled = try container.decodeIfPresent(Bool.self, forKey: .gestureEnabled) ?? true
        companionEnabled = try container.decodeIfPresent(Bool.self, forKey: .companionEnabled) ?? true
        chordEnabled = try container.decodeIfPresent(Bool.self, forKey: .chordEnabled) ?? true
        lastMicrophoneEnabled = try container.decodeIfPresent(Bool.self, forKey: .lastMicrophoneEnabled) ?? false
        lastCameraEnabled = try container.decodeIfPresent(Bool.self, forKey: .lastCameraEnabled) ?? false
        cameraShape = try container.decodeIfPresent(CameraSelfViewShape.self, forKey: .cameraShape) ?? .squircle
        palettePlacement = try container.decodeIfPresent(PalettePlacement.self, forKey: .palettePlacement) ?? PalettePlacement()
        audioCleanupEnabled = try container.decodeIfPresent(Bool.self, forKey: .audioCleanupEnabled) ?? false
        updateCheckEnabled = try container.decodeIfPresent(Bool.self, forKey: .updateCheckEnabled) ?? true
        startAtLogin = try container.decodeIfPresent(Bool.self, forKey: .startAtLogin) ?? false
        cameraEffectsBlur = try container.decodeIfPresent(Bool.self, forKey: .cameraEffectsBlur) ?? false
        cameraEffectsReplace = try container.decodeIfPresent(Bool.self, forKey: .cameraEffectsReplace) ?? false
        cameraBrightness = try container.decodeIfPresent(Double.self, forKey: .cameraBrightness) ?? 0
        cameraContrast = try container.decodeIfPresent(Double.self, forKey: .cameraContrast) ?? 1
        cameraWarmth = try container.decodeIfPresent(Double.self, forKey: .cameraWarmth) ?? 0
    }
}

/// Loads, normalizes, and saves `settings.json`. `normalize` is the single
/// place persisted values are clamped or repaired, mirroring the Windows
/// `SettingsStore.Normalize` rule.
public final class MacSettingsStore {
    public let settingsURL: URL
    private let defaultCaptureRoot: URL

    public init(settingsURL: URL, defaultCaptureRoot: URL) {
        self.settingsURL = settingsURL
        self.defaultCaptureRoot = defaultCaptureRoot
    }

    public static func defaultSettingsURL(applicationSupport: URL) -> URL {
        applicationSupport
            .appendingPathComponent("CursorPocket", isDirectory: true)
            .appendingPathComponent("settings.json")
    }

    public func load() -> MacAppSettings {
        guard let data = try? Data(contentsOf: settingsURL),
              let settings = try? JSONDecoder().decode(MacAppSettings.self, from: data) else {
            return normalize(MacAppSettings())
        }
        return normalize(settings)
    }

    public func save(_ settings: MacAppSettings) throws {
        let normalized = normalize(settings)
        try FileManager.default.createDirectory(
            at: settingsURL.deletingLastPathComponent(), withIntermediateDirectories: true)
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        try encoder.encode(normalized).write(to: settingsURL, options: .atomic)
    }

    public func normalize(_ settings: MacAppSettings) -> MacAppSettings {
        var result = settings
        result.palettePlacement = settings.palettePlacement.normalized()
        if let path = settings.captureRootPath?.trimmingCharacters(in: .whitespacesAndNewlines), !path.isEmpty {
            result.captureRootPath = path
        } else {
            result.captureRootPath = nil
        }
        // Camera effect values are clamped in one place (CameraEffectSettings.clamped()):
        // ranges repaired, corrupt values back to neutral, blur wins over replace.
        let effects = settings.cameraEffects
        result.cameraEffectsBlur = effects.backgroundBlurEnabled
        result.cameraEffectsReplace = effects.backgroundReplaceEnabled
        result.cameraBrightness = effects.brightness
        result.cameraContrast = effects.contrast
        result.cameraWarmth = effects.warmth
        return result
    }

    public func captureRoot(for settings: MacAppSettings) -> URL {
        if let path = settings.captureRootPath, !path.isEmpty {
            return URL(fileURLWithPath: path, isDirectory: true)
        }
        return defaultCaptureRoot
    }
}
