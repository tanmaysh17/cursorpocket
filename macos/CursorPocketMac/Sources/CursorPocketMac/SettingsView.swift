import AppKit
import SwiftUI
import CursorPocketMacKit

struct SettingsView: View {
    @ObservedObject var services: AppServices
    @ObservedObject var updates: UpdateService
    @ObservedObject var loginItems: LoginItemService

    var body: some View {
        Form {
            Section("Capture folder") {
                HStack {
                    Text(services.settingsStore.captureRoot(for: services.settings).path)
                        .font(.system(size: 12, design: .monospaced))
                        .lineLimit(1)
                        .truncationMode(.middle)
                    Spacer()
                    Button("Choose…") { chooseFolder() }
                    if services.settings.captureRootPath != nil {
                        Button("Use default") {
                            services.updateSettings { $0.captureRootPath = nil }
                        }
                    }
                }
                Text("Existing captures are never moved — changing the folder starts a fresh library there and leaves the old one intact.")
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
            }

            Section("Command palette") {
                Toggle("Open with two quick mouse circles", isOn: binding(\.gestureEnabled))
                Text("Draw two small circles with the pointer — anywhere, over any app — to open the command palette. Strict about the circular shape, forgiving about size and speed.")
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
            }

            Section("Cursor companion") {
                Toggle("Show the companion dot near the pointer", isOn: binding(\.companionEnabled))
                Text("A small dot trails the pointer: a hollow green ring when idle, a red ring with a filled square while recording — the shape changes, not just the color. Click it to open the command palette. It never appears in captures.")
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
                Toggle("Open the palette by holding both mouse buttons (700 ms)", isOn: binding(\.chordEnabled))
                Text("Needs Accessibility access; without it the chord quietly stays off. The first button press always reaches the app underneath — only the second is intercepted.")
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
            }

            Section("Global hotkeys") {
                Toggle("Enable global hotkeys", isOn: binding(\.hotkeysEnabled))
                ForEach(HotkeyAction.allCases, id: \.self) { action in
                    HStack {
                        Text(title(for: action))
                        Spacer()
                        Text(HotkeyDefaults.spec(for: action).displayString)
                            .font(.system(size: 12, design: .monospaced))
                            .foregroundStyle(.secondary)
                    }
                }
            }

            Section("Recording") {
                Toggle("Record microphone narration", isOn: binding(\.lastMicrophoneEnabled))
                Toggle("Show camera self-view in recordings", isOn: binding(\.lastCameraEnabled))
                Picker("Self-view shape", selection: binding(\.cameraShape)) {
                    Text("Squircle (1:1)").tag(CameraSelfViewShape.squircle)
                    Text("Rounded (16:9)").tag(CameraSelfViewShape.rounded)
                }
                Text("During a recording, Escape stops and SAVES the take. The self-view appears inside the recorded area and is part of the file.")
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
            }

            Section("Camera effects") {
                // Blur and replace are mutually exclusive; enabling one turns
                // the other off. Both default off.
                Toggle("Blur background", isOn: Binding(
                    get: { services.settings.cameraEffectsBlur },
                    set: { enabled in
                        services.updateSettings {
                            $0.cameraEffectsBlur = enabled
                            if enabled { $0.cameraEffectsReplace = false }
                        }
                    }))
                Toggle("Replace background with brand dark", isOn: Binding(
                    get: { services.settings.cameraEffectsReplace },
                    set: { enabled in
                        services.updateSettings {
                            $0.cameraEffectsReplace = enabled
                            if enabled { $0.cameraEffectsBlur = false }
                        }
                    }))
                HStack {
                    Text("Brightness")
                    Slider(value: binding(\.cameraBrightness), in: CameraEffectSettings.brightnessRange)
                    Button("Reset") { services.updateSettings { $0.cameraBrightness = 0 } }
                        .disabled(services.settings.cameraBrightness == 0)
                }
                HStack {
                    Text("Contrast")
                    Slider(value: binding(\.cameraContrast), in: CameraEffectSettings.contrastRange)
                    Button("Reset") { services.updateSettings { $0.cameraContrast = 1 } }
                        .disabled(services.settings.cameraContrast == 1)
                }
                HStack {
                    Text("Warmth")
                    Slider(value: binding(\.cameraWarmth), in: CameraEffectSettings.warmthRange)
                    Button("Reset") { services.updateSettings { $0.cameraWarmth = 0 } }
                        .disabled(services.settings.cameraWarmth == 0)
                }
                Text("Every effect defaults off — with all of them off the self-view runs the untouched camera preview. If the person can't be separated from the background, blur and replacement quietly turn off while color adjustments keep working; the background is never blurred without a person mask, and a failed effect can never take down a recording.")
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
            }

            Section("Audio notes") {
                Toggle("Clean up audio notes when saving (80 Hz high-pass + peak normalize)", isOn: binding(\.audioCleanupEnabled))
                Text("Cleanup never risks the take: the raw recording is saved to disk first, cleanup runs afterwards, and the file is replaced only when processing succeeds. If anything fails, the raw take is kept as-is.")
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
            }

            Section("Startup") {
                Toggle("Start CursorPocket at login", isOn: binding(\.startAtLogin))
                if let error = loginItems.lastError {
                    Text(error)
                        .font(.system(size: 11))
                        .foregroundStyle(Theme.alert)
                }
                Text("Uses the system login items list (System Settings → General → Login Items). Registration needs the app installed in a stable location such as /Applications.")
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
            }

            Section("Updates") {
                Toggle("Check for updates once a day", isOn: binding(\.updateCheckEnabled))
                HStack {
                    Text(updates.statusMessage.isEmpty
                        ? "CursorPocket \(updates.currentVersion)"
                        : updates.statusMessage)
                        .font(.system(size: 12))
                        .foregroundStyle(.secondary)
                    Spacer()
                    Button("Check now") { updates.checkNow() }
                    if updates.availableRelease != nil {
                        Button("Open release page") { updates.openReleasePage() }
                    }
                }
                Text("One anonymous request to GitHub's releases API — nothing is sent, nothing downloads automatically.")
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
            }

            Section("Privacy") {
                Text("Local-first: no account, cloud, analytics, or AI services. Selected-text capture uses Accessibility access; browser-link capture asks the front browser via Apple events. Both are read-only and save straight to your capture folder.")
                    .font(.system(size: 11))
                    .foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
    }

    private func binding<Value>(_ keyPath: WritableKeyPath<MacAppSettings, Value>) -> Binding<Value> {
        Binding(
            get: { services.settings[keyPath: keyPath] },
            set: { newValue in services.updateSettings { $0[keyPath: keyPath] = newValue } })
    }

    private func title(for action: HotkeyAction) -> String {
        switch action {
        case .screenshot: return "Screenshot"
        case .video: return "Record region"
        case .audioNote: return "Audio note (toggle)"
        case .textCapture: return "Grab selected text"
        case .linkCapture: return "Save browser link"
        case .commandPalette: return "Command palette"
        case .openLibrary: return "Open Library"
        }
    }

    private func chooseFolder() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.canCreateDirectories = true
        panel.prompt = "Use folder"
        if panel.runModal() == .OK, let url = panel.url {
            services.updateSettings { $0.captureRootPath = url.path }
        }
    }
}
