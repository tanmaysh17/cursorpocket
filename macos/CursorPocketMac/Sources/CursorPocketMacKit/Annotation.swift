import Foundation
import CoreGraphics

// MARK: Tools and marks

public enum AnnotationTool: String, CaseIterable, Equatable, Sendable {
    case select
    case arrow
    case line
    case box
    case ellipse
    case freehand
    case highlight
    case text
    case redact
    case marker

    /// Editor accelerator key for the tool. These are page-level keys, never
    /// global hotkeys — the editor can take focus, so they cannot leak into
    /// another application. ("c" is reserved by the editor for crop.)
    public var acceleratorKey: Character {
        switch self {
        case .select: return "v"
        case .arrow: return "a"
        case .line: return "l"
        case .box: return "b"
        case .ellipse: return "e"
        case .freehand: return "f"
        case .highlight: return "h"
        case .text: return "t"
        case .redact: return "r"
        case .marker: return "m"
        }
    }

    /// Stroke width a freshly armed tool draws with. The highlighter is a
    /// wide band by design; everything else is a 3 pt line.
    public var defaultStrokeWidth: CGFloat {
        self == .highlight ? 14 : 3
    }

    public static func tool(forAccelerator key: Character) -> AnnotationTool? {
        allCases.first { $0.acceleratorKey == Character(key.lowercased()) }
    }
}

public struct AnnotationMark: Identifiable, Equatable, Sendable {
    public let id: UUID
    public var tool: AnnotationTool
    public var start: CGPoint
    public var end: CGPoint
    public var path: [CGPoint]
    public var text: String
    public var colorIndex: Int
    public var strokeWidth: CGFloat

    public init(
        id: UUID = UUID(),
        tool: AnnotationTool,
        start: CGPoint,
        end: CGPoint,
        path: [CGPoint] = [],
        text: String = "",
        colorIndex: Int = 0,
        strokeWidth: CGFloat = 3
    ) {
        self.id = id
        self.tool = tool
        self.start = start
        self.end = end
        self.path = path
        self.text = text
        self.colorIndex = colorIndex
        self.strokeWidth = strokeWidth
    }

    public var bounds: CGRect {
        if tool == .marker {
            // Selection/hit halo only — the rendered radius is sized off the
            // image; 12 is the minimum, so the halo never overstates a stamp.
            let radius: CGFloat = 12
            return CGRect(
                x: start.x - radius, y: start.y - radius,
                width: radius * 2, height: radius * 2)
        }
        if tool == .freehand, !path.isEmpty {
            var minX = path[0].x, minY = path[0].y, maxX = path[0].x, maxY = path[0].y
            for point in path {
                minX = min(minX, point.x); minY = min(minY, point.y)
                maxX = max(maxX, point.x); maxY = max(maxY, point.y)
            }
            return CGRect(x: minX, y: minY, width: maxX - minX, height: maxY - minY)
        }
        return RegionSelection.rect(from: start, to: end)
    }

    public func hitTest(_ point: CGPoint, tolerance: CGFloat = 8) -> Bool {
        bounds.insetBy(dx: -tolerance, dy: -tolerance).contains(point)
    }
}

// MARK: Palette

/// Mark colors as sRGB hex. Index 0 is the red annotation default; the
/// redaction color is fixed solid black and deliberately not a choice —
/// derivative styles (pixelate, blur) can leak short strings back out.
public enum AnnotationPalette {
    public static let markColors: [String] = [
        "#FF5A67", "#43E08D", "#4DA3FF", "#FFD54D", "#FFFFFF", "#101314",
    ]

    public static let redactionColor = "#000000"

    public static func color(at index: Int) -> String {
        markColors[max(0, min(index, markColors.count - 1))]
    }

    /// Parses "#RRGGBB" into 0...1 components. Returns opaque black on any
    /// malformed input so rendering never gets an undefined color.
    public static func components(of hex: String) -> (red: CGFloat, green: CGFloat, blue: CGFloat) {
        var value = hex
        if value.hasPrefix("#") { value.removeFirst() }
        guard value.count == 6, let number = UInt32(value, radix: 16) else { return (0, 0, 0) }
        return (
            CGFloat((number >> 16) & 0xFF) / 255,
            CGFloat((number >> 8) & 0xFF) / 255,
            CGFloat(number & 0xFF) / 255)
    }
}

// MARK: Highlighter

