import Foundation
import CoreGraphics

/// Sizing and cascade math for pinned captures. Pure so it can be tested
/// without a window. All coordinates are Cocoa global points.
public enum PinnedCapturePlacement {
    public static let maxWidth: CGFloat = 360
    public static let maxHeight: CGFloat = 280
    public static let minSide: CGFloat = 80
    public static let margin: CGFloat = 24
    /// Each additional pin steps down-left so a stack of pins stays visible.
    public static let cascadeStep: CGFloat = 32

    /// Fits an image into the pin's maximum box, preserving aspect ratio and
    /// never upscaling past the source — except that a sliver-thin capture is
    /// grown so its short side reaches `minSide`, because a pin too thin to
    /// grab or close is worse than a slightly enlarged one. Degenerate sizes
    /// yield `.zero`.
    public static func fitSize(imageSize: CGSize) -> CGSize {
        guard imageSize.width > 0, imageSize.height > 0 else { return .zero }
        var scale = min(1, min(maxWidth / imageSize.width, maxHeight / imageSize.height))
        let shortSide = min(imageSize.width, imageSize.height)
        if shortSide * scale < minSide {
            // Grow toward the minimum, but an extreme aspect ratio may not
            // reach it: the long side is capped at twice the box so a pin can
            // never dwarf the screen.
            let grown = min(
                minSide / shortSide,
                min(maxWidth * 2 / imageSize.width, maxHeight * 2 / imageSize.height))
            scale = max(scale, grown)
        }
        return CGSize(width: imageSize.width * scale, height: imageSize.height * scale)
    }

    /// Top-right of the visible frame, cascading down-left per existing pin
    /// and clamped so the panel never leaves the screen.
    public static func origin(
        inVisibleFrame frame: CGRect,
        panelSize: CGSize,
        pinIndex: Int
    ) -> CGPoint {
        guard frame.width > 0, frame.height > 0 else { return .zero }
        let step = CGFloat(max(0, pinIndex)) * cascadeStep
        let x = frame.maxX - margin - panelSize.width - step
        let y = frame.maxY - margin - panelSize.height - step
        return CGPoint(
            x: min(max(x, frame.minX), max(frame.minX, frame.maxX - panelSize.width)),
            y: min(max(y, frame.minY), max(frame.minY, frame.maxY - panelSize.height)))
    }
}
