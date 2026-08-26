import Foundation
import XCTest
@testable import CursorPocketMacKit

final class CameraEffectSettingsTests: XCTestCase {
    func testEveryEffectDefaultsOff() {
        let settings = CameraEffectSettings()
        XCTAssertFalse(settings.backgroundBlurEnabled)
        XCTAssertFalse(settings.backgroundReplaceEnabled)
        XCTAssertEqual(settings.brightness, 0)
        XCTAssertEqual(settings.contrast, 1)
        XCTAssertEqual(settings.warmth, 0)
        XCTAssertTrue(settings.allOff)
        // The default state must keep the untouched preview-layer path.
        XCTAssertFalse(CameraEffectSettings.usesFramePipeline(settings))
    }

    func testAnySingleEffectSwitchesToTheFramePipeline() {
        XCTAssertTrue(CameraEffectSettings.usesFramePipeline(
            CameraEffectSettings(backgroundBlurEnabled: true)))
        XCTAssertTrue(CameraEffectSettings.usesFramePipeline(
            CameraEffectSettings(backgroundReplaceEnabled: true)))
        XCTAssertTrue(CameraEffectSettings.usesFramePipeline(
            CameraEffectSettings(brightness: 0.1)))
        XCTAssertTrue(CameraEffectSettings.usesFramePipeline(
            CameraEffectSettings(contrast: 1.1)))
        XCTAssertTrue(CameraEffectSettings.usesFramePipeline(
            CameraEffectSettings(warmth: -0.4)))
    }

    func testClampRepairsOutOfRangeValues() {
        let clamped = CameraEffectSettings(brightness: 5, contrast: -3, warmth: 9).clamped()
        XCTAssertEqual(clamped.brightness, 0.5)
        XCTAssertEqual(clamped.contrast, 0.75)
        XCTAssertEqual(clamped.warmth, 1)
    }

    func testClampRepairsCorruptValuesToNeutral() {
        let clamped = CameraEffectSettings(
            brightness: .nan, contrast: .infinity, warmth: .nan).clamped()
        XCTAssertEqual(clamped.brightness, 0)
        XCTAssertEqual(clamped.contrast, 1)
        XCTAssertEqual(clamped.warmth, 0)
        XCTAssertTrue(clamped.allOff)
    }

    func testClampLeavesInRangeValuesAlone() {
        let settings = CameraEffectSettings(
            backgroundBlurEnabled: true, brightness: -0.25, contrast: 1.2, warmth: 0.5)
        XCTAssertEqual(settings.clamped(), settings)
    }

    func testBlurWinsWhenBothBackgroundEffectsPersist() {
        let clamped = CameraEffectSettings(
            backgroundBlurEnabled: true, backgroundReplaceEnabled: true).clamped()
        XCTAssertTrue(clamped.backgroundBlurEnabled)
        XCTAssertFalse(clamped.backgroundReplaceEnabled)
    }

    func testMaskUnavailableDegradesBackgroundEffectsAndKeepsColor() {
        let settings = CameraEffectSettings(
            backgroundBlurEnabled: true, brightness: 0.2, contrast: 1.1, warmth: -0.3)
        let degraded = settings.resolved(maskAvailable: false)
        XCTAssertFalse(degraded.backgroundBlurEnabled)
        XCTAssertFalse(degraded.backgroundReplaceEnabled)
        XCTAssertFalse(degraded.wantsPersonMask)
        XCTAssertEqual(degraded.brightness, 0.2)
        XCTAssertEqual(degraded.contrast, 1.1)
        XCTAssertEqual(degraded.warmth, -0.3)
        XCTAssertTrue(degraded.hasColorAdjustment)
    }

    func testMaskAvailableKeepsBackgroundEffects() {
        let settings = CameraEffectSettings(backgroundReplaceEnabled: true)
        let resolved = settings.resolved(maskAvailable: true)
        XCTAssertTrue(resolved.backgroundReplaceEnabled)
        XCTAssertTrue(resolved.wantsPersonMask)
    }

    func testWarmthMapsLinearlyToTemperatureOffset() {
        XCTAssertEqual(CameraEffectSettings(warmth: 0).temperatureOffset, 0)
        XCTAssertEqual(
            CameraEffectSettings(warmth: 1).temperatureOffset,
            CameraEffectSettings.warmthTemperatureSpan)
        XCTAssertEqual(
            CameraEffectSettings(warmth: -0.5).temperatureOffset,
            -CameraEffectSettings.warmthTemperatureSpan / 2)
    }
}
