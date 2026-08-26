import AppKit
import ApplicationServices
import CursorPocketMacKit

/// Holding both mouse buttons together for 700 ms opens the command palette,
/// same as Windows. The Windows invariants carried over exactly:
/// - The FIRST button-down is never swallowed, so ordinary clicks and drags
///   are untouched.
/// - The SECOND button-down and every mouse-button event after it ARE
///   swallowed, with a synthetic release posted for the first button —
///   passing the chord through would pop the target app's context menu
///   behind the palette and leave it thinking a button is still down.
/// - The hold is timed by a polling timer, not by mouse events: a perfectly
///   still hold emits none, so the tap alone would never fire it.
///
/// Requires Accessibility trust for the modifying event tap. This service
/// never prompts: without trust (or if the tap cannot be created) it simply
/// stays off.
final class ChordService {
    var onChord: (() -> Void)?

    private var detector = ChordActivationDetector()
    private var tap: CFMachPort?
    private var runLoopSource: CFRunLoopSource?
    private var pollTimer: Timer?

    // Physical button state tracked from the tap, since while swallowing the
    // rest of the system believes both buttons are up.
    private var leftDown = false
    private var rightDown = false
    private var swallowing = false
    private var firstButton: MouseChordButton?

    /// Marks our synthetic events so the tap passes them through instead of
    /// feeding them back into the detector.
    private static let syntheticMarker: Int64 = 0x43_50_43_48 // "CPCH"

    var isRunning: Bool { tap != nil }

    func start() {
        guard tap == nil else { return }
        // Degrade silently without Accessibility trust — prompting belongs to
        // explicit user flows, never to service startup.
        guard AXIsProcessTrusted() else { return }
        let types: [CGEventType] = [
            .leftMouseDown, .leftMouseUp, .rightMouseDown, .rightMouseUp,
            .leftMouseDragged, .rightMouseDragged,
        ]
        let mask: CGEventMask = types.reduce(0) { $0 | (CGEventMask(1) << $1.rawValue) }
        guard let tap = CGEvent.tapCreate(
            tap: .cghidEventTap,
            place: .headInsertEventTap,
            options: .defaultTap,
            eventsOfInterest: mask,
            callback: chordEventTapCallback,
            userInfo: Unmanaged.passUnretained(self).toOpaque())
        else { return }
        self.tap = tap
        let source = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, tap, 0)
        runLoopSource = source
        CFRunLoopAddSource(CFRunLoopGetMain(), source, .commonModes)
        CGEvent.tapEnable(tap: tap, enable: true)

        let timer = Timer(timeInterval: 0.05, repeats: true) { [weak self] _ in self?.poll() }
        RunLoop.main.add(timer, forMode: .common)
        pollTimer = timer
    }

    func stop() {
        pollTimer?.invalidate()
        pollTimer = nil
        if let tap {
            CGEvent.tapEnable(tap: tap, enable: false)
            CFMachPortInvalidate(tap)
        }
        if let runLoopSource {
            CFRunLoopRemoveSource(CFRunLoopGetMain(), runLoopSource, .commonModes)
        }
        tap = nil
        runLoopSource = nil
        detector = ChordActivationDetector()
        leftDown = false
        rightDown = false
        swallowing = false
        firstButton = nil
    }

    /// Runs on the main run loop — the tap's source is attached there — so no
    /// synchronization is needed with `poll`.
    fileprivate func handle(type: CGEventType, event: CGEvent) -> Unmanaged<CGEvent>? {
        switch type {
        case .tapDisabledByTimeout, .tapDisabledByUserInput:
            if let tap { CGEvent.tapEnable(tap: tap, enable: true) }
            return Unmanaged.passUnretained(event)
        default:
            break
        }
        if event.getIntegerValueField(.eventSourceUserData) == Self.syntheticMarker {
            return Unmanaged.passUnretained(event)
        }
        let now = ProcessInfo.processInfo.systemUptime
        let swallow: Bool
        switch type {
        case .leftMouseDown: swallow = handleDown(.left, event: event, at: now)
        case .rightMouseDown: swallow = handleDown(.right, event: event, at: now)
        case .leftMouseUp: swallow = handleUp(.left, at: now)
        case .rightMouseUp: swallow = handleUp(.right, at: now)
        case .leftMouseDragged, .rightMouseDragged: swallow = swallowing
        default: swallow = false
        }
        if detector.shouldActivate(at: now) { fire() }
        return swallow ? nil : Unmanaged.passUnretained(event)
    }

    private func handleDown(_ button: MouseChordButton, event: CGEvent, at now: Double) -> Bool {
        let otherDown = button == .left ? rightDown : leftDown
        if button == .left { leftDown = true } else { rightDown = true }
        detector.press(button, at: now)
        if swallowing { return true }
        if otherDown, let first = firstButton {
            // The second button-down starts the swallow: from here until both
            // buttons are physically up, the app underneath sees nothing, and
            // the first button is released for it synthetically so it is not
            // left thinking a button is still down.
            swallowing = true
            postSyntheticRelease(of: first, at: event.location)
            return true
        }
        firstButton = button
        return false
    }

    private func handleUp(_ button: MouseChordButton, at now: Double) -> Bool {
        if button == .left { leftDown = false } else { rightDown = false }
        detector.release(button, at: now)
        let swallow = swallowing
        if !leftDown, !rightDown {
            swallowing = false
            firstButton = nil
        }
        return swallow
    }

    private func postSyntheticRelease(of button: MouseChordButton, at position: CGPoint) {
        let type: CGEventType = button == .left ? .leftMouseUp : .rightMouseUp
        let mouseButton: CGMouseButton = button == .left ? .left : .right
        guard let release = CGEvent(
            mouseEventSource: nil,
            mouseType: type,
            mouseCursorPosition: position,
            mouseButton: mouseButton)
        else { return }
        release.setIntegerValueField(.mouseEventClickState, value: 1)
        release.setIntegerValueField(.eventSourceUserData, value: Self.syntheticMarker)
        release.post(tap: .cghidEventTap)
    }

    /// A perfectly still hold produces no mouse events, so the detector must
    /// be polled as well as fed.
    private func poll() {
        if detector.shouldActivate(at: ProcessInfo.processInfo.systemUptime) {
            fire()
        }
    }

    private func fire() {
        DispatchQueue.main.async { [weak self] in self?.onChord?() }
    }
}

/// C-convention trampoline into the service; `userInfo` is the unretained
/// service pointer supplied at tap creation.
private func chordEventTapCallback(
    proxy: CGEventTapProxy,
    type: CGEventType,
    event: CGEvent,
    userInfo: UnsafeMutableRawPointer?
) -> Unmanaged<CGEvent>? {
    guard let userInfo else { return Unmanaged.passUnretained(event) }
    let service = Unmanaged<ChordService>.fromOpaque(userInfo).takeUnretainedValue()
    return service.handle(type: type, event: event)
}
