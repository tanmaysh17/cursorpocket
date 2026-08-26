import AVFoundation
import AppKit
import CursorPocketMacKit

/// The camera self-view: a floating, draggable panel showing the live webcam.
/// It reaches the recording by being on screen inside the captured rectangle —
/// it is deliberately NOT excluded from capture, never takes key focus, and is
/// clamped so it cannot wander outside the recording. The device is released
/// the moment recording stops so the next preview finds it free.
final class CameraSelfViewController: NSObject, NSWindowDelegate {
    private var panel: NSPanel?
    private var session: AVCaptureSession?
    private var effectRenderer: CameraEffectRenderer?
    private var recordedRectCocoa: CGRect = .zero

    var windowNumber: Int? { panel?.windowNumber }

    func show(
        recordedRectCG: CGRect,
        shape: CameraSelfViewShape,
        effects: CameraEffectSettings = CameraEffectSettings()
    ) {
        hide()
        let recordedCocoa = CoordinateSpaces.cocoaRect(fromCG: recordedRectCG)
        recordedRectCocoa = recordedCocoa
        let placementCG = CameraSelfViewPlacement.compute(recordedRect: recordedRectCG, shape: shape)
        guard placementCG != .zero else { return }
        let frame = CoordinateSpaces.cocoaRect(fromCG: placementCG)

        let panel = NSPanel(
            contentRect: frame,
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false)
        panel.level = .floating
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.isMovableByWindowBackground = true
        panel.hidesOnDeactivate = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        // On screen means in the file; nothing here may opt out of capture.
        panel.sharingType = .readOnly
        panel.delegate = self

        let container = NSView(frame: NSRect(origin: .zero, size: frame.size))
        container.wantsLayer = true
        container.layer?.cornerRadius = shape == .squircle ? frame.width * 0.28 : 14
        container.layer?.masksToBounds = true
        container.layer?.backgroundColor = NSColor.black.cgColor

        let session = AVCaptureSession()
        session.sessionPreset = .medium
        if let device = AVCaptureDevice.default(for: .video),
           let input = try? AVCaptureDeviceInput(device: device),
           session.canAddInput(input) {
            session.addInput(input)
        }
        // With every effect off the plain preview layer runs untouched — the
        // no-effects case must carry zero new risk, so it never routes
        // through the frame pipeline. And an effect that cannot attach falls
        // back to the same plain preview: effects degrade, never fail.
        var effectsAttached = false
        if CameraEffectSettings.usesFramePipeline(effects) {
            let frameLayer = CALayer()
            frameLayer.frame = container.bounds
            frameLayer.contentsGravity = .resizeAspectFill
            frameLayer.masksToBounds = true
            frameLayer.autoresizingMask = [.layerWidthSizable, .layerHeightSizable]
            frameLayer.backgroundColor = NSColor.black.cgColor
            let renderer = CameraEffectRenderer(settings: effects, targetLayer: frameLayer)
            if renderer.attach(to: session) {
                container.layer?.addSublayer(frameLayer)
                effectRenderer = renderer
                effectsAttached = true
            }
        }
        if !effectsAttached {
            let preview = AVCaptureVideoPreviewLayer(session: session)
            preview.frame = container.bounds
            preview.videoGravity = .resizeAspectFill
            preview.autoresizingMask = [.layerWidthSizable, .layerHeightSizable]
            container.layer?.addSublayer(preview)
        }
        session.startRunning()

        panel.contentView = container
        panel.orderFrontRegardless()
        self.panel = panel
        self.session = session
    }

    func hide() {
        session?.stopRunning()
        session = nil
        effectRenderer = nil
        panel?.orderOut(nil)
        panel = nil
    }

    func windowDidMove(_ notification: Notification) {
        guard let panel, recordedRectCocoa != .zero else { return }
        let clamped = CameraSelfViewPlacement.clamp(panel.frame, into: recordedRectCocoa)
        if clamped != panel.frame {
            panel.setFrame(clamped, display: true)
        }
    }
}
