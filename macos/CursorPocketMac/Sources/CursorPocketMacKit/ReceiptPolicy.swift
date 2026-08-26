import Foundation
import CoreGraphics

/// Actions a capture receipt can offer. `annotate` applies to screenshots
/// only; everything else applies to every kind.
public enum ReceiptAction: CaseIterable, Equatable, Sendable {
    case open
    case reveal
    case annotate
    case dismiss

    public var title: String {
        switch self {
        case .open: return "Open"
        case .reveal: return "Reveal"
        case .annotate: return "Annotate"
        case .dismiss: return "Dismiss"
        }
    }

    /// The mnemonic behind the receipt's Control+Option key access.
    public var key: Character {
        switch self {
        case .open: return "o"
        case .reveal: return "r"
        case .annotate: return "a"
        case .dismiss: return "d"
        }
    }
}

/// Decision logic for the capture receipt: which actions a kind offers, how
/// long the receipt lingers, where it lands, and which keys reach it. A
/// receipt does NOT own the user's attention — they carry on working while it
/// is up — so every key it honors must carry Control+Option; a bare key would
/// steal ordinary typing.
public enum ReceiptPolicy {
    /// The receipt dismisses itself after this long with no interaction.
    public static let autoDismissSeconds: Double = 6

    /// Bottom-right margin between the receipt and the screen's free space.
    public static let margin: CGFloat = 16

    public static func actions(for kind: CaptureKind) -> [ReceiptAction] {
        var actions: [ReceiptAction] = [.open, .reveal]
        if kind == .screenshot {
            actions.append(.annotate)
        }
        actions.append(.dismiss)
        return actions
    }

    /// Bottom-right corner of the visible frame (Cocoa coordinates, origin
    /// bottom-left). An empty frame yields `.zero` so a headless call cannot
    /// push the panel off-screen.
    public static func origin(inVisibleFrame frame: CGRect, panelSize: CGSize) -> CGPoint {
        guard frame.width > 0, frame.height > 0 else { return .zero }
        return CGPoint(
            x: max(frame.minX, frame.maxX - margin - panelSize.width),
            y: frame.minY + margin)
    }

    /// Maps a keypress to a receipt action. Returns nil without the full
    /// Control+Option chord, and nil for actions the kind does not offer —
    /// this is the invariant that keeps a lingering receipt from swallowing
    /// bare keys.
    public static func action(
        forKey key: Character,
        kind: CaptureKind,
        hasControlOption: Bool
    ) -> ReceiptAction? {
        guard hasControlOption else { return nil }
        // Some characters lowercase to more than one character; those can
        // never be a mnemonic.
        guard key.lowercased().count == 1, let lowered = key.lowercased().first else { return nil }
        return actions(for: kind).first { $0.key == lowered }
    }
}
