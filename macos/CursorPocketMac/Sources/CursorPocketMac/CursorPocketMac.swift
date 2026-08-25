import AppKit
import SwiftUI

@main
struct CursorPocketMacApp: App {
    var body: some Scene {
        WindowGroup {
            MacAvailabilityView()
                .frame(minWidth: 560, minHeight: 420)
        }
        .windowStyle(.hiddenTitleBar)
        .commands {
            CommandGroup(replacing: .newItem) { }
        }
    }
}

private struct MacAvailabilityView: View {
    private let ready = Color(red: 54 / 255, green: 229 / 255, blue: 140 / 255)
    private let pine = Color(red: 7 / 255, green: 19 / 255, blue: 15 / 255)
    @State private var status = "Ready for a screenshot"
    @State private var isCapturing = false

    var body: some View {
        ZStack {
            pine.ignoresSafeArea()

            VStack(alignment: .leading, spacing: 24) {
                HStack(spacing: 12) {
                    Image(systemName: "cursorarrow.motionlines")
                        .font(.system(size: 30, weight: .semibold))
                        .foregroundStyle(ready)
                    Text("CursorPocket")
                        .font(.system(size: 30, weight: .semibold, design: .rounded))
                        .foregroundStyle(.white)
                }

                Spacer()

                Text("Capture something.\nKeep it local.")
                    .font(.system(size: 32, weight: .semibold, design: .rounded))
                    .foregroundStyle(.white)
                    .fixedSize(horizontal: false, vertical: true)

                Text("The first native macOS edition starts with interactive screenshots. Choose a region or window and CursorPocket saves a PNG in Documents/CursorPocket Captures. Video, audio, annotation, and the cursor companion remain Windows-only for now.")
                    .font(.system(size: 15))
                    .foregroundStyle(Color.white.opacity(0.78))
                    .lineSpacing(4)

                HStack(spacing: 12) {
                    Button(action: captureScreenshot) {
                        Label(isCapturing ? "Selecting…" : "Capture screenshot", systemImage: "viewfinder")
                            .font(.system(size: 14, weight: .semibold))
                            .foregroundStyle(pine)
                            .padding(.horizontal, 16)
                            .frame(height: 36)
                            .background(ready, in: RoundedRectangle(cornerRadius: 8))
                    }
                    .buttonStyle(.plain)
                    .disabled(isCapturing)

                    Button(action: openCaptureFolder) {
                        Label("Open captures", systemImage: "folder")
                            .font(.system(size: 14, weight: .semibold))
                            .foregroundStyle(.white)
                            .padding(.horizontal, 16)
                            .frame(height: 36)
                            .background(Color.white.opacity(0.12), in: RoundedRectangle(cornerRadius: 8))
                    }
                    .buttonStyle(.plain)
                }

                Spacer()

                Label(status, systemImage: isCapturing ? "viewfinder" : "lock.shield")
                    .font(.system(size: 13, design: .monospaced))
                    .foregroundStyle(Color.white.opacity(0.62))
            }
            .padding(32)
        }
        .preferredColorScheme(.dark)
    }

    private var captureFolder: URL {
        FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("CursorPocket Captures", isDirectory: true)
    }

    private func openCaptureFolder() {
        try? FileManager.default.createDirectory(at: captureFolder, withIntermediateDirectories: true)
        NSWorkspace.shared.open(captureFolder)
    }

    private func captureScreenshot() {
        isCapturing = true
        status = "Choose a region or window · Escape cancels"
        let folder = captureFolder

        DispatchQueue.global(qos: .userInitiated).async {
            do {
                try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
                let formatter = DateFormatter()
                formatter.dateFormat = "yyyy-MM-dd_HH-mm-ss"
                let destination = folder.appendingPathComponent("\(formatter.string(from: Date()))_screenshot.png")
                let task = Process()
                task.executableURL = URL(fileURLWithPath: "/usr/sbin/screencapture")
                task.arguments = ["-i", "-x", destination.path]
                try task.run()
                task.waitUntilExit()
                let saved = task.terminationStatus == 0 && FileManager.default.fileExists(atPath: destination.path)
                DispatchQueue.main.async {
                    isCapturing = false
                    status = saved ? "Saved \(destination.lastPathComponent)" : "Screenshot cancelled"
                    if saved { NSWorkspace.shared.activateFileViewerSelecting([destination]) }
                }
            } catch {
                DispatchQueue.main.async {
                    isCapturing = false
                    status = "Could not capture: \(error.localizedDescription)"
                }
            }
        }
    }
}
