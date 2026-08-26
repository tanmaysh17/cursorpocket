import AppKit
import SwiftUI
import CursorPocketMacKit

/// Editor session state. All decisions defer to the Kit: two-stage Escape,
/// save-target (marks overwrite; a crop writes a new capture), history.
final class AnnotationEditorModel: ObservableObject {
    @Published var marks: [AnnotationMark] = []
    @Published var tool: AnnotationTool = .select
    @Published var colorIndex = 0
    @Published var cropArmed = false
    @Published var cropRect: CGRect?
    @Published var pendingMark: AnnotationMark?
    @Published var pendingCrop: CGRect?
    @Published var selectedMarkID: UUID?
    @Published var status = "Pick a tool, or press Enter to save"
    @Published var editingTextMarkID: UUID?

    var history = AnnotationHistory()
    let image: NSImage
    let imagePixelSize: CGSize
    let record: CaptureRecord
    let fileURL: URL
    var onClose: (() -> Void)?
    var saveHandler: ((AnnotationEditorModel) -> Void)?

    init(image: NSImage, imagePixelSize: CGSize, record: CaptureRecord, fileURL: URL) {
        self.image = image
        self.imagePixelSize = imagePixelSize
        self.record = record
        self.fileURL = fileURL
    }

    func arm(_ tool: AnnotationTool) {
        self.tool = tool
        cropArmed = false
        editingTextMarkID = nil
        switch tool {
        case .select:
            status = "Select — Escape closes and keeps the original"
        case .marker:
            status = "Marker armed — click to stamp step \(MarkerNumbering.next(in: marks))"
        default:
            status = "\(label(for: tool)) armed — drag to draw"
        }
    }

    func armCrop() {
        cropArmed = true
        tool = .select
        editingTextMarkID = nil
        status = "Crop armed — drag the area to keep (saves as a new capture)"
    }

    func label(for tool: AnnotationTool) -> String {
        switch tool {
        case .select: return "Select"
        case .arrow: return "Arrow"
        case .line: return "Line"
        case .box: return "Box"
        case .ellipse: return "Ellipse"
        case .freehand: return "Draw"
        case .highlight: return "Highlight"
        case .text: return "Text"
        case .redact: return "Redact"
        case .marker: return "Marker"
        }
    }

    func commit(_ mark: AnnotationMark) {
        history.recordChange(from: marks)
        marks.append(mark)
        if mark.tool == .text {
            selectedMarkID = mark.id
            editingTextMarkID = mark.id
        }
        if mark.tool == .marker {
            status = "Marker armed — click to stamp step \(MarkerNumbering.next(in: marks))"
        }
    }

    func undo() {
        if let previous = history.undo(current: marks) { marks = previous }
    }

    func redo() {
        if let next = history.redo(current: marks) { marks = next }
    }

    func deleteSelected() {
        guard let id = selectedMarkID, let index = marks.firstIndex(where: { $0.id == id }) else { return }
        history.recordChange(from: marks)
        marks.remove(at: index)
        selectedMarkID = nil
        editingTextMarkID = nil
    }

    /// Ends text editing; an empty text block was never content, so it is
    /// dropped rather than silently flattened invisible into the file.
    func finishTextEditing() {
        guard let id = editingTextMarkID else { return }
        editingTextMarkID = nil
        guard let index = marks.firstIndex(where: { $0.id == id }) else { return }
        if AnnotationEditorPolicy.shouldDiscardTextMark(text: marks[index].text) {
            marks.remove(at: index)
            if selectedMarkID == id { selectedMarkID = nil }
        }
    }

    /// Two-stage Escape, with the crop treated as an armed creation tool.
    /// Returns true when the editor should close.
    func handleEscape() -> Bool {
        if editingTextMarkID != nil {
            finishTextEditing()
            return false
        }
        if cropArmed || cropRect != nil {
            cropArmed = false
            cropRect = nil
            pendingCrop = nil
            status = AnnotationEditorPolicy.disarmedStatus
            return false
        }
        switch AnnotationEditorPolicy.escapeAction(armedTool: tool) {
        case .disarmToSelect:
            arm(.select)
            status = AnnotationEditorPolicy.disarmedStatus
            return false
        case .closeKeepingOriginal:
            return true
        }
    }

    var saveTarget: AnnotationSaveTarget {
        AnnotationSaveTarget.forOperation(cropsPixels: cropRect != nil)
    }
}

/// Owns the editor window. Enter saves (with or without marks); Escape is
/// two-stage. Both are handled by a window-scoped key monitor because the
/// drawing surface itself cannot take keyboard focus.
final class AnnotationEditorController: NSObject, NSWindowDelegate {
    private var window: NSWindow?
    private var model: AnnotationEditorModel?
    private var keyMonitor: Any?
    private let store: () -> CaptureStore
    var onSaved: ((CaptureRecord) -> Void)?

