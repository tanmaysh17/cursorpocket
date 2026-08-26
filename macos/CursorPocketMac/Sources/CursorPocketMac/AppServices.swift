import AppKit
import CursorPocketMacKit
import SwiftUI

/// Composition root: builds every runtime singleton and orchestrates the
/// capture flows. Changing the capture folder tears down and rebuilds the
/// `CaptureStore`; every service reaches the store through a closure so a
/// rebuild is atomic for all of them. Lives on the main thread by
/// construction: every entry point is UI, hotkey (dispatched to main), or a
/// completion already marshaled to main.
final class AppServices: ObservableObject {
    static let shared = AppServices()

    @Published private(set) var records: [CaptureRecord] = []
    @Published var statusMessage = "Ready"
    @Published var settings: MacAppSettings
    @Published private(set) var recordingStartedAt: Date?

    let settingsStore: MacSettingsStore
    private(set) var captureStore: CaptureStore

    let hotkeys = HotkeyService()
    private(set) lazy var screenshots = ScreenshotService(store: { [weak self] in self!.captureStore })
    private(set) lazy var audioNotes = AudioNoteService(
        store: { [weak self] in self!.captureStore },
        cleanupEnabled: { [weak self] in self?.settings.audioCleanupEnabled ?? false })
    private(set) lazy var recorder = RecordingController(store: { [weak self] in self!.captureStore })
    private(set) lazy var textCapture = TextCaptureService(store: { [weak self] in self!.captureStore })
    private(set) lazy var linkCapture = LinkCaptureService(store: { [weak self] in self!.captureStore })
    private(set) lazy var annotationEditor = AnnotationEditorController(store: { [weak self] in self!.captureStore })
    private(set) lazy var ocr = OCRService(store: { [weak self] in self!.captureStore })
    private(set) lazy var updates = UpdateService(
        isEnabled: { [weak self] in self?.settings.updateCheckEnabled ?? false })
    let loginItems = LoginItemService()

    let palette = CommandPaletteController()
    private let gesture = GestureService()
    private let regionSelector = RegionSelectorController()
    private let preflight = RecordingPreflightController()
    private let windowPicker = WindowPickerController()
    private let hud = RecordingHUDController()
    private let selfView = CameraSelfViewController()
    private let companion = CursorCompanionController()
    private let receipts = ReceiptController()
    private let chord = ChordService()
    private let pins = PinnedCaptureController()
    private var escapeHotkeyID: UInt32 = 0
    private var actionHotkeyIDs: [UInt32] = []

    private init() {
        let fileManager = FileManager.default
        let documents = fileManager.urls(for: .documentDirectory, in: .userDomainMask)[0]
        let applicationSupport = fileManager.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        let store = MacSettingsStore(
            settingsURL: MacSettingsStore.defaultSettingsURL(applicationSupport: applicationSupport),
            defaultCaptureRoot: ScreenshotCapture.captureFolder(inDocuments: documents))
        settingsStore = store
        let loaded = store.load()
        settings = loaded
        captureStore = CaptureStore(rootDirectory: store.captureRoot(for: loaded))
        wireStore()
        configurePalette()
        registerGlobalHotkeys()
        // Two quick pointer circles open the palette, same as Windows.
        gesture.onGesture = { [weak self] in self?.palette.show() }
        applyGestureSetting()
        companion.onClick = { [weak self] in self?.palette.toggle() }
        applyCompanionSetting()
        // Both mouse buttons held 700 ms opens the palette, same as Windows.
        chord.onChord = { [weak self] in self?.palette.show() }
        applyChordSetting()
        receipts.onAction = { [weak self] action, record in
            guard let self else { return }
            switch action {
            case .open: self.open(record)
            case .reveal: self.reveal(record)
            case .annotate: self.annotate(record)
            case .dismiss: break // handled inside the controller
            }
        }
        recorder.onFinished = { [weak self] result in self?.recordingFinished(result) }
        recorder.excludedWindowNumbers = { [weak self] in
            // HUD, palette, companion, and receipts stay out of recordings;
            // the camera self-view and pinned captures must NOT be excluded.
            [
                self?.hud.windowNumber, self?.palette.windowNumber,
                self?.companion.windowNumber, self?.receipts.windowNumber,
            ].compactMap { $0 }
        }
        captureStore.recoverOrphanedMedia()
        refreshLibrary()
        // Daily update check: the closure gate means this is a no-op when
        // the setting is off, and the throttle lives in UpdateCheckPlan.
        updates.checkIfDue()
    }

