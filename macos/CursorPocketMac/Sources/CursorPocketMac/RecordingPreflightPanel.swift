import AVFoundation
import AppKit
import SwiftUI
import CursorPocketMacKit

struct PreflightChoices: Equatable {
    var microphoneEnabled: Bool
    var cameraEnabled: Bool
    var cameraShape: CameraSelfViewShape
}

/// Per-recording preflight: source summary, microphone and camera choices,
/// and a live camera preview. Deliberately NOT capture-excluded, mirroring
/// the Windows rule that keeps preflight inspectable. `Escape` cancels —
/// nothing has been captured yet. The preview's camera session is released
/// BEFORE the completion runs, or the recording self-view finds the device
/// busy.
final class RecordingPreflightController: NSObject {
    private var panel: NSPanel?
    private var model: PreflightModel?
    private var keyMonitor: Any?

    func present(
        summary: String,
        cameraNote: String?,
        initial: PreflightChoices,
        completion: @escaping (PreflightChoices?) -> Void
    ) {
        dismiss()
        let model = PreflightModel(choices: initial, summary: summary, cameraNote: cameraNote)
        model.finish = { [weak self] choices in
            guard let self, let model = self.model else { return }
            // Release the camera before anything downstream acquires it.
            model.stopPreview()
            self.dismiss()
            completion(choices)
        }
        self.model = model

        let panel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 380, height: 320),
            styleMask: [.titled, .closable, .nonactivatingPanel],
            backing: .buffered,
            defer: false)
        panel.title = "Start recording"
        panel.isFloatingPanel = true
        panel.becomesKeyOnlyIfNeeded = false
        panel.contentView = NSHostingView(rootView: PreflightView(model: model))
        panel.center()
        panel.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        self.panel = panel

        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self, event.window === self.panel else { return event }
            if event.keyCode == HotkeyDefaults.keyEscape {
                self.model?.finish?(nil)
                return nil
            }
            return event
        }
    }

    private func dismiss() {
        if let keyMonitor { NSEvent.removeMonitor(keyMonitor) }
        keyMonitor = nil
        model?.stopPreview()
        model = nil
        panel?.orderOut(nil)
        panel = nil
    }
}

final class PreflightModel: ObservableObject {
    @Published var choices: PreflightChoices {
        didSet { syncPreview() }
    }
    let summary: String
    let cameraNote: String?
    @Published private(set) var previewSession: AVCaptureSession?
    var finish: ((PreflightChoices?) -> Void)?

    init(choices: PreflightChoices, summary: String, cameraNote: String?) {
        self.choices = choices
        self.summary = summary
        self.cameraNote = cameraNote
        syncPreview()
    }

    private func syncPreview() {
        if choices.cameraEnabled, previewSession == nil {
            let session = AVCaptureSession()
            session.sessionPreset = .medium
            if let device = AVCaptureDevice.default(for: .video),
               let input = try? AVCaptureDeviceInput(device: device),
               session.canAddInput(input) {
                session.addInput(input)
                session.startRunning()
                previewSession = session
            }
        } else if !choices.cameraEnabled {
            stopPreview()
        }
    }

    func stopPreview() {
        previewSession?.stopRunning()
        previewSession = nil
    }
}

struct PreflightView: View {
    @ObservedObject var model: PreflightModel

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(model.summary)
                .font(.system(size: 13, weight: .semibold))
            Toggle("Record microphone narration", isOn: $model.choices.microphoneEnabled)
            Toggle("Show camera self-view", isOn: $model.choices.cameraEnabled)
            if model.choices.cameraEnabled {
                Picker("Self-view shape", selection: $model.choices.cameraShape) {
                    Text("Squircle (1:1)").tag(CameraSelfViewShape.squircle)
                    Text("Rounded (16:9)").tag(CameraSelfViewShape.rounded)
                }
                if let session = model.previewSession {
                    PreflightCameraPreview(session: session)
                        .frame(height: 110)
                        .clipShape(RoundedRectangle(cornerRadius: 8))
                }
                if let note = model.cameraNote {
                    Text(note)
                        .font(.system(size: 11))
                        .foregroundStyle(.secondary)
                }
            }
            Spacer()
            HStack {
                Text("Escape stops the recording and SAVES it")
                    .font(.system(size: 10, design: .monospaced))
                    .foregroundStyle(.secondary)
                Spacer()
                Button("Cancel") { model.finish?(nil) }
                Button {
                    model.finish?(model.choices)
                } label: {
                    Text("Start recording")
                        .font(.system(size: 12, weight: .semibold))
                        .foregroundStyle(Theme.pine)
                        .padding(.horizontal, 12)
                        .frame(height: 26)
                        .background(Theme.ready, in: RoundedRectangle(cornerRadius: 6))
                }
                .buttonStyle(.plain)
            }
        }
        .padding(16)
        .frame(width: 380, alignment: .leading)
    }
}

private struct PreflightCameraPreview: NSViewRepresentable {
    let session: AVCaptureSession

    func makeNSView(context: Context) -> NSView {
        let view = NSView()
        view.wantsLayer = true
        let layer = AVCaptureVideoPreviewLayer(session: session)
        layer.videoGravity = .resizeAspectFill
        layer.frame = view.bounds
        layer.autoresizingMask = [.layerWidthSizable, .layerHeightSizable]
        view.layer?.addSublayer(layer)
        return view
    }

    func updateNSView(_ nsView: NSView, context: Context) {
        (nsView.layer?.sublayers?.first as? AVCaptureVideoPreviewLayer)?.session = session
    }
}
