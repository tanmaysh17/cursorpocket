import AVFoundation
import AppKit
import CursorPocketMacKit
import ScreenCaptureKit

/// Screen recording: ScreenCaptureKit frames and an optional microphone are
/// muxed by AVAssetWriter into H.264/AAC MP4. Capture writes a sibling
/// `.partial.mp4`; only a successful finalize moves it onto the reserved
/// library path and registers it, so a crash never leaves a half-indexed take.
final class RecordingController: NSObject, ObservableObject, SCStreamDelegate, SCStreamOutput {
    @Published private(set) var state: RecordingState = .idle

    private let store: () -> CaptureStore
    private var stream: SCStream?
    private var writer: AVAssetWriter?
    private var videoInput: AVAssetWriterInput?
    private var audioInput: AVAssetWriterInput?
    private var microphoneSession: AVCaptureSession?
    private var reservation: CaptureReservation?
    private var options: RecordingOptions?
    private var sessionStarted = false
    private var firstVideoTime = CMTime.invalid
    private let sampleQueue = DispatchQueue(label: "cursorpocket.recording.samples")

    /// Windows to keep OUT of the recording (HUD, palette, main window). The
    /// camera self-view is deliberately absent from this list — excluding it
    /// would drop the webcam from the file.
    var excludedWindowNumbers: () -> [Int] = { [] }

    var onFinished: ((Result<CaptureRecord, Error>) -> Void)?

    init(store: @escaping () -> CaptureStore) {
        self.store = store
        super.init()
    }

    var isRecording: Bool {
        if case .recording = state { return true }
        return false
    }

    // MARK: Start

    func start(options: RecordingOptions) async throws {
        guard case .idle = state else { return }
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: true)

        let configuration = SCStreamConfiguration()
        configuration.minimumFrameInterval = CMTime(value: 1, timescale: CMTimeScale(RecordingPlan.framesPerSecond))
        configuration.showsCursor = true
        configuration.queueDepth = 6

        let filter: SCContentFilter
        let pixelSize: (width: Int, height: Int)
        switch options.source {
        case .display(let displayID), .region(let displayID, _):
            guard let display = content.displays.first(where: { $0.displayID == displayID })
                ?? content.displays.first else {
                throw RecordingError.noDisplay
            }
            let excludedNumbers = Set(excludedWindowNumbers())
            let ourWindows = content.windows.filter { window in
                window.owningApplication?.processID == pid_t(ProcessInfo.processInfo.processIdentifier)
                    && excludedNumbers.contains(Int(window.windowID))
            }
            filter = SCContentFilter(display: display, excludingWindows: ourWindows)
            let scale = CoordinateSpaces.screen(forDisplayID: display.displayID)?.backingScaleFactor ?? 2
            if case .region(_, let rect) = options.source {
                configuration.sourceRect = rect
                pixelSize = RecordingPlan.evenPixelSize(width: rect.width, height: rect.height, scale: scale)
            } else {
                pixelSize = RecordingPlan.evenPixelSize(
                    width: CGFloat(display.width), height: CGFloat(display.height), scale: scale)
            }
        case .window(let windowID):
            guard let window = content.windows.first(where: { $0.windowID == windowID }) else {
                throw RecordingError.windowGone
            }
            // A window recording captures the window's own pixels wherever it
            // moves; our exclusion list is irrelevant here.
            filter = SCContentFilter(desktopIndependentWindow: window)
            let center = CGPoint(x: window.frame.midX, y: window.frame.midY)
            let scale = NSScreen.screens.first {
                CoordinateSpaces.cgRect(fromCocoa: $0.frame).contains(center)
            }?.backingScaleFactor ?? 2
            pixelSize = RecordingPlan.evenPixelSize(
                width: window.frame.width, height: window.frame.height, scale: scale)
        }
        configuration.width = pixelSize.width
        configuration.height = pixelSize.height

        let reservation = store().reserve(kind: .video)
        let partialURL = RecordingPlan.partialURL(for: reservation.absoluteURL)
        try? FileManager.default.removeItem(at: partialURL)

        let writer = try AVAssetWriter(outputURL: partialURL, fileType: .mp4)
        let videoSettings: [String: Any] = [
            AVVideoCodecKey: AVVideoCodecType.h264,
            AVVideoWidthKey: pixelSize.width,
            AVVideoHeightKey: pixelSize.height,
            AVVideoCompressionPropertiesKey: [
                AVVideoAverageBitRateKey: RecordingPlan.videoBitrate(
                    width: pixelSize.width, height: pixelSize.height),
            ],
        ]
        let videoInput = AVAssetWriterInput(mediaType: .video, outputSettings: videoSettings)
        videoInput.expectsMediaDataInRealTime = true
        writer.add(videoInput)

        var audioInput: AVAssetWriterInput?
        if options.microphoneEnabled {
            let audioSettings: [String: Any] = [
                AVFormatIDKey: kAudioFormatMPEG4AAC,
                AVSampleRateKey: 44_100,
                AVNumberOfChannelsKey: 1,
            ]
            let input = AVAssetWriterInput(mediaType: .audio, outputSettings: audioSettings)
            input.expectsMediaDataInRealTime = true
            writer.add(input)
            audioInput = input
        }

