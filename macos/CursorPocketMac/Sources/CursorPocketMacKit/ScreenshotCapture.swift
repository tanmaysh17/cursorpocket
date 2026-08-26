import Foundation
import CoreGraphics

/// Decision logic for the macOS screenshot flow: where a capture lands, what it
/// is named, how `screencapture` is invoked, and what the status strip says.
/// Kept free of AppKit/SwiftUI so it can be unit-tested.
public enum ScreenshotCapture {
    public static let folderName = "CursorPocket Captures"
    public static let screencaptureToolPath = "/usr/sbin/screencapture"

    public enum Mode: Equatable {
        /// Drag a region; space bar switches to window picking.
        case interactive
        /// Start straight in window-picking mode.
        case window
        /// Capture one whole display (1-based, as `screencapture -D` counts).
        case display(Int)
        /// Capture a fixed rectangle in global display points.
        case rect(CGRect)
    }

    /// The capture folder inside the user's Documents directory, matching the
    /// Windows layout (`Documents\CursorPocket Captures`).
    public static func captureFolder(inDocuments documents: URL) -> URL {
        documents.appendingPathComponent(folderName, isDirectory: true)
    }

    /// Timestamped destination for a new screenshot. The name must be stable
    /// across user locales and calendars, so the formatter is pinned.
    public static func screenshotDestination(in folder: URL, at date: Date, timeZone: TimeZone = .current) -> URL {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.calendar = Calendar(identifier: .gregorian)
        formatter.timeZone = timeZone
        formatter.dateFormat = "yyyy-MM-dd_HH-mm-ss"
        return folder.appendingPathComponent("\(formatter.string(from: date))_screenshot.png")
    }

    /// Arguments for `/usr/sbin/screencapture`. `-x` always: the capture sound
    /// would land in a simultaneous recording's microphone track.
    public static func screencaptureArguments(mode: Mode, savingTo destinationPath: String) -> [String] {
        switch mode {
        case .interactive:
            return ["-i", "-x", destinationPath]
        case .window:
            // -W starts interactive capture in window-selection mode; -o drops
            // the window shadow so the capture matches what other tools see.
            return ["-i", "-W", "-x", "-o", destinationPath]
        case .display(let number):
            return ["-D", String(max(1, number)), "-x", destinationPath]
        case .rect(let rect):
            return ["-R", RegionSelection.captureArgument(for: rect), "-x", destinationPath]
        }
    }

    public static func screencaptureArguments(savingTo destinationPath: String) -> [String] {
        screencaptureArguments(mode: .interactive, savingTo: destinationPath)
    }

    /// A zero exit alone is not enough: `screencapture -i` also exits 0 when
    /// the user presses Escape, and then simply writes no file.
    public static func didSave(terminationStatus: Int32, fileExists: Bool) -> Bool {
        terminationStatus == 0 && fileExists
    }

    public static func statusMessage(saved: Bool, destination: URL) -> String {
        saved ? "Saved \(destination.lastPathComponent)" : "Screenshot cancelled"
    }
}
