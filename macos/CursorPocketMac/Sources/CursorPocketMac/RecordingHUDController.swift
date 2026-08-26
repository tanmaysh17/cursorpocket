import AppKit
import SwiftUI
import CursorPocketMacKit

/// The small recording HUD: elapsed time, a labeled recording state (never
/// color alone), and Stop. Excluded from the recording via the stream's
/// window-exclusion list and `sharingType`.
final class RecordingHUDController {
    private var panel: NSPanel?
    private var model = HUDModel()

    var windowNumber: Int? { panel?.windowNumber }

    func show(startedAt: Date, onStop: @escaping () -> Void) {
        hide()
        model = HUDModel()
        model.startedAt = startedAt
        model.onStop = onStop

        let size = NSSize(width: 240, height: 56)
        let panel = NSPanel(
            contentRect: NSRect(origin: .zero, size: size),
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
        panel.sharingType = .none
        panel.contentView = NSHostingView(rootView: RecordingHUDView(model: model))

        if let screen = NSScreen.main {
            let frame = screen.visibleFrame
            panel.setFrameOrigin(NSPoint(
                x: frame.midX - size.width / 2,
                y: frame.maxY - size.height - 12))
        }
        panel.orderFrontRegardless()
        self.panel = panel
    }

    func hide() {
        panel?.orderOut(nil)
        panel = nil
    }
}

final class HUDModel: ObservableObject {
    @Published var startedAt = Date()
    var onStop: (() -> Void)?
}

struct RecordingHUDView: View {
    @ObservedObject var model: HUDModel

    var body: some View {
        HStack(spacing: 10) {
            Circle().fill(Theme.alert).frame(width: 10, height: 10)
            TimelineView(.periodic(from: .now, by: 1)) { context in
                Text("REC \(RecordingPlan.formatElapsed(context.date.timeIntervalSince(model.startedAt)))")
                    .font(.system(size: 13, weight: .semibold, design: .monospaced))
                    .foregroundStyle(.white)
            }
            Spacer()
            Button {
                model.onStop?()
            } label: {
                Text("Stop & save")
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundStyle(Theme.pine)
                    .padding(.horizontal, 10)
                    .frame(height: 26)
                    .background(Theme.ready, in: RoundedRectangle(cornerRadius: 6))
            }
            .buttonStyle(.plain)
        }
        .padding(.horizontal, 14)
        .frame(width: 240, height: 56)
        .background(Theme.pine, in: RoundedRectangle(cornerRadius: 12))
    }
}
