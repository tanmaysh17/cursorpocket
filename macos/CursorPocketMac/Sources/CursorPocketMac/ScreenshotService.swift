import AppKit
import CursorPocketMacKit

/// Screenshots shell out to `/usr/sbin/screencapture`, which owns the
/// interactive selection UI, window picking, and Screen Recording permission
/// prompts. The reservation is made first so the file lands directly in the
/// library layout with no copy step.
final class ScreenshotService {
    private let store: () -> CaptureStore

    init(store: @escaping () -> CaptureStore) {
        self.store = store
    }

    func capture(
        mode: ScreenshotCapture.Mode,
        completion: @escaping (Result<CaptureRecord, Error>) -> Void
    ) {
        let store = store()
        let reservation = store.reserve(kind: .screenshot)
        DispatchQueue.global(qos: .userInitiated).async {
            do {
                let task = Process()
                task.executableURL = URL(fileURLWithPath: ScreenshotCapture.screencaptureToolPath)
                task.arguments = ScreenshotCapture.screencaptureArguments(
                    mode: mode, savingTo: reservation.absoluteURL.path)
                try task.run()
                task.waitUntilExit()
                let saved = ScreenshotCapture.didSave(
                    terminationStatus: task.terminationStatus,
                    fileExists: FileManager.default.fileExists(atPath: reservation.absoluteURL.path))
                guard saved else {
                    DispatchQueue.main.async { completion(.failure(ScreenshotError.cancelled)) }
                    return
                }
                let preview = Self.preview(for: mode)
                let metadata = Self.dimensions(of: reservation.absoluteURL)
                let record = try store.registerReservation(
                    reservation, preview: preview, metadata: metadata)
                DispatchQueue.main.async { completion(.success(record)) }
            } catch {
                DispatchQueue.main.async { completion(.failure(error)) }
            }
        }
    }

    enum ScreenshotError: LocalizedError {
        case cancelled
        var errorDescription: String? { "Screenshot cancelled" }
    }

    private static func preview(for mode: ScreenshotCapture.Mode) -> String {
        switch mode {
        case .interactive: return "Region screenshot"
        case .window: return "Window screenshot"
        case .display(let number): return "Display \(number) screenshot"
        case .rect: return "Region screenshot"
        }
    }

    private static func dimensions(of url: URL) -> [String: JSONValue] {
        guard let data = try? Data(contentsOf: url),
              let image = NSBitmapImageRep(data: data) else { return [:] }
        return [
            "width": .number(Double(image.pixelsWide)),
            "height": .number(Double(image.pixelsHigh)),
        ]
    }
}
