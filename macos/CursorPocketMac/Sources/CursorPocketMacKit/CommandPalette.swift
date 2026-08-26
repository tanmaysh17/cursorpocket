import Foundation
import CoreGraphics

/// The command palette's mnemonic commands. The bare keys are honored ONLY
/// while the palette is visible and key — leaving them active would steal
/// ordinary typing from other applications.
public enum PaletteCommand: String, CaseIterable, Equatable, Sendable {
    case screenshot
    case video
    case audioNote
    case textCapture
    case linkCapture
    case openLibrary

    public var mnemonic: Character {
        switch self {
        case .screenshot: return "s"
        case .video: return "v"
        case .audioNote: return "a"
        case .textCapture: return "t"
        case .linkCapture: return "l"
        case .openLibrary: return "o"
        }
    }

    public var title: String {
        switch self {
        case .screenshot: return "Screenshot"
        case .video: return "Record video"
        case .audioNote: return "Audio note"
        case .textCapture: return "Grab selected text"
        case .linkCapture: return "Save browser link"
        case .openLibrary: return "Open Library"
        }
    }

    public static func command(forKey key: Character) -> PaletteCommand? {
        let lowered = Character(key.lowercased())
        return allCases.first { $0.mnemonic == lowered }
    }
}

/// The palette position persists as fractions of the display's free space
/// rather than screen coordinates, so it survives display, resolution, and
/// DPI changes. The user positions it; nothing else may move it.
public struct PalettePlacement: Equatable, Codable, Sendable {
    public var xFraction: Double
    public var yFraction: Double

    public init(xFraction: Double = 0.5, yFraction: Double = 0.72) {
        self.xFraction = xFraction
        self.yFraction = yFraction
    }

    public func normalized() -> PalettePlacement {
        PalettePlacement(
            xFraction: xFraction.isFinite ? min(max(xFraction, 0), 1) : 0.5,
            yFraction: yFraction.isFinite ? min(max(yFraction, 0), 1) : 0.72)
    }

    /// Panel origin inside the display's usable frame.
    public func origin(inFree freeRect: CGRect, panelSize: CGSize) -> CGPoint {
        let clamped = normalized()
        let usableWidth = max(0, freeRect.width - panelSize.width)
        let usableHeight = max(0, freeRect.height - panelSize.height)
        return CGPoint(
            x: freeRect.minX + usableWidth * CGFloat(clamped.xFraction),
            y: freeRect.minY + usableHeight * CGFloat(clamped.yFraction))
    }

    /// The inverse: fractions from a dragged panel origin.
    public static func fractions(forOrigin origin: CGPoint, inFree freeRect: CGRect, panelSize: CGSize) -> PalettePlacement {
        let usableWidth = max(1, freeRect.width - panelSize.width)
        let usableHeight = max(1, freeRect.height - panelSize.height)
        return PalettePlacement(
            xFraction: Double((origin.x - freeRect.minX) / usableWidth),
            yFraction: Double((origin.y - freeRect.minY) / usableHeight)
        ).normalized()
    }
}
