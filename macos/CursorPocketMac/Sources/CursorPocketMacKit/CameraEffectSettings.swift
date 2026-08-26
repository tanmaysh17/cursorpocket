import Foundation

/// Camera self-view effects, mirroring the Windows rules: every effect
/// defaults OFF; with everything off the self-view keeps the plain preview
/// path; effects degrade rather than fail — without a person mask the
/// background stays UNTOUCHED (blurring everything would erase the user)
/// while color adjustments keep working.
public struct CameraEffectSettings: Equatable, Sendable {
    /// Blur the background behind the person. Requires a person mask.
    public var backgroundBlurEnabled: Bool
    /// Replace the background with the solid brand-dark color. Requires a
    /// person mask. Mutually exclusive with blur; blur wins if both persist.
    public var backgroundReplaceEnabled: Bool
    /// Additive brightness, 0 neutral.
    public var brightness: Double
    /// Multiplicative contrast, 1 neutral.
    public var contrast: Double
    /// -1 (cool) ... 1 (warm), 0 neutral. Maps to a white-point temperature
    /// offset via `temperatureOffset`.
    public var warmth: Double

    public static let brightnessRange: ClosedRange<Double> = -0.5...0.5
    public static let contrastRange: ClosedRange<Double> = 0.75...1.25
    public static let warmthRange: ClosedRange<Double> = -1...1

    /// Fixed background blur radius in pixels at the (≤ 480 px) capture size.
    public static let blurRadius: Double = 18
    /// Reference white point in kelvin; `warmth` moves the scene's assumed
    /// neutral away from it, so positive warmth renders warmer (orange).
    public static let neutralTemperature: Double = 6500
    /// Kelvin swing at |warmth| == 1.
    public static let warmthTemperatureSpan: Double = 1500

    public init(
        backgroundBlurEnabled: Bool = false,
        backgroundReplaceEnabled: Bool = false,
        brightness: Double = 0,
        contrast: Double = 1,
        warmth: Double = 0
    ) {
        self.backgroundBlurEnabled = backgroundBlurEnabled
        self.backgroundReplaceEnabled = backgroundReplaceEnabled
        self.brightness = brightness
        self.contrast = contrast
        self.warmth = warmth
    }

    /// True only when every effect is at its default — the state in which the
    /// self-view MUST use the untouched preview-layer path.
    public var allOff: Bool {
        !backgroundBlurEnabled && !backgroundReplaceEnabled
            && brightness == 0 && contrast == 1 && warmth == 0
    }

    public var wantsPersonMask: Bool { backgroundBlurEnabled || backgroundReplaceEnabled }

    public var hasColorAdjustment: Bool { brightness != 0 || contrast != 1 || warmth != 0 }

    /// White-point offset in kelvin the renderer adds to the scene's assumed
    /// neutral. Positive warmth raises the assumed neutral, which shifts the
    /// rendered image toward orange.
    public var temperatureOffset: Double { warmth * Self.warmthTemperatureSpan }

    /// The single place persisted or UI-fed values are repaired: ranges are
    /// clamped and the blur/replace exclusivity is resolved (blur wins).
    public func clamped() -> CameraEffectSettings {
        var result = self
        result.brightness = Self.clamp(brightness, to: Self.brightnessRange, neutral: 0)
        result.contrast = Self.clamp(contrast, to: Self.contrastRange, neutral: 1)
        result.warmth = Self.clamp(warmth, to: Self.warmthRange, neutral: 0)
        if result.backgroundBlurEnabled && result.backgroundReplaceEnabled {
            result.backgroundReplaceEnabled = false
        }
        return result
    }

    /// True when the self-view must run the frame pipeline instead of the
    /// plain preview layer. Decided on the clamped values so an out-of-range
    /// persisted number that clamps back to neutral stays on the safe path.
    public static func usesFramePipeline(_ settings: CameraEffectSettings) -> Bool {
        !settings.clamped().allOff
    }

    /// Degraded mode: with no person mask the background effects quietly turn
    /// off — leaving the background untouched — while color keeps working.
    public func resolved(maskAvailable: Bool) -> CameraEffectSettings {
        var result = clamped()
        if !maskAvailable {
            result.backgroundBlurEnabled = false
            result.backgroundReplaceEnabled = false
        }
        return result
    }

    private static func clamp(_ value: Double, to range: ClosedRange<Double>, neutral: Double) -> Double {
        // A corrupt (non-finite) persisted value repairs to neutral, never to
        // a range edge that would silently enable the frame pipeline.
        guard value.isFinite else { return neutral }
        return min(max(value, range.lowerBound), range.upperBound)
    }
}
