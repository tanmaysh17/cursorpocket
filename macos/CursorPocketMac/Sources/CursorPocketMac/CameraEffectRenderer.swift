import AVFoundation
import CoreImage
import QuartzCore
import Vision
import CursorPocketMacKit

/// Renders camera effects into the self-view. Effects reach the recording the
/// same way the plain feed does — by being on screen — so everything here is
/// presentation: sample buffer → CIImage → optional Vision person mask →
/// CoreImage composite → color adjust → CGImage on the target layer.
///
/// Invariants ported from Windows:
/// - Degrade, never fail: a Vision error quietly disables background
///   blur/replacement for the rest of the session while color keeps working.
///   Nothing thrown here can reach the recording.
/// - Without a mask the background is left UNTOUCHED, never blurred —
///   blurring everything would erase the user.
/// - Latest-frame-wins: a busy gate taken per frame and released only after
///   the frame reaches the layer, so a slow machine drops frames instead of
///   growing latency.
final class CameraEffectRenderer: NSObject, AVCaptureVideoDataOutputSampleBufferDelegate {
    private let settings: CameraEffectSettings
    private weak var targetLayer: CALayer?
    // One CIContext for the renderer's lifetime; per-frame contexts would
    // recompile the filter graph every frame.
    private let ciContext = CIContext(options: [.cacheIntermediates: false])
    private let videoQueue = DispatchQueue(label: "app.cursorpocket.camera-effects")
    // Busy gate: value 1, taken with a zero timeout so a frame arriving while
    // the previous one is still in flight is dropped, and signaled only after
    // the previous frame's pixels are on the layer.
    private let frameGate = DispatchSemaphore(value: 1)
    // Touched only on `videoQueue`, which serializes delegate callbacks.
    private var maskUnavailable = false
    private let segmentationRequest: VNGeneratePersonSegmentationRequest

    /// Brand-dark (Theme.pine, #07130F) — the only background replacement;
    /// no bundled images.
    private static let replacementColor = CIColor(red: 7 / 255, green: 19 / 255, blue: 15 / 255)

    init(settings: CameraEffectSettings, targetLayer: CALayer) {
        self.settings = settings.clamped()
        self.targetLayer = targetLayer
        let request = VNGeneratePersonSegmentationRequest()
        request.qualityLevel = .balanced
        request.outputPixelFormat = kCVPixelFormatType_OneComponent8
        segmentationRequest = request
        super.init()
    }

    /// Adds the frame tap to the session. Returns false when the session
    /// refuses the output so the caller can fall back to the plain preview.
    func attach(to session: AVCaptureSession) -> Bool {
        let output = AVCaptureVideoDataOutput()
        output.videoSettings = [kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA]
        output.alwaysDiscardsLateVideoFrames = true
        output.setSampleBufferDelegate(self, queue: videoQueue)
        guard session.canAddOutput(output) else { return false }
        session.addOutput(output)
        return true
    }

    func captureOutput(
        _ output: AVCaptureOutput,
        didOutput sampleBuffer: CMSampleBuffer,
        from connection: AVCaptureConnection
    ) {
        guard frameGate.wait(timeout: .now()) == .success else { return }
        guard let pixelBuffer = CMSampleBufferGetImageBuffer(sampleBuffer) else {
            frameGate.signal()
            return
        }
        let rendered = autoreleasepool { render(pixelBuffer) }
        guard let rendered else {
            frameGate.signal()
            return
        }
        // Strong capture on purpose: a DispatchSemaphore must be restored to
        // its initial value before deallocation, so the in-flight frame keeps
        // the renderer alive until it signals. No cycle — the block runs once.
        DispatchQueue.main.async {
            // A standalone CALayer implicitly animates `contents` — a 0.25 s
            // crossfade per frame would smear the feed, so present directly.
            CATransaction.begin()
            CATransaction.setDisableActions(true)
            self.targetLayer?.contents = rendered
            CATransaction.commit()
            self.frameGate.signal()
        }
    }

