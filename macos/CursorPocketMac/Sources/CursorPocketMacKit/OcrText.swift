import CoreGraphics
import Foundation

/// Pure logic for OCR results. The Vision call itself lives in the app; this
/// assembles its observations into readable text. Recognized text NEVER
/// reaches the clipboard from here or the service — it is saved as a text
/// capture the user can copy deliberately.
public enum OcrText {
    /// Below this, recognition rarely returns anything useful — and scaling a
    /// tiny region up does not help, so callers should not try.
    public static let minimumComfortableDimension: CGFloat = 40

    public struct Observation: Equatable {
        public let text: String
        /// Vision-style normalized bounding box: origin bottom-left, 0...1.
        public let boundingBox: CGRect

        public init(text: String, boundingBox: CGRect) {
            self.text = text
            self.boundingBox = boundingBox
        }
    }

    public static func isBelowSizeFloor(width: CGFloat, height: CGFloat) -> Bool {
        min(width, height) < minimumComfortableDimension
    }

    /// Orders observations into reading order — top to bottom, then left to
    /// right within a row — and joins them line by line. Observations whose
    /// vertical centers are close share a row.
    public static func assemble(_ observations: [Observation]) -> String {
        let usable = observations.filter { !$0.text.trimmingCharacters(in: .whitespaces).isEmpty }
        guard !usable.isEmpty else { return "" }

        // Bottom-left origin: larger midY is higher on the page.
        let sorted = usable.sorted { $0.boundingBox.midY > $1.boundingBox.midY }
        var rows: [[Observation]] = []
        for observation in sorted {
            if var row = rows.last, let anchor = row.first,
               abs(anchor.boundingBox.midY - observation.boundingBox.midY)
                   < max(anchor.boundingBox.height, observation.boundingBox.height) / 2 {
                row.append(observation)
                rows[rows.count - 1] = row
            } else {
                rows.append([observation])
            }
        }
        return rows
            .map { row in
                row.sorted { $0.boundingBox.minX < $1.boundingBox.minX }
                    .map(\.text)
                    .joined(separator: " ")
            }
            .joined(separator: "\n")
    }
}
