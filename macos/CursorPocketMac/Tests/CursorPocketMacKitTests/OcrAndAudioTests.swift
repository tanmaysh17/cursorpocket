import AVFoundation
import CoreGraphics
import Foundation
import XCTest
@testable import CursorPocketMacKit

final class OcrTextTests: XCTestCase {
    private func observation(_ text: String, x: CGFloat, midY: CGFloat, height: CGFloat = 0.08) -> OcrText.Observation {
        OcrText.Observation(
            text: text,
            boundingBox: CGRect(x: x, y: midY - height / 2, width: 0.2, height: height))
    }

    func testAssemblesReadingOrderTopToBottomLeftToRight() {
        // Vision boxes are bottom-left normalized: larger y is higher up.
        let observations = [
            observation("Bottom", x: 0.1, midY: 0.2),
            observation("World", x: 0.5, midY: 0.8),
            observation("Hello", x: 0.1, midY: 0.81),
        ]
        XCTAssertEqual(OcrText.assemble(observations), "Hello World\nBottom")
    }

    func testSeparateRowsStaySeparateLines() {
        let observations = [
            observation("line one", x: 0.1, midY: 0.9),
            observation("line two", x: 0.1, midY: 0.5),
            observation("line three", x: 0.1, midY: 0.1),
        ]
        XCTAssertEqual(OcrText.assemble(observations), "line one\nline two\nline three")
    }

    func testBlankObservationsAreDropped() {
        XCTAssertEqual(OcrText.assemble([]), "")
        XCTAssertEqual(OcrText.assemble([observation("   ", x: 0.1, midY: 0.5)]), "")
    }

    func testSizeFloorMirrorsTheWindowsFortyPixelRule() {
        XCTAssertTrue(OcrText.isBelowSizeFloor(width: 39, height: 500))
        XCTAssertTrue(OcrText.isBelowSizeFloor(width: 500, height: 12))
        XCTAssertFalse(OcrText.isBelowSizeFloor(width: 40, height: 40))
    }
}

final class AudioNoteFormatTests: XCTestCase {
    /// The recorder settings must describe a real PCM format — a bad settings
    /// dictionary makes AVAudioRecorder fail only at runtime on a real mic.
    func testRecorderSettingsDescribeAValidWavPcmFormat() {
        let settings: [String: Any] = [
            AVFormatIDKey: kAudioFormatLinearPCM,
            AVSampleRateKey: AudioNotePlan.sampleRate,
            AVNumberOfChannelsKey: AudioNotePlan.channels,
            AVLinearPCMBitDepthKey: AudioNotePlan.bitsPerSample,
            AVLinearPCMIsFloatKey: false,
            AVLinearPCMIsBigEndianKey: false,
        ]
        let format = AVAudioFormat(settings: settings)
        XCTAssertNotNil(format)
        XCTAssertEqual(format?.sampleRate, 44_100)
        XCTAssertEqual(format?.channelCount, 1)
        XCTAssertEqual(format?.streamDescription.pointee.mBitsPerChannel, 16)
    }

    func testKitConstantsMatchTheAVFoundationValues() {
        // The Kit spells out 'lpcm' so it needs no CoreAudio import; it must
        // stay equal to the real constant the app hands AVAudioRecorder.
        XCTAssertEqual(AudioNotePlan.kAudioFormatLinearPCMValue, kAudioFormatLinearPCM)
        XCTAssertEqual(AudioNotePlan.sampleRate, 44_100)
        XCTAssertEqual(AudioNotePlan.channels, 1)
        XCTAssertEqual(AudioNotePlan.bitsPerSample, 16)
    }

    /// 44 bytes — the WAV header size — is the recovery floor for audio: a
    /// header-only file is a failed take, one sample more is recoverable.
    func testAudioRecoveryFloorIsTheWavHeaderSize() {
        XCTAssertEqual(CaptureKind.audio.minimumRecoverableBytes, 44)
    }
}
