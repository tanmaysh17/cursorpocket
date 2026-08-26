import Foundation

/// Recognises two quick circles drawn with the pointer — the signature way to
/// open the command palette, ported threshold-for-threshold from the Windows
/// `DoubleCircleGestureDetector`.
///
/// The thresholds are deliberately permissive about SIZE and SPEED — a tiny
/// flick of the wrist and a wide sweep of the whole arm both count, drawn
/// fast or slow — and strict only about SHAPE: the path has to loop
/// consistently around one centre, in one direction, for clearly more than a
/// single turn. That split is what keeps the gesture easy to perform without
/// firing during ordinary mouse work. The shape thresholds have been tuned in
/// both directions already on Windows; do not loosen them.
///
/// This runs on the global mouse monitor for every pointer move, so samples
/// live in a fixed ring buffer, each candidate window is evaluated in place
/// by index, and a running signed-heading total decides whether the geometric
/// test is worth reaching at all.
public final class DoubleCircleGestureDetector {
    // These sit midway between the original strict thresholds and a much
    // looser pass that turned out to trigger during ordinary mouse work.
    private static let windowSeconds = 3.0
    private static let cooldownSeconds = 1.4
    private static let minimumDuration = 0.18
    private static let minimumStep = 2.0
    private static let minimumPoints = 14
    // From a wrist circle to a wide sweep, without treating a sweep across a
    // whole 4K display as a gesture.
    private static let minimumDiameter = 18.0
    private static let maximumDiameter = 520.0
    private static let maximumAspectRatio = 2.4
    private static let maximumRadiusVariation = 0.46
    private static let minimumDirectionality = 0.68
    // Two loops, near enough. This is the strongest guard against false
    // positives, so it stays where it started.
    private static let minimumAngularTravel = Double.pi * 3.4
    // Bounds the cost of the sliding-window scan below.
    private static let maximumPoints = 320
    private static let maximumCandidates = 40

    // Heading measured between consecutive samples only approximates the
    // centre-relative travel the geometric test computes, so this gate sits
    // well below minimumAngularTravel. It exists to skip work, never to
    // reject a gesture: the geometric test remains the only accepter.
    private static let headingGateFactor = 0.55
    private static let headingRebaseLimit = 1e6

    private struct GesturePoint {
        var time: Double
        var x: Double
        var y: Double
        var cumulativeHeading: Double
    }

    private var points = [GesturePoint](
        repeating: GesturePoint(time: 0, x: 0, y: 0, cumulativeHeading: 0),
        count: DoubleCircleGestureDetector.maximumPoints)
    private var oldest = 0
    private var count = 0
    private var cooldownUntil = 0.0
    private var heading = 0.0
    private var cumulativeHeading = 0.0

    public init() {}

    public func feed(x: Double, y: Double, now: Double) -> Bool {
        if now < cooldownUntil {
            reset()
            return false
        }
        while count > 0, now - point(0).time > Self.windowSeconds {
            dropOldest()
        }
        if count > 0 {
            let last = point(count - 1)
            if Self.squaredDistance(x, y, last.x, last.y) < Self.minimumStep * Self.minimumStep {
                return false
            }
        }
        append(x: x, y: y, now: now)
        if count < Self.minimumPoints {
            return false
        }

        let end = count - 1
        let lastStart = count - Self.minimumPoints
        if !headingGateOpen(lastStart: lastStart, end: end) {
            return false
        }

        // Test the newest candidate first and stride through older ones, so a
        // long trail of movement cannot turn this into a quadratic scan.
        let stride = max(1, Int((Double(lastStart + 1) / Double(Self.maximumCandidates)).rounded(.up)))
        var start = 0
        while start <= lastStart {
            if point(end).time - point(start).time < Self.minimumDuration {
                break
            }
            if looksLikeDoubleCircle(start: start, end: end) {
                reset()
                cooldownUntil = now + Self.cooldownSeconds
                return true
            }
            start += stride
        }
        return false
    }

    public func reset() {
        oldest = 0
        count = 0
        heading = 0
        cumulativeHeading = 0
    }

    private func headingGateOpen(lastStart: Int, end: Int) -> Bool {
        // The signed heading total is kept per sample, so the travel of any
        // candidate suffix is a single subtraction. Straight motion
        // accumulates nothing; doubling back cancels itself out.
        let endHeading = point(end).cumulativeHeading
        var widest = 0.0
        for start in 0...lastStart {
            let travel = abs(endHeading - point(start).cumulativeHeading)
            if travel > widest {
                widest = travel
            }
        }
        return widest >= Self.minimumAngularTravel * Self.headingGateFactor
    }

