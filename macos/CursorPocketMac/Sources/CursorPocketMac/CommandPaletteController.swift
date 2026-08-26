import AppKit
import SwiftUI
import CursorPocketMacKit

/// The command palette: a small Spotlight-style panel the USER positions —
/// drag anywhere, position persisted as fractions of the display's free space.
/// Its bare mnemonic keys (S/V/A/T/L/O) are honored ONLY while the panel is
/// visible: the local key monitor lives and dies with the panel, so they can
/// never leak into ordinary typing. Clicking outside deliberately does not
/// dismiss it.
final class CommandPaletteController: NSObject, NSWindowDelegate {
    private var panel: NSPanel?
    private var keyMonitor: Any?
    private let panelSize = NSSize(width: 460, height: 120)

    var onCommand: ((PaletteCommand) -> Void)?
    var placementProvider: () -> PalettePlacement = { PalettePlacement() }
    var placementChanged: ((PalettePlacement) -> Void)?

    var isVisible: Bool { panel?.isVisible ?? false }
    var windowNumber: Int? { panel?.windowNumber }

    func toggle() {
        if isVisible { hide() } else { show() }
    }

    func show() {
        if panel == nil { buildPanel() }
        guard let panel else { return }
        positionFromPlacement(panel)
        panel.makeKeyAndOrderFront(nil)
        installKeyMonitor()
    }

    func hide() {
        removeKeyMonitor()
        panel?.orderOut(nil)
    }

    private func buildPanel() {
        let panel = KeyablePanel(
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
        panel.delegate = self
        panel.contentView = NSHostingView(rootView: CommandPaletteView { [weak self] command in
            self?.hide()
            self?.onCommand?(command)
        })
        self.panel = panel
    }

    private func positionFromPlacement(_ panel: NSPanel) {
        let free = (NSScreen.main ?? NSScreen.screens.first)?.visibleFrame ?? .zero
        let origin = placementProvider().origin(inFree: free, panelSize: panelSize)
        panel.setFrameOrigin(origin)
    }

    func windowDidMove(_ notification: Notification) {
        guard let panel, panel.isVisible else { return }
        let free = (panel.screen ?? NSScreen.main)?.visibleFrame ?? .zero
        guard free.width > 0 else { return }
        placementChanged?(PalettePlacement.fractions(
            forOrigin: panel.frame.origin, inFree: free, panelSize: panel.frame.size))
    }

    private func installKeyMonitor() {
        removeKeyMonitor()
        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self, self.isVisible else { return event }
            if event.keyCode == HotkeyDefaults.keyEscape {
                self.hide()
                return nil
            }
            guard let characters = event.charactersIgnoringModifiers, let key = characters.first,
                  event.modifierFlags.intersection([.command, .control, .option]).isEmpty,
                  let command = PaletteCommand.command(forKey: key) else { return event }
            self.hide()
            self.onCommand?(command)
            return nil
        }
    }

    private func removeKeyMonitor() {
        if let keyMonitor { NSEvent.removeMonitor(keyMonitor) }
        keyMonitor = nil
    }
}

/// A borderless nonactivating panel refuses key status by default; the
/// palette owns the user's attention while visible, so it takes it.
private final class KeyablePanel: NSPanel {
    override var canBecomeKey: Bool { true }
}

struct CommandPaletteView: View {
    let perform: (PaletteCommand) -> Void

    var body: some View {
        VStack(spacing: 10) {
            HStack(spacing: 8) {
                ForEach(PaletteCommand.allCases, id: \.self) { command in
                    Button {
                        perform(command)
                    } label: {
                        VStack(spacing: 6) {
                            Text(String(command.mnemonic).uppercased())
                                .font(.system(size: 15, weight: .bold, design: .monospaced))
                                .foregroundStyle(Theme.pine)
                                .frame(width: 30, height: 30)
                                .background(Theme.ready, in: RoundedRectangle(cornerRadius: 6))
                            Text(command.title)
                                .font(.system(size: 10, weight: .medium))
                                .foregroundStyle(Color.white.opacity(0.85))
                                .lineLimit(2)
                                .multilineTextAlignment(.center)
                        }
                        .frame(width: 66)
                    }
                    .buttonStyle(.plain)
                }
            }
            Text("Press a key · Escape closes · drag to move")
                .font(.system(size: 10, design: .monospaced))
                .foregroundStyle(Color.white.opacity(0.5))
        }
        .padding(12)
        .frame(width: 460, height: 120)
        .background(Theme.pine.opacity(0.97), in: RoundedRectangle(cornerRadius: 14))
    }
}
