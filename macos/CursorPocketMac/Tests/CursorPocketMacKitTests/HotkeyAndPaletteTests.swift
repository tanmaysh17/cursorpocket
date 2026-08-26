import CoreGraphics
import Foundation
import XCTest
@testable import CursorPocketMacKit

final class HotkeyTests: XCTestCase {
    func testEveryGlobalDefaultCarriesModifiers() {
        // A bare global key would steal ordinary typing from every
        // application. Only palette-visible keys may be bare, and those are
        // not registered through HotkeyDefaults at all.
        for action in HotkeyAction.allCases {
            let spec = HotkeyDefaults.spec(for: action)
            XCTAssertFalse(spec.modifiers.isEmpty, "\(action) has a bare global hotkey")
            XCTAssertTrue(spec.modifiers.contains(.control) || spec.modifiers.contains(.command))
        }
    }

    func testNoTwoActionsShareAChord() {
        let chords = HotkeyAction.allCases.map { action -> String in
            let spec = HotkeyDefaults.spec(for: action)
            return "\(spec.keyCode)+\(spec.modifiers.rawValue)"
        }
        XCTAssertEqual(Set(chords).count, chords.count)
    }

    func testCarbonFlagsMatchTheHIToolboxMasks() {
        XCTAssertEqual(HotkeyModifiers.command.carbonFlags, 0x0100)
        XCTAssertEqual(HotkeyModifiers.shift.carbonFlags, 0x0200)
        XCTAssertEqual(HotkeyModifiers.option.carbonFlags, 0x0800)
        XCTAssertEqual(HotkeyModifiers.control.carbonFlags, 0x1000)
        XCTAssertEqual(HotkeyModifiers([.control, .option]).carbonFlags, 0x1800)
    }

    func testDisplayStringOrdersModifiersConventionaly() {
        let spec = HotkeySpec(keyCode: 1, modifiers: [.command, .control, .shift, .option], keyLabel: "S")
        XCTAssertEqual(spec.displayString, "⌃⌥⇧⌘S")
    }
}

final class PaletteTests: XCTestCase {
    func testMnemonicsAreUniqueAndResolveBothCases() {
        var seen = Set<Character>()
        for command in PaletteCommand.allCases {
            XCTAssertFalse(seen.contains(command.mnemonic))
            seen.insert(command.mnemonic)
            XCTAssertEqual(PaletteCommand.command(forKey: command.mnemonic), command)
            XCTAssertEqual(
                PaletteCommand.command(forKey: Character(String(command.mnemonic).uppercased())), command)
        }
        XCTAssertNil(PaletteCommand.command(forKey: "q"))
    }

    func testPaletteCoversTheSixCommandKeys() {
        XCTAssertEqual(
            Set(PaletteCommand.allCases.map { String($0.mnemonic) }),
            ["s", "v", "a", "t", "l", "o"])
    }

    func testPlacementNormalizationRepairsBadFractions() {
        XCTAssertEqual(
            PalettePlacement(xFraction: -2, yFraction: 7).normalized(),
            PalettePlacement(xFraction: 0, yFraction: 1))
        let nan = PalettePlacement(xFraction: .nan, yFraction: .infinity).normalized()
        XCTAssertEqual(nan.xFraction, 0.5)
        XCTAssertEqual(nan.yFraction, 0.72)
    }

    func testPlacementRoundTripsThroughAnOrigin() {
        let free = CGRect(x: 0, y: 25, width: 1512, height: 920)
        let panel = CGSize(width: 420, height: 96)
        let placement = PalettePlacement(xFraction: 0.3, yFraction: 0.8)
        let origin = placement.origin(inFree: free, panelSize: panel)
        let recovered = PalettePlacement.fractions(forOrigin: origin, inFree: free, panelSize: panel)
        XCTAssertEqual(recovered.xFraction, 0.3, accuracy: 0.001)
        XCTAssertEqual(recovered.yFraction, 0.8, accuracy: 0.001)
    }

    func testOriginStaysInsideFreeSpaceEvenWhenPanelIsHuge() {
        let free = CGRect(x: 0, y: 0, width: 300, height: 200)
        let origin = PalettePlacement(xFraction: 1, yFraction: 1)
            .origin(inFree: free, panelSize: CGSize(width: 500, height: 400))
        XCTAssertEqual(origin, CGPoint(x: 0, y: 0))
    }
}
