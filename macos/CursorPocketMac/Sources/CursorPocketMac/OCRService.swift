import AppKit
import CursorPocketMacKit
import Vision

/// Recognizes text in a screenshot with Apple's on-device Vision framework —
/// local-first, nothing leaves the machine. The result is saved as a text
/// capture the user can open and copy deliberately: recognized text never
/// reaches the clipboard unasked, and this service contains no clipboard call.
final class OCRService {
    private let store: () -> CaptureStore

    init(store: @escaping () -> CaptureStore) {
        self.store = store
    }

    enum OCRError: LocalizedError {
        case notAScreenshot
        case noReadableText(tooSmall: Bool)

        var errorDescription: String? {
            switch self {
            case .notAScreenshot:
                return "Text recognition works on screenshots."
            case .noReadableText(let tooSmall):
                return tooSmall
                    ? "No readable text — the image is very small, and scaling small captures up does not help recognition."
                    : "No readable text was found in this screenshot."
            }
        }
    }

    func recognizeText(
        in record: CaptureRecord,
        completion: @escaping (Result<CaptureRecord, Error>) -> Void
    ) {
        guard record.captureKind == .screenshot else {
            completion(.failure(OCRError.notAScreenshot))
            return
        }
        let store = store()
        guard let url = try? store.absoluteURL(for: record) else {
            completion(.failure(OCRError.notAScreenshot))
            return
        }
        DispatchQueue.global(qos: .userInitiated).async {
            do {
                let request = VNRecognizeTextRequest()
                request.recognitionLevel = .accurate
                request.usesLanguageCorrection = true
                let handler = VNImageRequestHandler(url: url, options: [:])
                try handler.perform([request])

                let observations = (request.results ?? []).compactMap { observation -> OcrText.Observation? in
                    guard let candidate = observation.topCandidates(1).first else { return nil }
                    return OcrText.Observation(text: candidate.string, boundingBox: observation.boundingBox)
                }
                let text = OcrText.assemble(observations)
                guard !text.isEmpty else {
                    let tooSmall = Self.isTiny(url: url)
                    DispatchQueue.main.async {
                        completion(.failure(OCRError.noReadableText(tooSmall: tooSmall)))
                    }
                    return
                }
                let saved = try store.saveText(text)
                DispatchQueue.main.async { completion(.success(saved)) }
            } catch {
                DispatchQueue.main.async { completion(.failure(error)) }
            }
        }
    }

    private static func isTiny(url: URL) -> Bool {
        guard let data = try? Data(contentsOf: url),
              let image = NSBitmapImageRep(data: data) else { return false }
        return OcrText.isBelowSizeFloor(
            width: CGFloat(image.pixelsWide), height: CGFloat(image.pixelsHigh))
    }
}
