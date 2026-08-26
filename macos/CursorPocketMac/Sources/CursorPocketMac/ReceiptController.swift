import AppKit
import SwiftUI
import CursorPocketMacKit

/// The capture receipt: a small panel in the bottom-right confirming what was
/// just saved, with Open / Reveal / Annotate (screenshots only) / Dismiss.
/// A receipt does NOT own the user's attention — they carry on working while
/// it is up — so its key access requires Control+Option, never bare keys
/// (`ReceiptPolicy` enforces the chord). It auto-dismisses after six seconds
/// and is excluded from capture so a receipt for one take never appears in
/// the next.
final class ReceiptController {
    private var panel: NSPanel?
    private var keyMonitor: Any?
    private var dismissWork: DispatchWorkItem?
    private var currentRecord: CaptureRecord?

    var onAction: ((ReceiptAction, CaptureRecord) -> Void)?
    var windowNumber: Int? { panel?.windowNumber }
    var isVisible: Bool { panel?.isVisible ?? false }

    private let panelSize = NSSize(width: 360, height: 96)

    /// Shows a receipt for the record, replacing any receipt already up —
    /// latest capture wins.
    func show(_ record: CaptureRecord) {
        dismiss()
        currentRecord = record

        let panel = NSPanel(
            contentRect: NSRect(origin: .zero, size: panelSize),
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
        // CursorPocket chrome never appears in captured media.
        panel.sharingType = .none
        panel.contentView = NSHostingView(rootView: ReceiptView(
            record: record,
            perform: { [weak self] action in self?.perform(action) }))

        let free = (NSScreen.main ?? NSScreen.screens.first)?.visibleFrame ?? .zero
        panel.setFrameOrigin(ReceiptPolicy.origin(inVisibleFrame: free, panelSize: panelSize))
        panel.orderFrontRegardless()
        self.panel = panel

        installKeyMonitor()
        scheduleAutoDismiss()
    }

    func dismiss() {
        dismissWork?.cancel()
        dismissWork = nil
        removeKeyMonitor()
        panel?.orderOut(nil)
        panel = nil
        currentRecord = nil
    }

    private func perform(_ action: ReceiptAction) {
        guard let record = currentRecord else { return }
        dismiss()
        if action != .dismiss {
            onAction?(action, record)
        }
    }

    private func scheduleAutoDismiss() {
        let work = DispatchWorkItem { [weak self] in self?.dismiss() }
        dismissWork = work
        DispatchQueue.main.asyncAfter(
            deadline: .now() + ReceiptPolicy.autoDismissSeconds, execute: work)
    }

    /// Local monitor only: it sees keys already headed to this app, so it can
    /// never take keystrokes from another application. `ReceiptPolicy`
    /// refuses anything without the full Control+Option chord.
    private func installKeyMonitor() {
        removeKeyMonitor()
        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self, self.isVisible, let record = self.currentRecord else { return event }
            let modifiers = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
            let hasControlOption = modifiers.contains(.control) && modifiers.contains(.option)
            guard let characters = event.charactersIgnoringModifiers, let key = characters.first,
                  let action = ReceiptPolicy.action(
                      forKey: key, kind: record.captureKind, hasControlOption: hasControlOption)
            else { return event }
            self.perform(action)
            return nil
        }
    }

    private func removeKeyMonitor() {
        if let keyMonitor { NSEvent.removeMonitor(keyMonitor) }
        keyMonitor = nil
    }
}

private struct ReceiptView: View {
    let record: CaptureRecord
    let perform: (ReceiptAction) -> Void

    private var icon: String {
        switch record.captureKind {
        case .screenshot: return "photo"
        case .video: return "film"
        case .audio: return "waveform"
        case .text: return "text.alignleft"
        case .link: return "link"
        }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack(spacing: 8) {
                Image(systemName: icon)
                    .foregroundStyle(Theme.ready)
                    .frame(width: 20)
                Text("Saved — \(record.preview)")
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundStyle(.white)
                    .lineLimit(1)
                    .truncationMode(.middle)
                Spacer(minLength: 0)
            }
            HStack(spacing: 8) {
                ForEach(ReceiptPolicy.actions(for: record.captureKind), id: \.self) { action in
                    Button {
                        perform(action)
                    } label: {
                        Text("\(action.title) ⌃⌥\(String(action.key).uppercased())")
                            .font(.system(size: 10, weight: .medium))
                            .foregroundStyle(action == .dismiss ? Color.white.opacity(0.7) : Theme.pine)
                            .padding(.horizontal, 8)
                            .frame(height: 22)
                            .background(
                                action == .dismiss ? Color.white.opacity(0.12) : Theme.ready,
                                in: RoundedRectangle(cornerRadius: 5))
                    }
                    .buttonStyle(.plain)
                }
                Spacer(minLength: 0)
            }
        }
        .padding(12)
        .frame(width: 360, height: 96, alignment: .leading)
        .background(Theme.pine.opacity(0.97), in: RoundedRectangle(cornerRadius: 12))
    }
}