        guard writer.startWriting() else {
            throw writer.error ?? RecordingError.writerFailed
        }

        let stream = SCStream(filter: filter, configuration: configuration, delegate: self)
        try stream.addStreamOutput(self, type: .screen, sampleHandlerQueue: sampleQueue)

        self.writer = writer
        self.videoInput = videoInput
        self.audioInput = audioInput
        self.reservation = reservation
        self.options = options
        self.stream = stream
        sessionStarted = false
        firstVideoTime = .invalid

        if options.microphoneEnabled {
            startMicrophone()
        }

        try await stream.startCapture()
        await MainActor.run { self.state = .recording(startedAt: Date()) }
    }

    private func startMicrophone() {
        let session = AVCaptureSession()
        guard let device = AVCaptureDevice.default(for: .audio),
              let input = try? AVCaptureDeviceInput(device: device),
              session.canAddInput(input) else { return }
        session.addInput(input)
        let output = AVCaptureAudioDataOutput()
        output.setSampleBufferDelegate(self, queue: sampleQueue)
        guard session.canAddOutput(output) else { return }
        session.addOutput(output)
        session.startRunning()
        microphoneSession = session
    }

    // MARK: Samples

    func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer, of type: SCStreamOutputType) {
        guard type == .screen, CMSampleBufferIsValid(sampleBuffer),
              CMSampleBufferGetImageBuffer(sampleBuffer) != nil,
              let writer, let videoInput else { return }

        if let attachments = CMSampleBufferGetSampleAttachmentsArray(sampleBuffer, createIfNecessary: false)
            as? [[SCStreamFrameInfo: Any]],
            let statusValue = attachments.first?[.status] as? Int,
            let status = SCFrameStatus(rawValue: statusValue),
            status != .complete {
            return
        }

        let time = CMSampleBufferGetPresentationTimeStamp(sampleBuffer)
        if !sessionStarted {
            writer.startSession(atSourceTime: time)
            firstVideoTime = time
            sessionStarted = true
        }
        if videoInput.isReadyForMoreMediaData {
            videoInput.append(sampleBuffer)
        }
    }

    func stream(_ stream: SCStream, didStopWithError error: Error) {
        Task { await self.finish(streamAlreadyStopped: true) }
    }

    // MARK: Stop

    /// `Escape` and every Stop affordance land here: recording always stops
    /// by SAVING. Discard is a Library action on the saved record, never a
    /// stop-time gamble.
    func stop() {
        guard isRecording else { return }
        Task { await finish(streamAlreadyStopped: false) }
    }

    private func finish(streamAlreadyStopped: Bool) async {
        guard let writer, let reservation, let options else { return }
        let startedAt: Date
        if case .recording(let start) = state { startedAt = start } else { startedAt = Date() }
        await MainActor.run { self.state = .finalizing }

        if !streamAlreadyStopped {
            try? await stream?.stopCapture()
        }
        microphoneSession?.stopRunning()
        microphoneSession = nil

        videoInput?.markAsFinished()
        audioInput?.markAsFinished()

        let duration = Date().timeIntervalSince(startedAt)
        let partialURL = writer.outputURL
        let finished: Result<CaptureRecord, Error>
        if sessionStarted {
            await writer.finishWriting()
            do {
                try FileManager.default.moveItem(at: partialURL, to: reservation.absoluteURL)
                let record = try store().registerReservation(
                    reservation,
                    preview: RecordingPlan.preview(for: options, durationSeconds: duration),
                    metadata: ["duration_seconds": .number(duration.rounded())])
                finished = .success(record)
            } catch {
                finished = .failure(error)
            }
        } else {
            writer.cancelWriting()
            try? FileManager.default.removeItem(at: partialURL)
            finished = .failure(RecordingError.noFramesCaptured)
        }

        stream = nil
        self.writer = nil
        videoInput = nil
        audioInput = nil
        self.reservation = nil
        self.options = nil
        sessionStarted = false

        await MainActor.run {
            self.state = .idle
            self.onFinished?(finished)
        }
    }

    enum RecordingError: LocalizedError {
        case noDisplay
        case windowGone
        case writerFailed
        case noFramesCaptured

        var errorDescription: String? {
            switch self {
            case .noDisplay: return "No recordable display was found."
            case .windowGone: return "That window closed before recording could start."
            case .writerFailed: return "The video writer could not start."
            case .noFramesCaptured: return "No frames arrived — check Screen Recording permission."
            }
        }
    }
}

extension RecordingController: AVCaptureAudioDataOutputSampleBufferDelegate {
    func captureOutput(
        _ output: AVCaptureOutput,
        didOutput sampleBuffer: CMSampleBuffer,
        from connection: AVCaptureConnection
    ) {
        guard let audioInput, sessionStarted else { return }
        let time = CMSampleBufferGetPresentationTimeStamp(sampleBuffer)
        // Audio arriving before the writer session opened would rewind the mux.
        guard CMTimeCompare(time, firstVideoTime) >= 0 else { return }
        if audioInput.isReadyForMoreMediaData {
            audioInput.append(sampleBuffer)
        }
    }
}
