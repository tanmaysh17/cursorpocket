import AppKit
import ScreenCaptureKit
import SwiftUI
import CursorPocketMacKit

struct PickableWindow: Identifiable, Equatable {
    let id: UInt32
    let appName: String
    let title: String
    /// CG global coordinates (top-left origin), points.
    let frame: CGRect
}

/// Picks a window to record. Our own windows, tiny utility surfaces, and
/// untitled windows are not meaningful targets and are filtered out.
final class WindowPickerController: NSObject {
    private var panel: NSPanel?
    private var keyMonitor: Any?
    private var completion: ((PickableWindow?) -> Void)?

    func present(completion: @escaping (PickableWindow?) -> Void) {
        finish(nil)
        self.completion = completion
        Task { @MainActor in
            let windows = (try? await Self.pickableWindows()) ?? []
            guard !windows.isEmpty else {
                self.finish(nil)
                return
            }
            self.show(windows: windows)
        }
    }

    static func pickableWindows() async throws -> [PickableWindow] {
        let content = try await SCShareableContent.excludingDesktopWindows(true, onScreenWindowsOnly: true)
        let ourPID = pid_t(ProcessInfo.processInfo.processIdentifier)
        return content.windows.compactMap { window in
            guard window.owningApplication?.processID != ourPID,
                  window.frame.width >= 50, window.frame.height >= 50,
                  let title = window.title, !title.isEmpty else { return nil }
            return PickableWindow(
                id: window.windowID,
                appName: window.owningApplication?.applicationName ?? "Unknown app",
                title: title,
                frame: window.frame)
        }
    }

    private func show(windows: [PickableWindow]) {
        let panel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 420, height: 360),
            styleMask: [.titled, .closable, .nonactivatingPanel],
            backing: .buffered,
            defer: false)
        panel.title = "Record a window"
        panel.isFloatingPanel = true
        panel.contentView = NSHostingView(rootView: WindowPickerView(
            windows: windows,
            pick: { [weak self] window in self?.finish(window) },
            cancel: { [weak self] in self?.finish(nil) }))
        panel.center()
        panel.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        self.panel = panel

        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self, event.window === self.panel else { return event }
            if event.keyCode == HotkeyDefaults.keyEscape {
                self.finish(nil)
                return nil
            }
            return event
        }
    }

    private func finish(_ window: PickableWindow?) {
        if let keyMonitor { NSEvent.removeMonitor(keyMonitor) }
        keyMonitor = nil
        panel?.orderOut(nil)
        panel = nil
        let callback = completion
        completion = nil
        callback?(window)
    }
}

private struct WindowPickerView: View {
    let windows: [PickableWindow]
    let pick: (PickableWindow) -> Void
    let cancel: () -> Void

    var body: some View {
        VStack(spacing: 0) {
            List(windows) { window in
                Button {
                    pick(window)
                } label: {
                    VStack(alignment: .leading, spacing: 2) {
                        Text(window.title).lineLimit(1)
                        Text("\(window.appName) · \(Int(window.frame.width))×\(Int(window.frame.height))")
                            .font(.system(size: 10, design: .monospaced))
                            .foregroundStyle(.secondary)
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }
            HStack {
                Text("The recording follows the window wherever it moves")
                    .font(.system(size: 10, design: .monospaced))
                    .foregroundStyle(.secondary)
                Spacer()
                Button("Cancel") { cancel() }
            }
            .padding(10)
        }
        .frame(width: 420, height: 360)
    }
}
