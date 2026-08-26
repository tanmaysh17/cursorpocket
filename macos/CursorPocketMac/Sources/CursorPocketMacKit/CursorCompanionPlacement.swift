import Foundation
import CoreGraphics

/// Placement and move-throttle decisions for the cursor companion dot. All
/// coordinates are Cocoa global points (origin bottom-left, y grows upward).
/// Pure math so the follow behavior is testable without a pointer.
public enum CursorCompanionPlacement {
    /// The dot's diameter in points.
    public static let diameter: CGFloat = 18

    /// Gap between the pointer tip and the dot. Nonzero so the dot never sits
    /// under the click point — the pointer can always click *past* the dot.
    public static let gap: CGFloat = 14

    /// Repositioning is skipped below this delta so pointer jitter does not
    /// turn into a stream of window moves.
    public static let minimumMoveDelta: CGFloat = 2

    /// The dot holds still while the pointer is within this distance of it,
    /// so the user can land on the dot and click instead of chasing it.
    public static let hoverSlop: CGFloat = 4

    /// Preferred spot: the dot trails to the lower-right of the pointer.
    /// Near the right or bottom screen edge it flips to the other side so it
    /// stays fully visible; a final clamp keeps it inside `bounds` even in a
    /// corner too tight for either side.
    public static func desiredOrigin(
        pointer: CGPoint,
        in bounds: CGRect,
        diameter: CGFloat = diameter
    ) -> CGPoint {
        var x = pointer.x + gap
        var y = pointer.y - gap - diameter
        if bounds.width > 0, bounds.height > 0 {
            if x + diameter > bounds.maxX {
                x = pointer.x - gap - diameter
            }
            if y < bounds.minY {
                y = pointer.y + gap
            }
            x = min(max(x, bounds.minX), max(bounds.minX, bounds.maxX - diameter))
            y = min(max(y, bounds.minY), max(bounds.minY, bounds.maxY - diameter))
        }
        return CGPoint(x: x, y: y)
    }

    /// Whether the dot should move at all this event: not while the pointer
    /// is on (or nearly on) the dot, and not for a sub-threshold delta.
    public static func shouldMove(
        currentFrame: CGRect,
        pointer: CGPoint,
        target: CGPoint
    ) -> Bool {
        if currentFrame.insetBy(dx: -hoverSlop, dy: -hoverSlop).contains(pointer) {
            return false
        }
        return abs(target.x - currentFrame.origin.x) > minimumMoveDelta
            || abs(target.y - currentFrame.origin.y) > minimumMoveDelta
    }
}
