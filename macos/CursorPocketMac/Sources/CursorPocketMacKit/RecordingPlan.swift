import Foundation
import CoreGraphics

public enum RecordingSource: Equatable, Sendable {
    /// A whole display, by CoreGraphics display ID.
    case display(UInt32)
    /// A region of a display, in that display's point coordinates.
    case region(displayID: UInt32, rect: CGRect)
    /// A single window, by CGWindowID. The camera self-view floats over the
    /// screen, not inside the window's own pixels, so it cannot appear in a
    /// window recording's file.
    case window(windowID: UInt32)
}

/// Which display a display-recording targets is resolved from the pointer at
/// the moment the command is invoked — never when Start is pressed, because
/// by then the pointer is over the preflight panel, which the system may have
/// placed on another display.
public enum DisplayResolver {
    public static func displayUnderPointer(
        _ pointer: CGPoint,
        displays: [(id: UInt32, frame: CGRect)]
    ) -> UInt32? {
        displays.first { $0.frame.contains(pointer) }?.id ?? displays.first?.id
    }
}

public struct RecordingOptions: Equatable, Sendable {
    public var source: RecordingSource
    public var microphoneEnabled: Bool
    public var cameraEnabled: Bool
    public var cameraShape: CameraSelfViewShape

    public init(
        source: RecordingSource,
        microphoneEnabled: Bool = false,
        cameraEnabled: Bool = false,
        cameraShape: CameraSelfViewShape = .squircle
    ) {
        self.source = source
        self.microphoneEnabled = microphoneEnabled
        self.cameraEnabled = cameraEnabled
        self.cameraShape = cameraShape
    }
}

public enum RecordingState: Equatable {
    case idle
    case recording(startedAt: Date)
    case finalizing
}

/// Naming and encode decisions for screen recordings. The reserved final path
/// is `.mp4`; capture writes to a sibling `.partial.mp4` and only a successful
/// finalize moves it into place, so an unexpected exit leaves nothing
/// half-registered. Orphan recovery skips anything containing `.partial`.
public enum RecordingPlan {
    public static let framesPerSecond = 30

    public static func partialURL(for finalURL: URL) -> URL {
        finalURL.deletingPathExtension().appendingPathExtension("partial.mp4")
    }

    public static func isPartial(_ url: URL) -> Bool {
        url.lastPathComponent.contains(".partial")
    }

    /// H.264 average bitrate: 0.1 bit per pixel per frame, floored so small
    /// regions stay legible.
    public static func videoBitrate(width: Int, height: Int) -> Int {
        max(2_000_000, width * height * framesPerSecond / 10)
    }

    /// Encoders want even dimensions; pixel sizes come from points × scale.
    public static func evenPixelSize(width: CGFloat, height: CGFloat, scale: CGFloat) -> (width: Int, height: Int) {
        let w = max(2, Int(width * scale) & ~1)
        let h = max(2, Int(height * scale) & ~1)
        return (w, h)
    }

    public static func formatElapsed(_ seconds: TimeInterval) -> String {
        AudioNotePlan.formatDuration(seconds)
    }

    public static func preview(for options: RecordingOptions, durationSeconds: TimeInterval) -> String {
        let source: String
        switch options.source {
        case .display: source = "display"
        case .region: source = "region"
        case .window: source = "window"
        }
        var parts = ["Screen recording (\(source), \(formatElapsed(durationSeconds)))"]
        if options.microphoneEnabled { parts.append("narrated") }
        if options.cameraEnabled { parts.append("camera") }
        return parts.joined(separator: ", ")
    }
}
