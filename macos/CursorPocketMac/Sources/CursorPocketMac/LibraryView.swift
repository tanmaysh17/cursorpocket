import SwiftUI
import CursorPocketMacKit

struct LibraryView: View {
    @ObservedObject var services: AppServices
    @State private var filter: LibraryFilter = .all
    @State private var search = ""

    private var groups: [LibraryDayGroup] {
        LibraryModel.groupByDay(
            LibraryModel.filter(services.records, by: filter, search: search),
            today: Date())
    }

    var body: some View {
        VStack(spacing: 0) {
            header
            if groups.isEmpty {
                emptyState
            } else {
                List {
                    ForEach(groups) { group in
                        Section(group.label) {
                            ForEach(group.records) { record in
                                LibraryRow(record: record, services: services)
                            }
                        }
                    }
                }
                .listStyle(.inset)
            }
            statusStrip
        }
    }

    private var header: some View {
        VStack(spacing: 8) {
            HStack(spacing: 8) {
                Button {
                    services.captureScreenshot(mode: .interactive)
                } label: {
                    Label("Screenshot", systemImage: "viewfinder")
                }
                Button {
                    services.beginRegionRecording()
                } label: {
                    Label("Record region", systemImage: "record.circle")
                }
                Button {
                    services.beginDisplayRecording()
                } label: {
                    Label("Record display", systemImage: "rectangle.inset.filled.badge.record")
                }
                Button {
                    services.toggleAudioNote()
                } label: {
                    Label(
                        services.audioNotes.isRecording ? "Stop audio note" : "Audio note",
                        systemImage: "waveform")
                }
                Spacer()
                TextField("Search captures", text: $search)
                    .textFieldStyle(.roundedBorder)
                    .frame(width: 200)
            }
            Picker("Filter", selection: $filter) {
                ForEach(LibraryFilter.allCases, id: \.self) { option in
                    Text(option.title).tag(option)
                }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
        }
        .padding(12)
    }

    private var emptyState: some View {
        VStack(spacing: 8) {
            Spacer()
            Image(systemName: "tray")
                .font(.system(size: 32))
                .foregroundStyle(.secondary)
            Text("No captures yet — everything you capture lands here, on disk, local-first.")
                .foregroundStyle(.secondary)
            Spacer()
        }
        .frame(maxWidth: .infinity)
    }

    private var statusStrip: some View {
        HStack {
            if services.recordingStartedAt != nil {
                Circle().fill(Theme.alert).frame(width: 8, height: 8)
                Text("RECORDING — Escape stops and saves")
                    .font(.system(size: 11, weight: .semibold, design: .monospaced))
            } else {
                Text(services.statusMessage)
                    .font(.system(size: 11, design: .monospaced))
                    .foregroundStyle(.secondary)
            }
            Spacer()
            Text("\(services.records.count) captures")
                .font(.system(size: 11, design: .monospaced))
                .foregroundStyle(.secondary)
        }
        .padding(.horizontal, 12)
        .frame(height: 26)
    }
}

private struct LibraryRow: View {
    let record: CaptureRecord
    let services: AppServices
    @State private var showTextEditor = false

    private var icon: String {
        switch record.captureKind {
        case .screenshot: return "photo"
        case .video: return "film"
        case .audio: return "waveform"
        case .text: return "text.alignleft"
        case .link: return "link"
        }
    }

    private var timeLabel: String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.dateFormat = "HH:mm"
        return formatter.string(from: record.created)
    }

    var body: some View {
        HStack(spacing: 10) {
            if record.captureKind == .screenshot {
                LibraryThumbnail(record: record, services: services, fallbackIcon: icon)
            } else {
                Image(systemName: icon)
                    .frame(width: 22)
                    .foregroundStyle(Theme.ready)
            }
            VStack(alignment: .leading, spacing: 2) {
                Text(record.preview)
                    .lineLimit(1)
                Text("\(timeLabel) · \(record.relativePath)")
                    .font(.system(size: 10, design: .monospaced))
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
            Spacer()
            Button("Open") { services.open(record) }
            Button("Reveal") { services.reveal(record) }
            if record.captureKind == .screenshot {
                Button("Annotate") { services.annotate(record) }
                Button("Grab text") { services.recognizeText(record) }
                Button("Pin") { services.pin(record) }
            }
            if record.captureKind == .text {
                Button("Edit") { showTextEditor = true }
            }
            Button(role: .destructive) {
                services.moveToTrash(record)
            } label: {
                Text("Trash")
                    .foregroundStyle(Theme.alert)
            }
        }
        .padding(.vertical, 2)
        .sheet(isPresented: $showTextEditor) {
            TextCaptureEditSheet(record: record, services: services, isPresented: $showTextEditor)
        }
    }
}

/// Downsampled preview for screenshot rows. Loads off the main thread and
/// keeps the kind icon while loading or when the file is unreadable.
private struct LibraryThumbnail: View {
    let record: CaptureRecord
    let services: AppServices
    let fallbackIcon: String
    @State private var image: NSImage?

    var body: some View {
        Group {
            if let image {
                Image(nsImage: image)
                    .resizable()
                    .aspectRatio(contentMode: .fill)
                    .frame(width: 56, height: 36)
                    .clipShape(RoundedRectangle(cornerRadius: 4))
            } else {
                Image(systemName: fallbackIcon)
                    .frame(width: 56, height: 36)
                    .foregroundStyle(Theme.ready)
            }
        }
        .onAppear {
            guard let url = try? services.captureStore.absoluteURL(for: record) else { return }
            ThumbnailLoader.shared.load(recordID: record.id, fileURL: url) { image = $0 }
        }
    }
}

/// Small sheet editing a text capture's content. Save goes through the
/// store's `updateText`, which rewrites the same manifest line in place, so
/// the same Library row updates with a recompacted preview.
private struct TextCaptureEditSheet: View {
    let record: CaptureRecord
    let services: AppServices
    @Binding var isPresented: Bool
    @State private var text = ""
    @State private var errorMessage: String?

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Edit text capture")
                .font(.headline)
            TextEditor(text: $text)
                .font(.system(size: 12, design: .monospaced))
                .frame(width: 420, height: 220)
            if let errorMessage {
                Text(errorMessage)
                    .font(.system(size: 11))
                    .foregroundStyle(Theme.alert)
            }
            HStack {
                Spacer()
                Button("Cancel") { isPresented = false }
                    .keyboardShortcut(.cancelAction)
                Button("Save") { save() }
                    .keyboardShortcut(.defaultAction)
                    .disabled(text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
            }
        }
        .padding(16)
        .onAppear(perform: load)
    }

    private func load() {
        guard let url = try? services.captureStore.absoluteURL(for: record),
              let body = try? String(contentsOf: url, encoding: .utf8) else { return }
        // The store writes a trailing newline; don't surface it as content.
        text = body.hasSuffix("\n") ? String(body.dropLast()) : body
    }

    private func save() {
        do {
            _ = try services.captureStore.updateText(record: record, newText: text)
            services.refreshLibrary()
            services.statusMessage = "Updated text capture"
            isPresented = false
        } catch {
            errorMessage = "Could not save: \(error.localizedDescription)"
        }
    }
}