    init(store: @escaping () -> CaptureStore) {
        self.store = store
        super.init()
    }

    func open(record: CaptureRecord) {
        guard record.captureKind == .screenshot else { return }
        guard let fileURL = try? store().absoluteURL(for: record),
              let image = NSImage(contentsOf: fileURL),
              let data = try? Data(contentsOf: fileURL),
              let representation = NSBitmapImageRep(data: data) else { return }
        close()

        let pixelSize = CGSize(width: representation.pixelsWide, height: representation.pixelsHigh)
        let model = AnnotationEditorModel(
            image: image, imagePixelSize: pixelSize, record: record, fileURL: fileURL)
        model.onClose = { [weak self] in self?.close() }
        model.saveHandler = { [weak self] model in self?.save(model) }

        let window = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 980, height: 700),
            styleMask: [.titled, .closable, .resizable, .miniaturizable],
            backing: .buffered,
            defer: false)
        window.title = "Annotate — \(fileURL.lastPathComponent)"
        window.contentView = NSHostingView(rootView: AnnotationEditorView(model: model))
        window.center()
        window.delegate = self
        window.isReleasedWhenClosed = false
        window.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)

        self.window = window
        self.model = model
        installKeyMonitor()
    }

    func close() {
        removeKeyMonitor()
        window?.orderOut(nil)
        window = nil
        model = nil
    }

    func windowWillClose(_ notification: Notification) {
        removeKeyMonitor()
        window = nil
        model = nil
    }

    private func save(_ model: AnnotationEditorModel) {
        model.finishTextEditing()
        switch model.saveTarget {
        case .overwriteInPlace:
            // Enter saves with or without marks; with none there is nothing
            // to write and the original already is the result.
            if !model.marks.isEmpty {
                guard let data = AnnotationRenderer.flatten(
                    image: model.image, marks: model.marks, cropRect: nil) else { return }
                try? data.write(to: model.fileURL, options: .atomic)
            }
            close()
        case .newCapture:
            // A crop deletes pixels, and a save overwrites rather than
            // deleting — a new capture keeps the original recoverable.
            guard let crop = model.cropRect,
                  let data = AnnotationRenderer.flatten(
                    image: model.image, marks: model.marks, cropRect: crop) else { return }
            let temporary = FileManager.default.temporaryDirectory
                .appendingPathComponent("cursorpocket-crop-\(UUID().uuidString).png")
            do {
                try data.write(to: temporary)
                defer { try? FileManager.default.removeItem(at: temporary) }
                let record = try store().importFile(
                    kind: .screenshot,
                    from: temporary,
                    preview: "Cropped screenshot",
                    metadata: [
                        "width": .number(Double(Int(crop.width))),
                        "height": .number(Double(Int(crop.height))),
                    ])
                onSaved?(record)
            } catch {
                model.status = "Could not save the crop: \(error.localizedDescription)"
                return
            }
            close()
        }
    }

    private func installKeyMonitor() {
        removeKeyMonitor()
        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self, let window = self.window, let model = self.model,
                  event.window === window else { return event }
            // Let the text-mark field editor keep its keystrokes.
            if window.firstResponder is NSTextView, event.keyCode != HotkeyDefaults.keyEscape {
                return event
            }
            if event.keyCode == HotkeyDefaults.keyEscape {
                if model.handleEscape() { self.close() }
                return nil
            }
            // Return or keypad Enter saves.
            if event.keyCode == 36 || event.keyCode == 76 {
                self.save(model)
                return nil
            }
            // Delete removes the selection.
            if event.keyCode == 51, model.selectedMarkID != nil {
                model.deleteSelected()
                return nil
            }
            if event.modifierFlags.contains(.command),
               let characters = event.charactersIgnoringModifiers?.lowercased() {
                if characters == "z" {
                    event.modifierFlags.contains(.shift) ? model.redo() : model.undo()
                    return nil
                }
            }
            if event.modifierFlags.intersection([.command, .control, .option]).isEmpty,
               let key = event.charactersIgnoringModifiers?.first {
                if key == "c" {
                    model.armCrop()
                    return nil
                }
                if let tool = AnnotationTool.tool(forAccelerator: key) {
                    model.arm(tool)
                    return nil
                }
            }
            return event
        }
    }

    private func removeKeyMonitor() {
        if let keyMonitor { NSEvent.removeMonitor(keyMonitor) }
        keyMonitor = nil
    }
}
