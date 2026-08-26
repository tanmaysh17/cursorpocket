import CoreGraphics
import CoreText
import Foundation
import ImageIO
import UniformTypeIdentifiers

/// Flattens marks into pixels. Pure CoreGraphics so the output — including
/// text blocks and redaction — is unit-testable at the pixel level. Redaction
/// is a solid fill: nothing here may read a clock or a random generator, so
/// identical input always produces identical output across releases.
public enum MarkRenderer {
    /// Renders `image` with `marks` (in image-pixel, top-left coordinates),
    /// optionally cropped.
    public static func flatten(image: CGImage, marks: [AnnotationMark], cropRect: CGRect?) -> CGImage? {
        let imageWidth = CGFloat(image.width)
        let imageHeight = CGFloat(image.height)
        let crop = (cropRect ?? CGRect(x: 0, y: 0, width: imageWidth, height: imageHeight)).integral
        guard crop.width >= 1, crop.height >= 1 else { return nil }

        guard let context = CGContext(
            data: nil,
            width: Int(crop.width),
            height: Int(crop.height),
            bitsPerComponent: 8,
            bytesPerRow: 0,
            space: CGColorSpace(name: CGColorSpace.sRGB)!,
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue) else { return nil }

        // Base image: CG contexts are bottom-left, so place the full image so
        // the crop region fills the context.
        context.draw(image, in: CGRect(
            x: -crop.minX,
            y: -(imageHeight - crop.maxY),
            width: imageWidth,
            height: imageHeight))

        // Marks are stored top-left; flip once and translate into crop space.
        context.translateBy(x: 0, y: crop.height)
        context.scaleBy(x: 1, y: -1)
        context.translateBy(x: -crop.minX, y: -crop.minY)

        // Marker numbers derive from position among marker marks; the radius
        // is sized off the pre-crop image, matching the editor's live canvas.
        let markerRadius = MarkerNumbering.radius(forImageShortEdge: min(imageWidth, imageHeight))
        var markerNumber = 0
        for mark in marks {
            if mark.tool == .marker {
                markerNumber += 1
                drawMarker(mark, number: markerNumber, radius: markerRadius, in: context)
            } else {
                draw(mark, in: context)
            }
        }

        return context.makeImage()
    }

    public static func pngData(from image: CGImage) -> Data? {
        let data = NSMutableData()
        guard let destination = CGImageDestinationCreateWithData(
            data, UTType.png.identifier as CFString, 1, nil) else { return nil }
        CGImageDestinationAddImage(destination, image, nil)
        guard CGImageDestinationFinalize(destination) else { return nil }
        return data as Data
    }

    private static func color(_ hex: String) -> CGColor {
        let components = AnnotationPalette.components(of: hex)
        return CGColor(
            colorSpace: CGColorSpace(name: CGColorSpace.sRGB)!,
            components: [components.red, components.green, components.blue, 1])!
    }

    private static func draw(_ mark: AnnotationMark, in context: CGContext) {
        let markColor = color(AnnotationPalette.color(at: mark.colorIndex))
        context.setStrokeColor(markColor)
        context.setFillColor(markColor)
        context.setLineWidth(mark.strokeWidth)
        context.setLineCap(.round)
        context.setLineJoin(.round)

        switch mark.tool {
        case .select:
            break
        case .box:
            context.stroke(mark.bounds)
        case .ellipse:
            context.strokeEllipse(in: mark.bounds)
        case .line:
            context.move(to: mark.start)
            context.addLine(to: mark.end)
            context.strokePath()
        case .arrow:
            context.move(to: mark.start)
            context.addLine(to: mark.end)
            let head = AnnotationGeometry.arrowHead(from: mark.start, to: mark.end, length: 6 * mark.strokeWidth)
            context.move(to: head.left)
            context.addLine(to: mark.end)
            context.addLine(to: head.right)
            context.strokePath()
        case .freehand:
            guard mark.path.count > 1 else { break }
            context.move(to: mark.path[0])
            for point in mark.path.dropFirst() { context.addLine(to: point) }
            context.strokePath()
        case .highlight:
            // Multiply keeps dark glyphs dark under the translucent band, so
            // highlighted text stays legible; white picks up the mark color.
            context.saveGState()
            context.setAlpha(AnnotationHighlightStyle.opacity)
            context.setBlendMode(.multiply)
            context.move(to: mark.start)
            context.addLine(to: mark.end)
            context.strokePath()
            context.restoreGState()
        case .marker:
            // Drawn by `flatten` via `drawMarker` with its order-derived
            // number; unreachable here.
            break
        case .redact:
            context.setFillColor(color(AnnotationPalette.redactionColor))
            context.fill(mark.bounds)
        case .text:
            drawText(mark, color: markColor, in: context)
        }
    }

    private static func drawMarker(_ mark: AnnotationMark, number: Int, radius: CGFloat, in context: CGContext) {
        let markColor = color(AnnotationPalette.color(at: mark.colorIndex))
        context.setFillColor(markColor)
        context.fillEllipse(in: CGRect(
            x: mark.start.x - radius, y: mark.start.y - radius,
            width: radius * 2, height: radius * 2))

        let fontSize = radius * 1.1
        let font = CTFontCreateWithName("Helvetica-Bold" as CFString, fontSize, nil)
        let white = CGColor(
            colorSpace: CGColorSpace(name: CGColorSpace.sRGB)!,
            components: [1, 1, 1, 1])!
        let attributes: [CFString: Any] = [
            kCTFontAttributeName: font,
            kCTForegroundColorAttributeName: white,
        ]
        let attributed = CFAttributedStringCreate(
            nil, String(number) as CFString, attributes as CFDictionary)!
        let line = CTLineCreateWithAttributedString(attributed)
        var ascent: CGFloat = 0
        var descent: CGFloat = 0
        let width = CGFloat(CTLineGetTypographicBounds(line, &ascent, &descent, nil))
        // The context is flipped for top-left marks; un-flip locally around
        // the baseline so the numeral is upright and centered in the circle.
        context.saveGState()
        context.translateBy(
            x: mark.start.x - width / 2,
            y: mark.start.y + (ascent - descent) / 2)
        context.scaleBy(x: 1, y: -1)
        context.textPosition = .zero
        CTLineDraw(line, context)
        context.restoreGState()
    }

    private static func drawText(_ mark: AnnotationMark, color: CGColor, in context: CGContext) {
        guard !mark.text.isEmpty else { return }
        let fontSize = max(14, mark.strokeWidth * 8)
        let font = CTFontCreateWithName("Helvetica-Bold" as CFString, fontSize, nil)
        let attributes: [CFString: Any] = [
            kCTFontAttributeName: font,
            kCTForegroundColorAttributeName: color,
        ]
        let attributed = CFAttributedStringCreate(
            nil, mark.text as CFString, attributes as CFDictionary)!
        let line = CTLineCreateWithAttributedString(attributed)
        // The context is flipped for top-left marks; text glyphs need the
        // native orientation back, locally.
        context.saveGState()
        context.translateBy(x: mark.start.x, y: mark.start.y + fontSize)
        context.scaleBy(x: 1, y: -1)
        context.textPosition = .zero
        CTLineDraw(line, context)
        context.restoreGState()
    }
}
