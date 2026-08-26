import Foundation
import CoreGraphics

/// Corner math for drag-to-select regions. Works purely in the coordinate
/// space it is given; callers decide whether that is points or pixels.
public enum RegionSelection {
    public static let minimumSize: CGFloat = 4

    /// The rectangle between two drag points, normalized so origin is the
    /// top-left corner regardless of drag direction.
    public static func rect(from start: CGPoint, to end: CGPoint) -> CGRect {
        CGRect(
            x: min(start.x, end.x),
            y: min(start.y, end.y),
            width: abs(end.x - start.x),
            height: abs(end.y - start.y))
    }

    public static func clamp(_ rect: CGRect, to bounds: CGRect) -> CGRect {
        let clamped = rect.intersection(bounds)
        return clamped.isNull ? .zero : clamped
    }

    public static func isUsable(_ rect: CGRect) -> Bool {
        rect.width >= minimumSize && rect.height >= minimumSize
    }

    /// `screencapture -R` wants integral x,y,w,h.
    public static func captureArgument(for rect: CGRect) -> String {
        let integral = rect.integral
        return "\(Int(integral.origin.x)),\(Int(integral.origin.y)),\(Int(integral.width)),\(Int(integral.height))"
    }
}
