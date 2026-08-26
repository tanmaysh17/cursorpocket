import AVFoundation
import CursorPocketMacKit

/// Audio notes record straight to WAV at the reserved library path. The raw
/// take is the deliverable: it is fully on disk before optional cleanup runs,
/// cleanup replaces it only when every step succeeds, and any failure keeps
/// the raw take — a note always saves as long as the recorder itself ran.
final class AudioNoteService: NSObject, ObservableObject {
    @Published private(set) var isRecording = false
    @Published private(set) var startedAt: Date?

    private var recorder: AVAudioRecorder?
    private var reservation: CaptureReservation?
    private let store: () -> CaptureStore
    private let cleanupEnabled: () -> Bool

    init(store: @escaping () -> CaptureStore, cleanupEnabled: @escaping () -> Bool = { false }) {
        self.store = store
        self.cleanupEnabled = cleanupEnabled
        super.init()
    }

    func start() throws {
        guard !isRecording else { return }
        let reservation = store().reserve(kind: .audio)
        let settings: [String: Any] = [
            AVFormatIDKey: kAudioFormatLinearPCM,
            AVSampleRateKey: AudioNotePlan.sampleRate,
            AVNumberOfChannelsKey: AudioNotePlan.channels,
            AVLinearPCMBitDepthKey: AudioNotePlan.bitsPerSample,
            AVLinearPCMIsFloatKey: false,
            AVLinearPCMIsBigEndianKey: false,
        ]
        let recorder = try AVAudioRecorder(url: reservation.absoluteURL, settings: settings)
        guard recorder.record() else {
            throw NSError(
                domain: "CursorPocket", code: 1,
                userInfo: [NSLocalizedDescriptionKey: "The microphone could not start."])
        }
        self.recorder = recorder
        self.reservation = reservation
        startedAt = Date()
        isRecording = true
    }

    @discardableResult
    func stop() -> CaptureRecord? {
        guard isRecording, let recorder, let reservation else { return nil }
        let duration = recorder.currentTime
        recorder.stop()
        self.recorder = nil
        self.reservation = nil
        isRecording = false
        startedAt = nil
        // Finalize-time cleanup, mirroring the Windows rule: the raw capture
        // is already on disk, and a cleanup failure just means the raw take
        // ships. Registration happens exactly once either way.
        if cleanupEnabled() {
            cleanUpInPlace(at: reservation.absoluteURL)
        }
        let preview = "Audio note (\(AudioNotePlan.formatDuration(duration)))"
        return try? store().registerReservation(
            reservation, preview: preview,
            metadata: ["duration_seconds": .number(duration.rounded())])
    }

    /// Reads the WAV as float samples, runs the pure Kit DSP (80 Hz high-pass
    /// + peak normalize), writes a sibling temp WAV in the recorder's own
    /// on-disk format, and atomically swaps it in. Every guard falls through
    /// to "keep the raw take" — nothing here may stand between the user and
    /// their recording.
    private func cleanUpInPlace(at url: URL) {
        let tempURL = url.deletingLastPathComponent()
            .appendingPathComponent(url.deletingPathExtension().lastPathComponent + "_cleanup.tmp.wav")
        try? FileManager.default.removeItem(at: tempURL)
        do {
            let input = try AVAudioFile(forReading: url, commonFormat: .pcmFormatFloat32, interleaved: false)
            let frameCount = AVAudioFrameCount(input.length)
            guard frameCount > 0,
                  let buffer = AVAudioPCMBuffer(pcmFormat: input.processingFormat, frameCapacity: frameCount)
            else { return }
            try input.read(into: buffer)
            guard buffer.frameLength > 0, let channels = buffer.floatChannelData else { return }
            let sampleRate = input.processingFormat.sampleRate
            let frames = Int(buffer.frameLength)
            for channel in 0..<Int(input.processingFormat.channelCount) {
                let samples = Array(UnsafeBufferPointer(start: channels[channel], count: frames))
                let cleaned = AudioCleanup.process(samples, sampleRate: sampleRate)
                guard cleaned.count == frames else { return }
                cleaned.withUnsafeBufferPointer { source in
                    guard let base = source.baseAddress else { return }
                    channels[channel].update(from: base, count: frames)
                }
            }
            // The writer flushes its header on deallocation; the pool scope
            // guarantees that happens before the file is swapped in.
            try autoreleasepool {
                let output = try AVAudioFile(
                    forWriting: tempURL, settings: input.fileFormat.settings,
                    commonFormat: .pcmFormatFloat32, interleaved: false)
                try output.write(from: buffer)
            }
            _ = try FileManager.default.replaceItemAt(url, withItemAt: tempURL)
        } catch {
            // Keep the raw take; only the abandoned temp file is removed.
            try? FileManager.default.removeItem(at: tempURL)
        }
    }
}
