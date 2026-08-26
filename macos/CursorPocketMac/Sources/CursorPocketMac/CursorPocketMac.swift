import AppKit
import CursorPocketMacKit
import SwiftUI

@main
struct CursorPocketMacApp: App {
    @StateObject private var services = AppServices.shared

    var body: some Scene {
        WindowGroup("CursorPocket") {
            MainView(services: services)
                .frame(minWidth: 720, minHeight: 480)
        }
        .windowStyle(.hiddenTitleBar)
        .commands {
            CommandGroup(replacing: .newItem) { }
        }

        MenuBarExtra {
            menuContent
        } label: {
            // Recording state is never conveyed by color alone: the symbol
            // itself changes and the first menu line spells it out.
            Image(systemName: services.recordingStartedAt == nil
                ? "cursorarrow.motionlines"
                : "record.circle.fill")
        }
    }

    @ViewBuilder
    private var menuContent: some View {
        if services.recordingStartedAt != nil {
            Button("Stop recording & save (Escape)") { services.stopRecording() }
            Divider()
        }
        Button("Screenshot — region or window") {
            services.captureScreenshot(mode: .interactive)
        }
        Button("Screenshot — full display") {
            services.captureScreenshot(mode: .display(1))
        }
        Divider()
        Button("Record a region…") { services.beginRegionRecording() }
            .disabled(services.recordingStartedAt != nil)
        Button("Record the display") { services.beginDisplayRecording() }
            .disabled(services.recordingStartedAt != nil)
        Button("Record a window…") { services.beginWindowRecording() }
            .disabled(services.recordingStartedAt != nil)
        Button(services.audioNotes.isRecording ? "Stop audio note & save" : "Start audio note") {
            services.toggleAudioNote()
        }
        Divider()
        Button("Grab selected text") { services.perform(.textCapture) }
        Button("Save browser link") { services.perform(.linkCapture) }
        Divider()
        Button("Command palette") { services.palette.toggle() }
        Button("Open Library") { services.perform(.openLibrary) }
        Button("Pin last screenshot") {
            if let record = services.records.first(where: { $0.captureKind == .screenshot }) {
                services.pin(record)
            }
        }
        .disabled(!services.records.contains { $0.captureKind == .screenshot })
        Divider()
        Button("Quit CursorPocket") { NSApp.terminate(nil) }
    }
}

struct MainView: View {
    @ObservedObject var services: AppServices

    var body: some View {
        TabView {
            LibraryView(services: services)
                .tabItem { Label("Library", systemImage: "tray.full") }
            SettingsView(services: services, updates: services.updates, loginItems: services.loginItems)
                .tabItem { Label("Settings", systemImage: "gearshape") }
        }
    }
}
