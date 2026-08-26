import CoreGraphics
import Foundation
import XCTest
@testable import CursorPocketMacKit

final class AnnotationTests: XCTestCase {
    func testEscapeIsTwoStage() {
        // An armed creation tool returns to Select…
        XCTAssertEqual(AnnotationEditorPolicy.escapeAction(armedTool: .arrow), .disarmToSelect)
        XCTAssertEqual(AnnotationEditorPolicy.escapeAction(armedTool: .redact), .disarmToSelect)
        // …and Escape from Select closes keeping the original.
        XCTAssertEqual(AnnotationEditorPolicy.escapeAction(armedTool: .select), .closeKeepingOriginal)
    }

    func testMarksOverwriteButCropsWriteANewCapture() {
        XCTAssertEqual(AnnotationSaveTarget.forOperation(cropsPixels: false), .overwriteInPlace)
        XCTAssertEqual(AnnotationSaveTarget.forOperation(cropsPixels: true), .newCapture)
    }

    func testRedactionIsSolidBlackAndNotAPaletteChoice() {
        // Pixelation and blur derive output from the pixels underneath and can
        // leak short strings back out, so redaction is a solid fill only.
        XCTAssertEqual(AnnotationPalette.redactionColor, "#000000")
        XCTAssertFalse(
            AnnotationPalette.markColors.contains(AnnotationPalette.redactionColor),
            "Redaction color is fixed, never a palette choice")
        let components = AnnotationPalette.components(of: AnnotationPalette.redactionColor)
        XCTAssertEqual(components.red, 0)
        XCTAssertEqual(components.green, 0)
        XCTAssertEqual(components.blue, 0)
    }

    func testPaletteParsingIsDefensive() {
        let brand = AnnotationPalette.components(of: "#43E08D")
        XCTAssertEqual(brand.red, 0x43 / 255, accuracy: 0.001)
        XCTAssertEqual(brand.green, 0xE0 / 255, accuracy: 0.001)
        XCTAssertEqual(brand.blue, 0x8D / 255, accuracy: 0.001)
        let junk = AnnotationPalette.components(of: "oops")
        XCTAssertEqual(junk.red, 0)
        XCTAssertEqual(AnnotationPalette.color(at: 999), AnnotationPalette.markColors.last)
        XCTAssertEqual(AnnotationPalette.color(at: -1), AnnotationPalette.markColors.first)
    }

    func testEveryToolHasAUniqueAcceleratorMapping() {
        // Declaring a key and mapping it are two edits, and missing the second
        // is silent — so assert every declared tool resolves back to itself.
        var seen = Set<Character>()
        for tool in AnnotationTool.allCases {
            let key = tool.acceleratorKey
            XCTAssertFalse(seen.contains(key), "Duplicate accelerator \(key)")
            seen.insert(key)
            XCTAssertEqual(AnnotationTool.tool(forAccelerator: key), tool)
            XCTAssertEqual(AnnotationTool.tool(forAccelerator: Character(String(key).uppercased())), tool)
        }
    }

    func testHighlighterAndMarkerAccelerators() {
        // "c" is reserved by the editor for crop, so neither new tool may
        // claim it; uniqueness against the other tools is asserted above.
        XCTAssertEqual(AnnotationTool.highlight.acceleratorKey, "h")
        XCTAssertEqual(AnnotationTool.marker.acceleratorKey, "m")
        XCTAssertFalse(AnnotationTool.allCases.contains { $0.acceleratorKey == "c" })
    }

    func testHighlighterDrawsAWideTranslucentBand() {
        XCTAssertEqual(AnnotationTool.highlight.defaultStrokeWidth, 14)
        XCTAssertEqual(AnnotationTool.box.defaultStrokeWidth, 3)
        XCTAssertEqual(AnnotationTool.marker.defaultStrokeWidth, 3)
        // Fully opaque would hide the text a highlight exists to emphasize;
        // fully transparent would be invisible.
        XCTAssertGreaterThan(AnnotationHighlightStyle.opacity, 0)
        XCTAssertLessThan(AnnotationHighlightStyle.opacity, 1)
    }

    func testNewToolsAreCreationToolsForEscape() {
        XCTAssertEqual(AnnotationEditorPolicy.escapeAction(armedTool: .highlight), .disarmToSelect)
        XCTAssertEqual(AnnotationEditorPolicy.escapeAction(armedTool: .marker), .disarmToSelect)
    }

