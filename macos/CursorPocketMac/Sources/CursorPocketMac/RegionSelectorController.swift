import AppKit
import CursorPocketMacKit

/// Full-screen overlay for drag-selecting a recording region. One overlay per
/// display; the display where the drag starts wins. `Escape` cancels without
/// starting anything — nothing has been captured yet, so there is nothing to
/// save.
final class RegionSelectorController {
    private var windows: [NSWindow] = []
    private var completion: (((displayID: CGDirectDisplayID, rectCG: CGRect)?) -> Void)?

    func begin(completion: @escaping ((displayID: CGDirectDisplayID, rectCG: CGRect)?) -> Void) {
        dismiss()
        self.completion = completion
        for screen in NSScreen.screens {
            guard let displayID = CoordinateSpaces.displayID(for: screen) else { continue }
            let window = KeyableBorderlessWindow(
                contentRect: screen.frame,
                styleMask: [.borderless],
                backing: .buffered,
                defer: false)
            window.level = .screenSaver
            window.isOpaque = false
            window.backgroundColor = .clear
            window.ignoresMouseEvents = false
            window.acceptsMouseMovedEvents = true
            // The selector itself must never appear in any capture.
            window.sharingType = .none
            let view = RegionSelectionView(frame: NSRect(origin: .zero, size: screen.frame.size))
            view.onSelected = { [weak self] viewRect in
                guard let self else { return }
                // View coordinates are window-local Cocoa; lift to global then to CG.
                let windowRect = view.convert(viewRect, to: nil)
                let screenRect = window.convertToScreen(windowRect)
                let cg = CoordinateSpaces.cgRect(fromCocoa: screenRect)
                self.finish(with: (displayID, cg))
            }
            view.onCancelled = { [weak self] in self?.finish(with: nil) }
            window.contentView = view
            window.makeKeyAndOrderFront(nil)
            window.makeFirstResponder(view)
            windows.append(window)
        }
        NSApp.activate(ignoringOtherApps: true)
    }

    private func finish(with selection: (displayID: CGDirectDisplayID, rectCG: CGRect)?) {
        let callback = completion
        completion = nil
        dismiss()
        callback?(selection)
    }

    private func dismiss() {
        for window in windows { window.orderOut(nil) }
        windows = []
    }
}

/// A borderless window refuses key status by default, which would make the
/// Escape-to-cancel key dead.
private final class KeyableBorderlessWindow: NSWindow {
    override var canBecomeKey: Bool { true }
}

private final class RegionSelectionView: NSView {
    var onSelected: ((CGRect) -> Void)?
    var onCancelled: (() -> Void)?

    private var dragStart: CGPoint?
    private var dragCurrent: CGPoint?

    override var acceptsFirstResponder: Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        NSColor.black.withAlphaComponent(0.25).setFill()
        bounds.fill()
        guard let rect = selectionRect else { return }
        NSColor.clear.setFill()
        rect.fill(using: .copy)
        Theme.readyNS.setStroke()
        let outline = NSBezierPath(rect: rect)
        outline.lineWidth = 2
        outline.stroke()
    }

    private var selectionRect: CGRect? {
        guard let dragStart, let dragCurrent else { return nil }
        return RegionSelection.rect(from: dragStart, to: dragCurrent)
    }

    override func mouseDown(with event: NSEvent) {
        dragStart = convert(event.locationInWindow, from: nil)
        dragCurrent = dragStart
        needsDisplay = true
    }

    override func mouseDragged(with event: NSEvent) {
        dragCurrent = convert(event.locationInWindow, from: nil)
        needsDisplay = true
    }

    override func mouseUp(with event: NSEvent) {
        dragCurrent = convert(event.locationInWindow, from: nil)
        defer { dragStart = nil; dragCurrent = nil }
        guard let rect = selectionRect, RegionSelection.isUsable(rect) else {
            onCancelled?()
            return
        }
        onSelected?(RegionSelection.clamp(rect, to: bounds))
    }

    override func keyDown(with event: NSEvent) {
        if event.keyCode == HotkeyDefaults.keyEscape {
            onCancelled?()
        } else {
            super.keyDown(with: event)
        }
    }
}
