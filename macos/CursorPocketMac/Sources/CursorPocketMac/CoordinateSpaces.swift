import AppKit

/// AppKit windows live in Cocoa global coordinates (origin bottom-left of the
/// primary screen, y up). CoreGraphics displays, ScreenCaptureKit, and
/// `screencapture -R` use CG coordinates (origin top-left, y down). Mixing the
/// two silently captures the wrong strip of screen, so every conversion goes
/// through here.
enum CoordinateSpaces {
    static var primaryScreenHeight: CGFloat {
        NSScreen.screens.first?.frame.height ?? 0
    }

    static func cgRect(fromCocoa rect: CGRect) -> CGRect {
        CGRect(
            x: rect.origin.x,
            y: primaryScreenHeight - rect.origin.y - rect.height,
            width: rect.width,
            height: rect.height)
    }

    static func cocoaRect(fromCG rect: CGRect) -> CGRect {
        // The transform is its own inverse.
        cgRect(fromCocoa: rect)
    }

    static func displayID(for screen: NSScreen) -> CGDirectDisplayID? {
        screen.deviceDescription[NSDeviceDescriptionKey("NSScreenNumber")] as? CGDirectDisplayID
    }

    static func screen(forDisplayID displayID: CGDirectDisplayID) -> NSScreen? {
        NSScreen.screens.first { self.displayID(for: $0) == displayID }
    }
}
