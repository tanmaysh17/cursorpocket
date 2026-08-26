import AppKit
import CursorPocketMacKit

/// Feeds global pointer movement into the double-circle detector so drawing
/// two quick circles opens the command palette — the same signature gesture
/// as on Windows. Mouse-move monitors need no Accessibility grant; a global
/// monitor covers other apps' screens and a local one covers our own.
final class GestureService {
    private let detector = DoubleCircleGestureDetector()
    private var globalMonitor: Any?
    private var localMonitor: Any?

    var onGesture: (() -> Void)?

    private static let movementEvents: NSEvent.EventTypeMask = [
        .mouseMoved, .leftMouseDragged, .rightMouseDragged, .otherMouseDragged,
    ]

    var isRunning: Bool { globalMonitor != nil }

    func start() {
        guard globalMonitor == nil else { return }
        globalMonitor = NSEvent.addGlobalMonitorForEvents(matching: Self.movementEvents) { [weak self] event in
            self?.feed(event)
        }
        localMonitor = NSEvent.addLocalMonitorForEvents(matching: Self.movementEvents) { [weak self] event in
            self?.feed(event)
            return event
        }
    }

    func stop() {
        if let globalMonitor { NSEvent.removeMonitor(globalMonitor) }
        if let localMonitor { NSEvent.removeMonitor(localMonitor) }
        globalMonitor = nil
        localMonitor = nil
        detector.reset()
    }

    private func feed(_ event: NSEvent) {
        // Only the path's shape matters, so Cocoa global coordinates are fine
        // as-is; event timestamps are monotonic uptime seconds.
        let location = NSEvent.mouseLocation
        if detector.feed(x: location.x, y: location.y, now: event.timestamp) {
            DispatchQueue.main.async { [weak self] in self?.onGesture?() }
        }
    }
}
