import CoreGraphics
import Foundation
import XCTest
@testable import CursorPocketMacKit

final class MarkRendererTests: XCTestCase {
    // MARK: Pixel helpers

    private func makeWhiteImage(width: Int, height: Int) -> CGImage {
        let context = CGContext(
            data: nil, width: width, height: height,
            bitsPerComponent: 8, bytesPerRow: 0,
            space: CGColorSpace(name: CGColorSpace.sRGB)!,
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        context.setFillColor(CGColor(
            colorSpace: CGColorSpace(name: CGColorSpace.sRGB)!,
            components: [1, 1, 1, 1])!)
        context.fill(CGRect(x: 0, y: 0, width: width, height: height))
        return context.makeImage()!
    }

    /// RGBA bytes with row 0 at the TOP of the image.
    private func rgbaBytes(of image: CGImage) -> [UInt8] {
        let width = image.width, height = image.height
        var bytes = [UInt8](repeating: 0, count: width * height * 4)
        let context = CGContext(
            data: &bytes, width: width, height: height,
            bitsPerComponent: 8, bytesPerRow: width * 4,
            space: CGColorSpace(name: CGColorSpace.sRGB)!,
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)!
        context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))
        return bytes
    }

    private func pixel(_ bytes: [UInt8], width: Int, x: Int, yFromTop: Int) -> (r: UInt8, g: UInt8, b: UInt8) {
        let offset = (yFromTop * width + x) * 4
        return (bytes[offset], bytes[offset + 1], bytes[offset + 2])
    }

    private func nonWhiteCount(of image: CGImage) -> Int {
        let bytes = rgbaBytes(of: image)
        var count = 0
        for index in stride(from: 0, to: bytes.count, by: 4)
        where bytes[index] < 250 || bytes[index + 1] < 250 || bytes[index + 2] < 250 {
            count += 1
        }
        return count
    }

    // MARK: Redaction

    func testRedactionFillsTheExactRegionSolidBlack() throws {
        let base = makeWhiteImage(width: 100, height: 100)
        let mark = AnnotationMark(
            tool: .redact, start: CGPoint(x: 10, y: 10), end: CGPoint(x: 40, y: 40))
        let result = try XCTUnwrap(MarkRenderer.flatten(image: base, marks: [mark], cropRect: nil))
        let bytes = rgbaBytes(of: result)

        // Inside the block (top-left coordinates — a vertical-flip bug would
        // paint rows 60...89 instead).
        let inside = pixel(bytes, width: 100, x: 25, yFromTop: 25)
        XCTAssertLessThan(inside.r, 10)
        XCTAssertLessThan(inside.g, 10)
        XCTAssertLessThan(inside.b, 10)

        // The mirrored position must stay white.
        let mirrored = pixel(bytes, width: 100, x: 25, yFromTop: 75)
        XCTAssertGreaterThan(mirrored.r, 245)

        // Roughly the block's area is dark, nothing more.
        let dark = nonWhiteCount(of: result)
        XCTAssertGreaterThan(dark, 800)
        XCTAssertLessThan(dark, 1100)
    }

    // MARK: Text blocks

    func testTextBlockActuallyRendersPixels() throws {
        let base = makeWhiteImage(width: 300, height: 100)
        let text = AnnotationMark(
            tool: .text, start: CGPoint(x: 10, y: 20), end: CGPoint(x: 10, y: 20),
            text: "HELLO", colorIndex: 0)
        let result = try XCTUnwrap(MarkRenderer.flatten(image: base, marks: [text], cropRect: nil))
        XCTAssertGreaterThan(
            nonWhiteCount(of: result), 50,
            "A committed text block must change pixels — an invisible text mark is the bug this guards against.")
    }

    func testEmptyTextBlockRendersNothing() throws {
        let base = makeWhiteImage(width: 300, height: 100)
        let empty = AnnotationMark(
            tool: .text, start: CGPoint(x: 10, y: 20), end: CGPoint(x: 10, y: 20),
            text: "", colorIndex: 0)
        let result = try XCTUnwrap(MarkRenderer.flatten(image: base, marks: [empty], cropRect: nil))
        XCTAssertEqual(nonWhiteCount(of: result), 0)
    }

    func testEmptyTextMarksAreDiscardedByPolicy() {
        XCTAssertTrue(AnnotationEditorPolicy.shouldDiscardTextMark(text: ""))
        XCTAssertTrue(AnnotationEditorPolicy.shouldDiscardTextMark(text: "  \n "))
        XCTAssertFalse(AnnotationEditorPolicy.shouldDiscardTextMark(text: "note"))
    }

    // MARK: Highlighter

    func testHighlightMultipliesSoDarkPixelsStayDarkAndWhiteIsTinted() throws {
        let base = makeWhiteImage(width: 100, height: 100)
        // Black region standing in for text glyphs, then a highlight band
        // straight across it (width 14 covers rows 43...57).
        let ink = AnnotationMark(
            tool: .redact, start: CGPoint(x: 40, y: 20), end: CGPoint(x: 60, y: 80))
        let highlight = AnnotationMark(
            tool: .highlight, start: CGPoint(x: 10, y: 50), end: CGPoint(x: 90, y: 50),
            colorIndex: 0, strokeWidth: AnnotationTool.highlight.defaultStrokeWidth)
        let result = try XCTUnwrap(
            MarkRenderer.flatten(image: base, marks: [ink, highlight], cropRect: nil))
        let bytes = rgbaBytes(of: result)

        // Multiply: black under the band stays black — the text stays legible.
        let overInk = pixel(bytes, width: 100, x: 50, yFromTop: 50)
        XCTAssertLessThan(overInk.r, 20)
        XCTAssertLessThan(overInk.g, 20)
        XCTAssertLessThan(overInk.b, 20)

        // White under the band picks up the mark color, translucently: not
        // white anymore, but far lighter than the solid color would be.
        let overPaper = pixel(bytes, width: 100, x: 20, yFromTop: 50)
        XCTAssertGreaterThan(overPaper.r, 240, "Red channel of #FF5A67 over white stays high")
        XCTAssertLessThan(overPaper.g, 235, "The band must visibly tint white")
        XCTAssertGreaterThan(overPaper.g, 150, "The band must stay translucent, not solid color")

        // Outside the band, untouched.
        let outside = pixel(bytes, width: 100, x: 20, yFromTop: 10)
        XCTAssertGreaterThan(outside.r, 245)
        XCTAssertGreaterThan(outside.g, 245)
    }

    // MARK: Step markers

    func testMarkerStampsAFilledCircleWithAWhiteNumeral() throws {
        let base = makeWhiteImage(width: 200, height: 200)
        let marker = AnnotationMark(
            tool: .marker, start: CGPoint(x: 100, y: 100), end: CGPoint(x: 100, y: 100),
            colorIndex: 0)
        let result = try XCTUnwrap(MarkRenderer.flatten(image: base, marks: [marker], cropRect: nil))
        let bytes = rgbaBytes(of: result)

        // Short edge 200 → radius clamps to the 12 px floor.
        XCTAssertEqual(MarkerNumbering.radius(forImageShortEdge: 200), 12)

        // Inside the circle, left of the numeral: the mark color (#FF5A67).
        let fill = pixel(bytes, width: 200, x: 91, yFromTop: 100)
        XCTAssertGreaterThan(fill.r, 200)
        XCTAssertLessThan(fill.g, 160)

        // Outside the circle: untouched white.
        let outside = pixel(bytes, width: 200, x: 100, yFromTop: 130)
        XCTAssertGreaterThan(outside.r, 245)

        // The white "1" leaves bright pixels strictly inside the circle —
        // the whole outside is white, so restrict the scan to the interior.
        var brightInside = 0
        for y in 90...110 {
            for x in 90...110 {
                let dx = CGFloat(x - 100), dy = CGFloat(y - 100)
                guard dx * dx + dy * dy <= 100 else { continue }
                let value = pixel(bytes, width: 200, x: x, yFromTop: y)
                if value.r > 150, value.g > 150, value.b > 150 { brightInside += 1 }
            }
        }
        XCTAssertGreaterThanOrEqual(brightInside, 2, "The numeral must render inside the circle")
    }

    func testMarkerNumbersFollowArrayOrderInTheFlattenedOutput() throws {
        // Two markers flatten without error and both stamp their circles;
        // the numeral values themselves are covered by MarkerNumbering tests.
        let base = makeWhiteImage(width: 200, height: 200)
        let first = AnnotationMark(
            tool: .marker, start: CGPoint(x: 50, y: 50), end: CGPoint(x: 50, y: 50), colorIndex: 0)
        let second = AnnotationMark(
            tool: .marker, start: CGPoint(x: 150, y: 150), end: CGPoint(x: 150, y: 150), colorIndex: 0)
        let result = try XCTUnwrap(
            MarkRenderer.flatten(image: base, marks: [first, second], cropRect: nil))
        let bytes = rgbaBytes(of: result)
        XCTAssertLessThan(pixel(bytes, width: 200, x: 41, yFromTop: 50).g, 160)
        XCTAssertLessThan(pixel(bytes, width: 200, x: 141, yFromTop: 150).g, 160)
    }

    // MARK: Strokes

    func testBoxStrokeLandsOnItsEdges() throws {
        let base = makeWhiteImage(width: 100, height: 100)
        let box = AnnotationMark(
            tool: .box, start: CGPoint(x: 20, y: 20), end: CGPoint(x: 80, y: 60),
            colorIndex: 0, strokeWidth: 4)
        let result = try XCTUnwrap(MarkRenderer.flatten(image: base, marks: [box], cropRect: nil))
        let bytes = rgbaBytes(of: result)
        let onEdge = pixel(bytes, width: 100, x: 50, yFromTop: 20)
        XCTAssertLessThan(onEdge.g, 200, "Top edge of the box should be stroked")
        let center = pixel(bytes, width: 100, x: 50, yFromTop: 40)
        XCTAssertGreaterThan(center.r, 245, "Box interior stays untouched")
    }

    // MARK: Crop

    func testCropOutputsTheRegionAndKeepsMarkAlignment() throws {
        let base = makeWhiteImage(width: 100, height: 100)
        let mark = AnnotationMark(
            tool: .redact, start: CGPoint(x: 25, y: 25), end: CGPoint(x: 35, y: 35))
        let result = try XCTUnwrap(MarkRenderer.flatten(
            image: base, marks: [mark],
            cropRect: CGRect(x: 20, y: 20, width: 50, height: 40)))
        XCTAssertEqual(result.width, 50)
        XCTAssertEqual(result.height, 40)
        let bytes = rgbaBytes(of: result)
        // (30, 30) in image space is (10, 10) in crop space.
        let inside = pixel(bytes, width: 50, x: 10, yFromTop: 10)
        XCTAssertLessThan(inside.r, 10)
    }

    func testDegenerateCropReturnsNil() {
        let base = makeWhiteImage(width: 10, height: 10)
        XCTAssertNil(MarkRenderer.flatten(image: base, marks: [], cropRect: .zero))
    }

    // MARK: PNG

    func testPngDataHasThePngMagicBytes() throws {
        let image = makeWhiteImage(width: 4, height: 4)
        let data = try XCTUnwrap(MarkRenderer.pngData(from: image))
        XCTAssertEqual([UInt8](data.prefix(4)), [0x89, 0x50, 0x4E, 0x47])
    }
}
