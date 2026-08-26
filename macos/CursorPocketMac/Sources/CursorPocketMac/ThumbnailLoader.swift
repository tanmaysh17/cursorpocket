import AppKit
import ImageIO

/// Downsamples screenshot files for Library rows through ImageIO's thumbnail
/// path, so a full-resolution capture is never decoded just to draw a 56 pt
/// row. Results are cached keyed by record id *and* the file's modification
/// date — annotation overwrites the file in place under the same id, and a
/// stale thumbnail would silently show the pre-annotation pixels.
final class ThumbnailLoader {
    static let shared = ThumbnailLoader()

    static let maxPixelSize = 112

    private let cache = NSCache<NSString, NSImage>()
    private let queue = DispatchQueue(label: "cursorpocket.thumbnails", qos: .utility)

    /// Delivers on the main queue; nil when the file is missing or unreadable
    /// (the row keeps its kind icon).
    func load(recordID: String, fileURL: URL, completion: @escaping (NSImage?) -> Void) {
        queue.async { [cache] in
            let attributes = try? FileManager.default.attributesOfItem(atPath: fileURL.path)
            let modified = (attributes?[.modificationDate] as? Date)?
                .timeIntervalSinceReferenceDate ?? 0
            let key = "\(recordID)|\(modified)" as NSString
            if let cached = cache.object(forKey: key) {
                DispatchQueue.main.async { completion(cached) }
                return
            }
            let options: [CFString: Any] = [
                kCGImageSourceCreateThumbnailFromImageAlways: true,
                kCGImageSourceCreateThumbnailWithTransform: true,
                kCGImageSourceThumbnailMaxPixelSize: Self.maxPixelSize,
                kCGImageSourceShouldCache: false,
            ]
            var image: NSImage?
            if let source = CGImageSourceCreateWithURL(fileURL as CFURL, nil),
               let thumbnail = CGImageSourceCreateThumbnailAtIndex(source, 0, options as CFDictionary) {
                let sized = NSImage(
                    cgImage: thumbnail,
                    size: NSSize(width: thumbnail.width, height: thumbnail.height))
                cache.setObject(sized, forKey: key)
                image = sized
            }
            DispatchQueue.main.async { completion(image) }
        }
    }
}
