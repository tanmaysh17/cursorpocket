import AppKit
import CursorPocketMacKit

/// Thin AppKit adapter over the Kit's pixel-tested `MarkRenderer`: converts
/// the editor's `NSImage` to `CGImage` and encodes the result as PNG.
enum AnnotationRenderer {
    static func flatten(image: NSImage, marks: [AnnotationMark], cropRect: CGRect?) -> Data? {
        var proposedRect = CGRect(origin: .zero, size: image.size)
        guard let cgImage = image.cgImage(forProposedRect: &proposedRect, context: nil, hints: nil),
              let flattened = MarkRenderer.flatten(image: cgImage, marks: marks, cropRect: cropRect) else {
            return nil
        }
        return MarkRenderer.pngData(from: flattened)
    }
}