    private func looksLikeDoubleCircle(start: Int, end: Int) -> Bool {
        var minX = Double.greatestFiniteMagnitude
        var maxX = -Double.greatestFiniteMagnitude
        var minY = Double.greatestFiniteMagnitude
        var maxY = -Double.greatestFiniteMagnitude
        for index in start...end {
            let p = point(index)
            if p.x < minX { minX = p.x }
            if p.x > maxX { maxX = p.x }
            if p.y < minY { minY = p.y }
            if p.y > maxY { maxY = p.y }
        }
        let width = maxX - minX
        let height = maxY - minY
        let diameter = max(width, height)
        let smaller = min(width, height)
        if smaller < Self.minimumDiameter || diameter > Self.maximumDiameter
            || diameter / smaller > Self.maximumAspectRatio {
            return false
        }

        let centerX = (minX + maxX) / 2
        let centerY = (minY + maxY) / 2
        let sampleCount = Double(end - start + 1)
        var radiusSum = 0.0
        var smallestRadius = Double.greatestFiniteMagnitude
        for index in start...end {
            let p = point(index)
            let radius = Self.squaredDistance(p.x, p.y, centerX, centerY).squareRoot()
            radiusSum += radius
            if radius < smallestRadius {
                smallestRadius = radius
            }
        }
        let meanRadius = radiusSum / sampleCount
        if meanRadius <= 0 || smallestRadius < meanRadius * 0.25 {
            return false
        }

        var varianceSum = 0.0
        for index in start...end {
            let p = point(index)
            let deviation = Self.squaredDistance(p.x, p.y, centerX, centerY).squareRoot() - meanRadius
            varianceSum += deviation * deviation
        }
        let variation = (varianceSum / sampleCount).squareRoot() / meanRadius
        let first = point(start)
        let last = point(end)
        let closingGap = max(20, meanRadius * 0.9)
        if variation > Self.maximumRadiusVariation
            || Self.squaredDistance(first.x, first.y, last.x, last.y) > closingGap * closingGap {
            return false
        }

        var signedTravel = 0.0
        var absoluteTravel = 0.0
        var previousAngle = atan2(first.y - centerY, first.x - centerX)
        for index in (start + 1)...end {
            let p = point(index)
            let angle = atan2(p.y - centerY, p.x - centerX)
            let delta = Self.normalize(angle - previousAngle)
            previousAngle = angle
            signedTravel += delta
            absoluteTravel += abs(delta)
        }
        return abs(signedTravel) >= Self.minimumAngularTravel
            && absoluteTravel > 0
            && abs(signedTravel) / absoluteTravel >= Self.minimumDirectionality
    }

    private func append(x: Double, y: Double, now: Double) {
        if count > 0 {
            let last = point(count - 1)
            let newHeading = atan2(y - last.y, x - last.x)
            cumulativeHeading += count > 1 ? Self.normalize(newHeading - heading) : 0
            heading = newHeading
        } else {
            heading = 0
            cumulativeHeading = 0
        }
        if abs(cumulativeHeading) > Self.headingRebaseLimit {
            rebaseHeading()
        }

        if count == Self.maximumPoints {
            dropOldest()
        }
        points[(oldest + count) % Self.maximumPoints] = GesturePoint(
            time: now, x: x, y: y, cumulativeHeading: cumulativeHeading)
        count += 1
    }

    private func rebaseHeading() {
        let offset = cumulativeHeading
        for index in 0..<count {
            let slot = (oldest + index) % Self.maximumPoints
            points[slot].cumulativeHeading -= offset
        }
        cumulativeHeading = 0
    }

    private func dropOldest() {
        oldest = (oldest + 1) % Self.maximumPoints
        count -= 1
    }

    private func point(_ index: Int) -> GesturePoint {
        points[(oldest + index) % Self.maximumPoints]
    }

    private static func squaredDistance(_ x1: Double, _ y1: Double, _ x2: Double, _ y2: Double) -> Double {
        let dx = x1 - x2
        let dy = y1 - y2
        return dx * dx + dy * dy
    }

    private static func normalize(_ radians: Double) -> Double {
        var value = radians
        while value > .pi { value -= .pi * 2 }
        while value < -.pi { value += .pi * 2 }
        return value
    }
}