    // MARK: Settings

    func updateSettings(_ transform: (inout MacAppSettings) -> Void) {
        var updated = settings
        transform(&updated)
        updated = settingsStore.normalize(updated)
        let rootChanged = settingsStore.captureRoot(for: updated) != settingsStore.captureRoot(for: settings)
        let loginChanged = updated.startAtLogin != settings.startAtLogin
        settings = updated
        try? settingsStore.save(updated)
        if rootChanged {
            captureStore = CaptureStore(rootDirectory: settingsStore.captureRoot(for: updated))
            wireStore()
            captureStore.recoverOrphanedMedia()
            refreshLibrary()
        }
        if updated.hotkeysEnabled != !actionHotkeyIDs.isEmpty {
            registerGlobalHotkeys()
        }
        applyGestureSetting()
        applyCompanionSetting()
        applyChordSetting()
        if loginChanged, !loginItems.setEnabled(updated.startAtLogin) {
            statusMessage = loginItems.lastError ?? "Could not update the login item."
        }
    }

    private func applyGestureSetting() {
        if settings.gestureEnabled {
            gesture.start()
        } else {
            gesture.stop()
        }
    }

    private func applyCompanionSetting() {
        if settings.companionEnabled {
            companion.start()
        } else {
            companion.stop()
        }
    }

    private func applyChordSetting() {
        if settings.chordEnabled {
            chord.start()
        } else {
            chord.stop()
        }
    }

    private func wireStore() {
        captureStore.captureCompleted = { [weak self] record, _ in
            DispatchQueue.main.async {
                self?.refreshLibrary()
                self?.statusMessage = "Saved \(record.preview)"
                self?.receipts.show(record)
            }
        }
    }

    func refreshLibrary() {
        records = captureStore.recent(limit: 500)
    }

    // MARK: Global hotkeys

    private func registerGlobalHotkeys() {
        for id in actionHotkeyIDs { hotkeys.unregister(id) }
        actionHotkeyIDs = []
        guard settings.hotkeysEnabled else { return }
        let bindings: [(HotkeyAction, () -> Void)] = [
            (.screenshot, { [weak self] in self?.perform(.screenshot) }),
            (.video, { [weak self] in self?.perform(.video) }),
            (.audioNote, { [weak self] in self?.perform(.audioNote) }),
            (.textCapture, { [weak self] in self?.perform(.textCapture) }),
            (.linkCapture, { [weak self] in self?.perform(.linkCapture) }),
            (.openLibrary, { [weak self] in self?.perform(.openLibrary) }),
            (.commandPalette, { [weak self] in self?.palette.toggle() }),
        ]
        for (action, handler) in bindings {
            actionHotkeyIDs.append(hotkeys.register(HotkeyDefaults.spec(for: action), handler: handler))
        }
    }

    private func configurePalette() {
        palette.placementProvider = { [weak self] in self?.settings.palettePlacement ?? PalettePlacement() }
        palette.placementChanged = { [weak self] placement in
            self?.updateSettings { $0.palettePlacement = placement }
        }
        palette.onCommand = { [weak self] command in self?.perform(command) }
    }

    // MARK: Commands

    func perform(_ command: PaletteCommand) {
        switch command {
        case .screenshot:
            captureScreenshot(mode: .interactive)
        case .video:
            beginRegionRecording()
        case .audioNote:
            toggleAudioNote()
        case .textCapture:
            do {
                _ = try textCapture.captureSelectedText()
            } catch {
                statusMessage = error.localizedDescription
                if case TextCaptureService.TextCaptureError.accessibilityDenied = error {
                    TextCaptureService.requestAccessibilityIfNeeded()
                }
            }
        case .linkCapture:
            do {
                _ = try linkCapture.captureFrontBrowserLink()
            } catch {
                statusMessage = error.localizedDescription
            }
        case .openLibrary:
            NSApp.activate(ignoringOtherApps: true)
            for window in NSApp.windows where !(window is NSPanel) {
                window.makeKeyAndOrderFront(nil)
            }
        }
    }

