import Foundation

/// Recording settings for audio notes. WAV/PCM matches the Windows library
/// contract (`audio/*.wav`), and notes must save with nothing but the
/// recorder available — no post-processing sits between the user and the take.
public enum AudioNotePlan {
    public static let sampleRate: Double = 44_100
    public static let channels = 1
    public static let bitsPerSample = 16

    /// AVAudioRecorder settings dictionary keys are plain strings here so the
    /// Kit stays importable without AVFoundation.
    public static var recorderSettings: [String: Any] {
        [
            "AVFormatIDKey": kAudioFormatLinearPCMValue,
            "AVSampleRateKey": sampleRate,
            "AVNumberOfChannelsKey": channels,
            "AVLinearPCMBitDepthKey": bitsPerSample,
            "AVLinearPCMIsFloatKey": false,
            "AVLinearPCMIsBigEndianKey": false,
        ]
    }

    /// 'lpcm' as a FourCC, spelled out so the Kit needs no CoreAudio import.
    public static let kAudioFormatLinearPCMValue: UInt32 = 0x6C70_636D

    public static func formatDuration(_ seconds: TimeInterval) -> String {
        let total = max(0, Int(seconds.rounded()))
        let hours = total / 3600
        let minutes = (total % 3600) / 60
        let secs = total % 60
        return hours > 0
            ? String(format: "%d:%02d:%02d", hours, minutes, secs)
            : String(format: "%d:%02d", minutes, secs)
    }
}