/// The highlighter draws with multiply blending so dark glyphs underneath
/// stay dark — keeping the text legible is the point of a highlight. Both
/// renderers (CoreGraphics flatten and the live SwiftUI canvas) must use
/// these same constants or the saved file would not match the editor.
public enum AnnotationHighlightStyle {
    public static let opacity: CGFloat = 0.35
}

// MARK: Step markers

/// Step-marker numbers are derived from the mark's position among the marker
/// marks, never stored: delete marker 2 and the ones after it renumber
/// automatically, and undo/redo needs no separate counter to rewind.
public enum MarkerNumbering {
    /// 1-based number of `mark` among the marker marks in `marks`; nil for
    /// non-marker marks or a mark that is not in the array.
    public static func number(of mark: AnnotationMark, in marks: [AnnotationMark]) -> Int? {
        guard mark.tool == .marker else { return nil }
        var number = 0
        for candidate in marks where candidate.tool == .marker {
            number += 1
            if candidate.id == mark.id { return number }
        }
        return nil
    }

    public static func next(in marks: [AnnotationMark]) -> Int {
        marks.reduce(0) { $0 + ($1.tool == .marker ? 1 : 0) } + 1
    }

    /// Radius sized off the image's short edge, like every other weight:
    /// a fixed radius is a dot on a 4K shot and a blot on a small region
    /// capture. Mirrors the Windows `MarkerNumbering.RadiusFor` default step.
    public static func radius(forImageShortEdge shortEdge: CGFloat) -> CGFloat {
        min(max(max(1, shortEdge) * 0.032, 12), 72)
    }
}

// MARK: Geometry

public enum AnnotationGeometry {
    /// The two barb points of an arrowhead for a shaft from `start` to `end`.
    public static func arrowHead(
        from start: CGPoint, to end: CGPoint, length: CGFloat = 14, spreadDegrees: CGFloat = 28
    ) -> (left: CGPoint, right: CGPoint) {
        let angle = atan2(end.y - start.y, end.x - start.x)
        let spread = spreadDegrees * .pi / 180
        let left = CGPoint(
            x: end.x - length * cos(angle - spread),
            y: end.y - length * sin(angle - spread))
        let right = CGPoint(
            x: end.x - length * cos(angle + spread),
            y: end.y - length * sin(angle + spread))
        return (left, right)
    }
}

// MARK: Editor policy

/// `Escape` in the editor is two-stage: an armed creation tool returns to
/// Select, and `Escape` from Select closes keeping the original. `Enter`
/// saves with or without marks.
public enum AnnotationEscapeAction: Equatable {
    case disarmToSelect
    case closeKeepingOriginal
}

public enum AnnotationEditorPolicy {
    public static func escapeAction(armedTool: AnnotationTool) -> AnnotationEscapeAction {
        armedTool == .select ? .closeKeepingOriginal : .disarmToSelect
    }

    /// Status-strip hint after the first Escape press, so the two-stage
    /// behavior is discoverable.
    public static let disarmedStatus = "Tool cleared — press Escape again to close and keep the original"

    /// A text block whose editing ends still empty was never content — keep
    /// it and Save would silently flatten an invisible mark into the file.
    public static func shouldDiscardTextMark(text: String) -> Bool {
        text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
    }
}

// MARK: Save target

/// Marks overwrite the capture in place; a crop deletes pixels, and a save
/// overwrites rather than deleting, so there would be no Recycle-Bin/Trash
/// copy to fall back on — it must write a new capture instead.
public enum AnnotationSaveTarget: Equatable {
    case overwriteInPlace
    case newCapture

    public static func forOperation(cropsPixels: Bool) -> AnnotationSaveTarget {
        cropsPixels ? .newCapture : .overwriteInPlace
    }
}

// MARK: History

public struct AnnotationHistory: Equatable {
    private var undoStack: [[AnnotationMark]] = []
    private var redoStack: [[AnnotationMark]] = []

    public init() {}

    public var canUndo: Bool { !undoStack.isEmpty }
    public var canRedo: Bool { !redoStack.isEmpty }

    /// Record the state *before* a change is applied.
    public mutating func recordChange(from marks: [AnnotationMark]) {
        undoStack.append(marks)
        redoStack.removeAll()
    }

    public mutating func undo(current: [AnnotationMark]) -> [AnnotationMark]? {
        guard let previous = undoStack.popLast() else { return nil }
        redoStack.append(current)
        return previous
    }

    public mutating func redo(current: [AnnotationMark]) -> [AnnotationMark]? {
        guard let next = redoStack.popLast() else { return nil }
        undoStack.append(current)
        return next
    }
}
