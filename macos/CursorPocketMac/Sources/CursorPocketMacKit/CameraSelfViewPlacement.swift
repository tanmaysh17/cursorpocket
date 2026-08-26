import Foundation
import CoreGraphics

/// The webcam reaches the file by being on screen inside the recorded
/// rectangle, so the self-view must land fully inside it — anything outside
/// is simply absent from the recording. Shape drives aspect, matching the
/// Windows rule: squircle records 1:1, rounded 16:9.
public enum CameraSelfViewShape: String, Codable, CaseIterable, Equatable, Sendable {
    case squircle
    case rounded

    public var aspectRatio: CGFloat {
        switch self {
        case .squircle: return 1
        case .rounded: return 16.0 / 9.0
        }
    }
}

public enum CameraSelfViewPlacement {
    public static let margin: CGFloat = 16
    public static let minimumHeight: CGFloat = 120
    public static let maximumHeight: CGFloat = 320

    /// Bottom-right corner of the recorded rectangle, sized to a fraction of
    /// its height and clamped so the window always fits inside.
    public static func compute(
        recordedRect: CGRect,
        shape: CameraSelfViewShape,
        heightFraction: CGFloat = 0.22
    ) -> CGRect {
        guard recordedRect.width > 0, recordedRect.height > 0 else { return .zero }
        var height = min(max(recordedRect.height * heightFraction, minimumHeight), maximumHeight)
        height = min(height, max(1, recordedRect.height - margin * 2))
        var width = height * shape.aspectRatio
        if width > recordedRect.width - margin * 2 {
            width = max(1, recordedRect.width - margin * 2)
            height = width / shape.aspectRatio
        }
        let origin = CGPoint(
            x: recordedRect.maxX - margin - width,
            y: recordedRect.maxY - margin - height)
        return clamp(CGRect(origin: origin, size: CGSize(width: width, height: height)), into: recordedRect)
    }

    /// Keeps a dragged self-view inside the recorded rectangle.
    public static func clamp(_ rect: CGRect, into recordedRect: CGRect) -> CGRect {
        var result = rect
        result.origin.x = min(max(result.origin.x, recordedRect.minX), max(recordedRect.minX, recordedRect.maxX - result.width))
        result.origin.y = min(max(result.origin.y, recordedRect.minY), max(recordedRect.minY, recordedRect.maxY - result.height))
        return result
    }
}
