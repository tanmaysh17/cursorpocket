import AppKit
import SwiftUI

/// Brand colors. Green means ready/saved/primary; red means recording,
/// discard, destructive. Never decorative — and recording state is always
/// conveyed by text as well, never by color alone.
enum Theme {
    static let ready = Color(red: 0x43 / 255, green: 0xE0 / 255, blue: 0x8D / 255)
    static let alert = Color(red: 0xFF / 255, green: 0x5A / 255, blue: 0x67 / 255)
    static let pine = Color(red: 7 / 255, green: 19 / 255, blue: 15 / 255)

    static let readyNS = NSColor(red: 0x43 / 255, green: 0xE0 / 255, blue: 0x8D / 255, alpha: 1)
    static let alertNS = NSColor(red: 0xFF / 255, green: 0x5A / 255, blue: 0x67 / 255, alpha: 1)
}