    func testMarkerNumbersDeriveFromOrderAndRenumberOnDelete() {
        let first = AnnotationMark(tool: .marker, start: CGPoint(x: 10, y: 10), end: CGPoint(x: 10, y: 10))
        let box = AnnotationMark(tool: .box, start: .zero, end: CGPoint(x: 5, y: 5))
        let second = AnnotationMark(tool: .marker, start: CGPoint(x: 20, y: 20), end: CGPoint(x: 20, y: 20))
        let third = AnnotationMark(tool: .marker, start: CGPoint(x: 30, y: 30), end: CGPoint(x: 30, y: 30))
        var marks = [first, box, second, third]

        XCTAssertEqual(MarkerNumbering.number(of: first, in: marks), 1)
        XCTAssertNil(MarkerNumbering.number(of: box, in: marks), "Only marker marks carry a number")
        XCTAssertEqual(MarkerNumbering.number(of: second, in: marks), 2)
        XCTAssertEqual(MarkerNumbering.number(of: third, in: marks), 3)
        XCTAssertEqual(MarkerNumbering.next(in: marks), 4)

        // Deleting marker 2 renumbers automatically — the number is the
        // position among marker marks, never stored in the mark.
        marks.removeAll { $0.id == second.id }
        XCTAssertEqual(MarkerNumbering.number(of: first, in: marks), 1)
        XCTAssertEqual(MarkerNumbering.number(of: third, in: marks), 2)
        XCTAssertEqual(MarkerNumbering.next(in: marks), 3)

        XCTAssertNil(MarkerNumbering.number(of: second, in: marks), "A removed mark has no number")
        XCTAssertEqual(MarkerNumbering.next(in: []), 1)
    }

    func testMarkerRadiusScalesWithTheShortEdgeAndClamps() {
        XCTAssertEqual(MarkerNumbering.radius(forImageShortEdge: 100), 12, "Clamped to the floor")
        XCTAssertEqual(MarkerNumbering.radius(forImageShortEdge: 1000), 32, accuracy: 0.001)
        XCTAssertEqual(MarkerNumbering.radius(forImageShortEdge: 10000), 72, "Clamped to the ceiling")
        XCTAssertEqual(MarkerNumbering.radius(forImageShortEdge: 0), 12, "Degenerate size never crashes")
    }

    func testMarkerBoundsGiveASelectionHitTarget() {
        let mark = AnnotationMark(tool: .marker, start: CGPoint(x: 50, y: 50), end: CGPoint(x: 50, y: 50))
        XCTAssertEqual(mark.bounds, CGRect(x: 38, y: 38, width: 24, height: 24))
        XCTAssertTrue(mark.hitTest(CGPoint(x: 50, y: 50)))
        XCTAssertFalse(mark.hitTest(CGPoint(x: 100, y: 100)))
    }

    func testFreehandBoundsCoverThePath() {
        let mark = AnnotationMark(
            tool: .freehand, start: .zero, end: .zero,
            path: [CGPoint(x: 10, y: 40), CGPoint(x: 30, y: 5), CGPoint(x: 22, y: 18)])
        XCTAssertEqual(mark.bounds, CGRect(x: 10, y: 5, width: 20, height: 35))
        XCTAssertTrue(mark.hitTest(CGPoint(x: 12, y: 10)))
        XCTAssertFalse(mark.hitTest(CGPoint(x: 200, y: 200)))
    }

    func testArrowHeadBarbsSitBehindTheTip() {
        let head = AnnotationGeometry.arrowHead(
            from: CGPoint(x: 0, y: 0), to: CGPoint(x: 100, y: 0), length: 10, spreadDegrees: 30)
        XCTAssertLessThan(head.left.x, 100)
        XCTAssertLessThan(head.right.x, 100)
        XCTAssertEqual(head.left.y, -head.right.y, accuracy: 0.0001)
    }

    func testHistoryUndoRedo() {
        var history = AnnotationHistory()
        let one = [AnnotationMark(tool: .box, start: .zero, end: CGPoint(x: 5, y: 5))]
        let two = one + [AnnotationMark(tool: .line, start: .zero, end: CGPoint(x: 9, y: 9))]

        history.recordChange(from: [])
        history.recordChange(from: one)
        XCTAssertEqual(history.undo(current: two), one)
        XCTAssertEqual(history.redo(current: one), two)
        XCTAssertEqual(history.undo(current: two), one)
        XCTAssertEqual(history.undo(current: one), [])
        XCTAssertNil(history.undo(current: []))
    }

    func testNewChangeClearsRedo() {
        var history = AnnotationHistory()
        history.recordChange(from: [])
        let one = [AnnotationMark(tool: .box, start: .zero, end: CGPoint(x: 5, y: 5))]
        _ = history.undo(current: one)
        XCTAssertTrue(history.canRedo)
        history.recordChange(from: [])
        XCTAssertFalse(history.canRedo)
    }
}