    func captureScreenshot(mode: ScreenshotCapture.Mode) {
        screenshots.capture(mode: mode) { [weak self] result in
            switch result {
            case .success: break // captureCompleted already refreshed.
            case .failure(let error): self?.statusMessage = error.localizedDescription
            }
        }
    }

    func toggleAudioNote() {
        if audioNotes.isRecording {
            _ = audioNotes.stop()
        } else {
            do {
                try audioNotes.start()
                statusMessage = "Audio note recording — trigger again to stop and save"
            } catch {
                statusMessage = error.localizedDescription
            }
        }
    }

    // MARK: Recording

    func beginDisplayRecording() {
        guard !recorder.isRecording else { return }
        // Resolved from the pointer NOW, at command time — by Start-press the
        // pointer is over the preflight panel, possibly on another display.
        let pointer = NSEvent.mouseLocation
        let displays = NSScreen.screens.compactMap { screen -> (id: UInt32, frame: CGRect)? in
            guard let id = CoordinateSpaces.displayID(for: screen) else { return nil }
            return (id: id, frame: screen.frame)
        }
        guard let displayID = DisplayResolver.displayUnderPointer(pointer, displays: displays) else {
            statusMessage = "No display found to record."
            return
        }
        let ordinal = (displays.firstIndex { $0.id == displayID }).map { $0 + 1 } ?? 1
        let summary = displays.count > 1
            ? "Record display \(ordinal) of \(displays.count) (under the pointer)"
            : "Record the display"
        runPreflight(summary: summary, cameraNote: nil) { [weak self] choices in
            self?.startRecording(options: RecordingOptions(
                source: .display(displayID),
                microphoneEnabled: choices.microphoneEnabled,
                cameraEnabled: choices.cameraEnabled,
                cameraShape: choices.cameraShape))
        }
    }

    func beginRegionRecording() {
        guard !recorder.isRecording else { return }
        regionSelector.begin { [weak self] selection in
            guard let self, let selection else { return }
            guard let screen = CoordinateSpaces.screen(forDisplayID: selection.displayID) else { return }
            // SCStreamConfiguration.sourceRect is display-relative, top-left.
            let displayCG = CoordinateSpaces.cgRect(fromCocoa: screen.frame)
            let relative = CGRect(
                x: selection.rectCG.origin.x - displayCG.origin.x,
                y: selection.rectCG.origin.y - displayCG.origin.y,
                width: selection.rectCG.width,
                height: selection.rectCG.height)
            let summary = "Record a \(Int(selection.rectCG.width))×\(Int(selection.rectCG.height)) region"
            self.runPreflight(summary: summary, cameraNote: nil) { [weak self] choices in
                self?.startRecording(
                    options: RecordingOptions(
                        source: .region(displayID: selection.displayID, rect: relative),
                        microphoneEnabled: choices.microphoneEnabled,
                        cameraEnabled: choices.cameraEnabled,
                        cameraShape: choices.cameraShape),
                    recordedRectCG: selection.rectCG)
            }
        }
    }

    func beginWindowRecording() {
        guard !recorder.isRecording else { return }
        windowPicker.present { [weak self] window in
            guard let self, let window else { return }
            self.runPreflight(
                summary: "Record “\(window.title)” (\(window.appName))",
                cameraNote: "A window recording captures the window's own pixels, so the self-view stays visible on screen but cannot appear in the file."
            ) { [weak self] choices in
                self?.startRecording(
                    options: RecordingOptions(
                        source: .window(windowID: window.id),
                        microphoneEnabled: choices.microphoneEnabled,
                        cameraEnabled: choices.cameraEnabled,
                        cameraShape: choices.cameraShape),
                    recordedRectCG: window.frame)
            }
        }
    }

    /// Shows the preflight seeded from settings; a Start writes the choices
    /// back so the next preflight opens the same way.
    private func runPreflight(
        summary: String,
        cameraNote: String?,
        onStart: @escaping (PreflightChoices) -> Void
    ) {
        preflight.present(
            summary: summary,
            cameraNote: cameraNote,
            initial: PreflightChoices(
                microphoneEnabled: settings.lastMicrophoneEnabled,
                cameraEnabled: settings.lastCameraEnabled,
                cameraShape: settings.cameraShape)
        ) { [weak self] choices in
            guard let self, let choices else { return }
            self.updateSettings {
                $0.lastMicrophoneEnabled = choices.microphoneEnabled
                $0.lastCameraEnabled = choices.cameraEnabled
                $0.cameraShape = choices.cameraShape
            }
            onStart(choices)
        }
    }