    private func render(_ pixelBuffer: CVPixelBuffer) -> CGImage? {
        let frame = CIImage(cvPixelBuffer: pixelBuffer)
        let mask = personMask(in: pixelBuffer, scaledTo: frame.extent)
        let effective = settings.resolved(maskAvailable: mask != nil)
        var image = frame
        if effective.wantsPersonMask, let mask {
            image = compositedBackground(frame: frame, mask: mask, settings: effective) ?? frame
        }
        if effective.hasColorAdjustment {
            image = colorAdjusted(image, settings: effective)
        }
        return ciContext.createCGImage(image, from: frame.extent)
    }

    private func personMask(in pixelBuffer: CVPixelBuffer, scaledTo extent: CGRect) -> CIImage? {
        guard settings.wantsPersonMask, !maskUnavailable else { return nil }
        let handler = VNImageRequestHandler(cvPixelBuffer: pixelBuffer, options: [:])
        do {
            try handler.perform([segmentationRequest])
        } catch {
            // Degrade, never fail: background effects stay off from here on;
            // color adjustments keep working on every later frame.
            maskUnavailable = true
            return nil
        }
        // A missing result is transient (this frame is passed through with
        // its background untouched), not a reason to disable the effect.
        guard let observation = segmentationRequest.results?.first else { return nil }
        let mask = CIImage(cvPixelBuffer: observation.pixelBuffer)
        guard mask.extent.width > 0, mask.extent.height > 0 else { return nil }
        return mask.transformed(by: CGAffineTransform(
            scaleX: extent.width / mask.extent.width,
            y: extent.height / mask.extent.height))
    }

    private func compositedBackground(
        frame: CIImage,
        mask: CIImage,
        settings: CameraEffectSettings
    ) -> CIImage? {
        let background: CIImage
        if settings.backgroundReplaceEnabled {
            background = CIImage(color: Self.replacementColor).cropped(to: frame.extent)
        } else {
            // Clamp before blurring so the frame edge does not bleed to black.
            background = frame
                .clampedToExtent()
                .applyingGaussianBlur(sigma: CameraEffectSettings.blurRadius)
                .cropped(to: frame.extent)
        }
        guard let blend = CIFilter(name: "CIBlendWithMask") else { return nil }
        // Vision's mask is high where the person is, so the person keeps the
        // original pixels and only the background is replaced.
        blend.setValue(frame, forKey: kCIInputImageKey)
        blend.setValue(background, forKey: kCIInputBackgroundImageKey)
        blend.setValue(mask, forKey: kCIInputMaskImageKey)
        return blend.outputImage
    }

    private func colorAdjusted(_ image: CIImage, settings: CameraEffectSettings) -> CIImage {
        var result = image
        if settings.brightness != 0 || settings.contrast != 1,
           let controls = CIFilter(name: "CIColorControls") {
            controls.setValue(result, forKey: kCIInputImageKey)
            controls.setValue(settings.brightness, forKey: kCIInputBrightnessKey)
            controls.setValue(settings.contrast, forKey: kCIInputContrastKey)
            controls.setValue(1.0, forKey: kCIInputSaturationKey)
            result = controls.outputImage ?? result
        }
        if settings.warmth != 0, let temperature = CIFilter(name: "CITemperatureAndTint") {
            temperature.setValue(result, forKey: kCIInputImageKey)
            // Raising the assumed scene neutral above the render target
            // shifts the output warmer; lowering it shifts cooler.
            temperature.setValue(
                CIVector(
                    x: CameraEffectSettings.neutralTemperature + settings.temperatureOffset,
                    y: 0),
                forKey: "inputNeutral")
            temperature.setValue(
                CIVector(x: CameraEffectSettings.neutralTemperature, y: 0),
                forKey: "inputTargetNeutral")
            result = temperature.outputImage ?? result
        }
        return result
    }
}
