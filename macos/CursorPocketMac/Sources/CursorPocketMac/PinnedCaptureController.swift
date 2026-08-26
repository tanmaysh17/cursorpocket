import AppKit
import SwiftUI
import CursorPocketMacKit

/// Pinned captures: a screenshot floated in a small always-on-top draggable
/// panel. The binding invariants (each fixed after a real regression on
/// Windows):
/// - Pins are created only by explicit action and are NEVER restored after a
///   restart — nothing here persists anything.
/// - Pins are deliberately NOT capture-excluded: pinning something next to a
///   recording so it appears in the file is the point.
/// - Pins register no Escape handling of any kind — a pin can sit on screen
///   for hours, and grabbing Escape would steal it from every application,
///   including a live recording where Escape means stop-and-save.
final class PinnedCaptureController {
    private var pins: [Int: NSPanel] = [:]
    private var nextPinID = 0
    private var createdCount = 0

    var pinCount: Int { pins.count }

    /// Floats the image at `imageURL`. Does nothing when the file cannot be
    /// read as an image. Multiple pins cascade from the top-right.
    func pin(imageURL: URL) {
        guard let image = NSImage(contentsOf: imageURL) else { return }
        let size = PinnedCapturePlacement.fitSize(imageSize: image.size)
        guard size != .zero else { return }

        let pinID = nextPinID
        nextPinID += 1

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
        // Deliberately NOT capture-excluded — a pin is content, not chrome.
        panel.sharingType = .readOnly
        panel.contentView = NSHostingView(rootView: PinnedCaptureView(
            image: image,
            close: { [weak self] in self?.close(pinID) }))

        let free = (NSScreen.main ?? NSScreen.screens.first)?.visibleFrame ?? .zero
        panel.setFrameOrigin(PinnedCapturePlacement.origin(
            inVisibleFrame: free, panelSize: size, pinIndex: createdCount))
        createdCount += 1
        panel.orderFrontRegardless()
        pins[pinID] = panel
    }

    func close(_ pinID: Int) {
        pins[pinID]?.orderOut(nil)
        pins[pinID] = nil
        if pins.isEmpty { createdCount = 0 }
    }

    func closeAll() {
        for id in Array(pins.keys) { close(id) }
    }
}

/// The pinned image with a close button that appears on hover. No keyboard
/// handling at all, by design.
private struct PinnedCaptureView: View {
    let image: NSImage
    let close: () -> Void
    @State private var hovering = false

    var body: some View {
        ZStack(alignment: .topTrailing) {
            Image(nsImage: image)
                .resizable()
                .aspectRatio(contentMode: .fit)
                .clipShape(RoundedRectangle(cornerRadius: 8))
                .overlay(
                    RoundedRectangle(cornerRadius: 8)
                        .strokeBorder(Color.black.opacity(0.35), lineWidth: 1))
            if hovering {
                Button {
                    close()
                } label: {
                    Image(systemName: "xmark.circle.fill")
                        .font(.system(size: 16, weight: .semibold))
                        .foregroundStyle(.white, Color.black.opacity(0.6))
                }
                .buttonStyle(.plain)
                .padding(6)
                .accessibilityLabel("Close pin")
            }
        }
        .onHover { hovering = $0 }
    }
}