    private func startRecording(options: RecordingOptions, recordedRectCG: CGRect? = nil) {
        Task {
            do {
                try await recorder.start(options: options)
                let startedAt = Date()
                recordingStartedAt = startedAt
                companion.setRecording(true)
                statusMessage = "Recording — Escape stops and SAVES"
                hud.show(startedAt: startedAt) { [weak self] in self?.stopRecording() }
                if options.cameraEnabled {
                    let rect: CGRect
                    if let recordedRectCG {
                        rect = recordedRectCG
                    } else if case .display(let displayID) = options.source,
                              let screen = CoordinateSpaces.screen(forDisplayID: displayID) {
                        rect = CoordinateSpaces.cgRect(fromCocoa: screen.frame)
                    } else {
                        rect = .zero
                    }
                    if rect != .zero {
                        selfView.show(
                            recordedRectCG: rect,
                            shape: options.cameraShape,
                            effects: settings.cameraEffects)
                    }
                }
                // Escape stops AND saves — registered only while recording, so
                // it never steals Escape from other apps outside a take.
                escapeHotkeyID = hotkeys.register(
                    HotkeySpec(keyCode: HotkeyDefaults.keyEscape, modifiers: [], keyLabel: "Esc")
                ) { [weak self] in self?.stopRecording() }
            } catch {
                statusMessage = "Could not start recording: \(error.localizedDescription)"
            }
        }
    }

    func stopRecording() {
        guard recorder.isRecording else { return }
        recorder.stop()
    }

    private func recordingFinished(_ result: Result<CaptureRecord, Error>) {
        hud.hide()
        // Release the camera the moment recording stops, or the next preview
        // finds the device busy.
        selfView.hide()
        if escapeHotkeyID != 0 {
            hotkeys.unregister(escapeHotkeyID)
            escapeHotkeyID = 0
        }
        recordingStartedAt = nil
        companion.setRecording(false)
        switch result {
        case .success: break
        case .failure(let error): statusMessage = error.localizedDescription
        }
    }

    // MARK: Library actions

    func open(_ record: CaptureRecord) {
        guard let url = try? captureStore.absoluteURL(for: record) else { return }
        if record.captureKind == .link,
           let body = try? String(contentsOf: url, encoding: .utf8),
           let link = LinkCapture.url(fromInternetShortcut: body),
           let target = URL(string: link) {
            NSWorkspace.shared.open(target)
            return
        }
        NSWorkspace.shared.open(url)
    }

    func reveal(_ record: CaptureRecord) {
        guard let url = try? captureStore.absoluteURL(for: record) else { return }
        NSWorkspace.shared.activateFileViewerSelecting([url])
    }

    func annotate(_ record: CaptureRecord) {
        annotationEditor.open(record: record)
    }

    /// Pins are created only by explicit action and are never restored after
    /// a restart. The pin panel is deliberately NOT capture-excluded and
    /// registers no Escape handling of any kind.
    func pin(_ record: CaptureRecord) {
        guard record.captureKind == .screenshot,
              let url = try? captureStore.absoluteURL(for: record) else { return }
        pins.pin(imageURL: url)
    }

    /// Saves the recognized text as a new text capture — never the clipboard.
    func recognizeText(_ record: CaptureRecord) {
        statusMessage = "Recognizing text…"
        ocr.recognizeText(in: record) { [weak self] result in
            if case .failure(let error) = result {
                self?.statusMessage = error.localizedDescription
            }
        }
    }

    /// Deletion goes to the Trash, never a hard delete.
    func moveToTrash(_ record: CaptureRecord) {
        guard let url = try? captureStore.absoluteURL(for: record) else { return }
        do {
            try FileManager.default.trashItem(at: url, resultingItemURL: nil)
            try captureStore.removeFromIndex(id: record.id)
            refreshLibrary()
            statusMessage = "Moved to Trash"
        } catch {
            statusMessage = "Could not delete: \(error.localizedDescription)"
        }
    }
}
