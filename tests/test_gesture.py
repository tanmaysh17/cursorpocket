from __future__ import annotations

import math
import unittest

from cursorpocket.gesture import DoubleCircleGestureDetector


def circle_points(
    turns: float,
    duration: float,
    radius: float = 32.0,
    samples: int = 90,
) -> list[tuple[int, int, float]]:
    return [
        (
            round(200 + radius * math.cos(turns * 2 * math.pi * index / samples)),
            round(160 + radius * math.sin(turns * 2 * math.pi * index / samples)),
            duration * index / samples,
        )
        for index in range(samples + 1)
    ]


class DoubleCircleGestureTests(unittest.TestCase):
    def test_two_quick_circles_trigger_once(self) -> None:
        detector = DoubleCircleGestureDetector()
        triggers = [detector.feed(x, y, now) for x, y, now in circle_points(2, 1.1)]

        self.assertEqual(triggers.count(True), 1)

    def test_one_circle_does_not_trigger(self) -> None:
        detector = DoubleCircleGestureDetector()

        triggered = any(
            detector.feed(x, y, now)
            for x, y, now in circle_points(1, 0.8)
        )

        self.assertFalse(triggered)

    def test_slow_or_straight_motion_does_not_trigger(self) -> None:
        slow_detector = DoubleCircleGestureDetector()
        straight_detector = DoubleCircleGestureDetector()
        slow = any(
            slow_detector.feed(x, y, now)
            for x, y, now in circle_points(2, 2.8)
        )
        straight = any(
            straight_detector.feed(100 + index * 4, 200 + (index % 2), index * 0.02)
            for index in range(80)
        )

        self.assertFalse(slow)
        self.assertFalse(straight)

    def test_clockwise_circles_also_trigger(self) -> None:
        detector = DoubleCircleGestureDetector()
        clockwise = [(x, 320 - y, now) for x, y, now in circle_points(2, 1.0)]

        self.assertTrue(any(detector.feed(x, y, now) for x, y, now in clockwise))


if __name__ == "__main__":
    unittest.main()
