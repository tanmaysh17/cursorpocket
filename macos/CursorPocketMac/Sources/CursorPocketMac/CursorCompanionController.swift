import AppKit
import CursorPocketMacKit

/// The cursor companion: a tiny dot that trails the pointer — the product's
/// signature. Green hollow ring when idle; while recording it becomes a red
/// ring with a filled square inside (the classic record mark), so the state
/// is carried by shape as well as color. Clicking it opens the command
/// palette. It never takes key focus or activation, and it is excluded from
/// capture so it can trail the pointer straight through a recording without
/// appearing in the file.
final class CursorCompanionController {
    private var panel: NSPanel?
    private var dotView: CompanionDotView?
    private var globalMonitor: Any?
    private var localMonitor: Any?
    private var isRecording = false

    var onClick: (() -> Void)?
    var windowNumber: Int? { panel?.windowNumber }
    var isRunning: Bool { panel != nil }

    private static let movementEvents: NSEvent.EventTypeMask = [
        .mouseMoved, .leftMouseDragged, .rightMouseDragged, .otherMouseDragged,
    ]

    func start() {
        guard panel == nil else { return }
        buildPanel()
        // Mouse-move monitors need no Accessibility grant; the global monitor
        // covers other apps' screens and the local one covers our own.
        globalMonitor = NSEvent.addGlobalMonitorForEvents(matching: Self.movementEvents) { [weak self] _ in
            self?.followPointer()
        }
        localMonitor = NSEvent.addLocalMonitorForEvents(matching: Self.movementEvents) { [weak self] event in
            self?.followPointer()
            return event
        }
        followPointer(force: true)
    }

    func stop() {
        if let globalMonitor { NSEvent.removeMonitor(globalMonitor) }
        if let localMonitor { NSEvent.removeMonitor(localMonitor) }
        globalMonitor = nil
        localMonitor = nil
        panel?.orderOut(nil)
        panel = nil
        dotView = nil
    }

    /// Recording state changes the glyph as well as the color — never color
    /// alone.
    func setRecording(_ recording: Bool) {
        isRecording = recording
        dotView?.isRecording = recording
        dotView?.toolTip = recording
            ? "CursorPocket — RECORDING. Click for commands."
            : "CursorPocket — click for commands"
    }

    private func buildPanel() {
        let side = CursorCompanionPlacement.diameter
        let panel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: side, height: side),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false)
        panel.level = .statusBar
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.hidesOnDeactivate = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        // CursorPocket chrome never appears in captured media.
        panel.sharingType = .none
        panel.ignoresMouseEvents = false

        let dot = CompanionDotView(frame: NSRect(x: 0, y: 0, width: side, height: side))
        dot.isRecording = isRecording
        dot.onClick = { [weak self] in self?.onClick?() }
        panel.contentView = dot
        panel.orderFrontRegardless()
        self.panel = panel
        self.dotView = dot
        setRecording(isRecording)
    }

    private func followPointer(force: Bool = false) {
        guard let panel else { return }
        let pointer = NSEvent.mouseLocation
        let screen = NSScreen.screens.first { NSMouseInRect(pointer, $0.frame, false) }
            ?? NSScreen.main
        let bounds = screen?.visibleFrame ?? .zero
        let target = CursorCompanionPlacement.desiredOrigin(pointer: pointer, in: bounds)
        // The dot holds still while the pointer is over it — otherwise it
        // would flee every attempt to click it — and skips sub-2pt moves so
        // jitter never becomes a layout storm.
        guard force || CursorCompanionPlacement.shouldMove(
            currentFrame: panel.frame, pointer: pointer, target: target) else { return }
        panel.setFrameOrigin(target)
    }
}

/// Draws the companion dot. Idle: hollow green ring. Recording: red ring with
/// a filled square inside, the classic record mark, so the state reads
/// without color.
private final class CompanionDotView: NSView {
    var isRecording = false {
        didSet { needsDisplay = true }
    }
    var onClick: (() -> Void)?

    // The dot must be clickable without activating anything.
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
    override var acceptsFirstResponder: Bool { false }

    override func mouseUp(with event: NSEvent) {
        if bounds.contains(convert(event.locationInWindow, from: nil)) {
            onClick?()
        }
    }

    override func draw(_ dirtyRect: NSRect) {
        let color = isRecording ? Theme.alertNS : Theme.readyNS
        let lineWidth: CGFloat = 2.5
        let ringRect = bounds.insetBy(dx: lineWidth / 2 + 0.5, dy: lineWidth / 2 + 0.5)

        // A soft dark halo keeps the ring legible over both light and dark
        // content without reading as a decorative border.
        let halo = NSBezierPath(ovalIn: bounds.insetBy(dx: 0.5, dy: 0.5))
        NSColor.black.withAlphaComponent(0.35).setStroke()
        halo.lineWidth = 1
        halo.stroke()

        let ring = NSBezierPath(ovalIn: ringRect)
        ring.lineWidth = lineWidth
        color.setStroke()
        ring.stroke()

        if isRecording {
            let squareSide = bounds.width * 0.32
            let square = NSRect(
                x: bounds.midX - squareSide / 2,
                y: bounds.midY - squareSide / 2,
                width: squareSide,
                height: squareSide)
            color.setFill()
            NSBezierPath(roundedRect: square, xRadius: 1.5, yRadius: 1.5).fill()
        }
    }
}
