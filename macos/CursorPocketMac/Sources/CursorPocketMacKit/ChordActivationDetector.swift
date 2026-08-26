import Foundation

public enum MouseChordButton: Equatable, Sendable {
    case left
    case right
}

/// Opens command mode when both mouse buttons are held together for a moment.
/// Port of the Windows `ChordActivationDetector` — same thresholds, same
/// semantics, so the two gestures cannot drift apart.
///
/// Deliberately strict about the chord and the duration and permissive about
/// everything else: pointer movement never cancels it. Cancelling on drift
/// would not help — the app underneath has already received the drag either
/// way — and it would only add a way for a deliberate gesture to silently
/// fail. The protection against firing during ordinary work is that chording
/// both buttons is already something nothing else asks you to do.
///
/// This is a pure state machine so it can be tested without an event tap. It
/// does not read the clock: every method takes the caller's timestamp, and
/// `shouldActivate` has to be polled, because a perfectly still hold produces
/// no mouse events at all and would otherwise never be noticed.
public final class ChordActivationDetector {
    /// How long both buttons must be held. Chording both buttons is already
    /// deliberate enough to carry the false-positive protection on its own, so
    /// the hold is short: long enough to be unmistakably intentional, short
    /// enough that the app underneath has little chance to start a rubber-band
    /// drag first.
    public static let defaultHoldSeconds: Double = 0.7

    private let holdSeconds: Double
    private var leftDown = false
    private var rightDown = false
    private var fired = false
    private var chordStartedAt: Double?

    public init() {
        holdSeconds = Self.defaultHoldSeconds
    }

    /// A non-finite or non-positive hold is refused, mirroring the Windows
    /// constructor's argument check.
    public init?(holdSeconds: Double) {
        guard holdSeconds.isFinite, holdSeconds > 0 else { return nil }
        self.holdSeconds = holdSeconds
    }

    /// Whether both buttons are currently down.
    public var isChordHeld: Bool { leftDown && rightDown }

    /// Whether the chord has already opened command mode. The gesture arms
    /// again only once both buttons come back up, so one hold cannot fire
    /// twice.
    public var hasFired: Bool { fired }

    /// When the pending chord will fire, or nil when no chord is waiting.
    public func secondsUntilActivation(at seconds: Double) -> Double? {
        guard isChordHeld, !fired, let chordStartedAt else { return nil }
        return max(0, chordStartedAt + holdSeconds - seconds)
    }

    public func press(_ button: MouseChordButton, at seconds: Double) {
        if button == .left {
            leftDown = true
        } else {
            rightDown = true
        }
        // The clock starts when the *second* button lands, not the first, so a
        // slow reach for the second button does not eat into the hold.
        if isChordHeld, chordStartedAt == nil {
            chordStartedAt = seconds
        }
    }

    public func release(_ button: MouseChordButton, at seconds: Double) {
        if button == .left {
            leftDown = false
        } else {
            rightDown = false
        }
        chordStartedAt = nil
        // Re-arm only when the hand is completely off, so releasing one button
        // and pressing it again cannot chain a second activation.
        if !leftDown, !rightDown {
            fired = false
        }
    }

    /// True exactly once per chord, the first time it is polled at or after
    /// the hold has elapsed. Poll this from a timer as well as from mouse
    /// events.
    public func shouldActivate(at seconds: Double) -> Bool {
        guard !fired, isChordHeld, let chordStartedAt else { return false }
        guard seconds - chordStartedAt >= holdSeconds else { return false }
        fired = true
        return true
    }
}
