from __future__ import annotations

import math
from collections import deque


class DoubleCircleGestureDetector:
    """Recognize two quick, similarly sized circles from cursor positions."""

    WINDOW_SECONDS = 1.8
    COOLDOWN_SECONDS = 1.4
    MIN_DURATION = 0.45
    MIN_STEP = 2.0
    MIN_POINTS = 18
    MIN_DIAMETER = 24.0
    MAX_DIAMETER = 180.0
    MAX_ASPECT_RATIO = 2.2
    MAX_RADIUS_VARIATION = 0.42
    MIN_DIRECTIONALITY = 0.72
    MIN_ANGULAR_TRAVEL = math.pi * 3.4

    def __init__(self) -> None:
        self._points: deque[tuple[float, float, float]] = deque()
        self._cooldown_until = 0.0

    def reset(self) -> None:
        self._points.clear()

    def feed(self, x: int, y: int, now: float) -> bool:
        if now < self._cooldown_until:
            self._points.clear()
            return False

        while self._points and now - self._points[0][0] > self.WINDOW_SECONDS:
            self._points.popleft()

        if self._points:
            _last_time, last_x, last_y = self._points[-1]
            if math.hypot(x - last_x, y - last_y) < self.MIN_STEP:
                return False

        self._points.append((now, float(x), float(y)))
        if len(self._points) < self.MIN_POINTS:
            return False

        points = list(self._points)
        latest_time = points[-1][0]
        for start in range(0, len(points) - self.MIN_POINTS + 1):
            candidate = points[start:]
            duration = latest_time - candidate[0][0]
            if duration < self.MIN_DURATION:
                break
            if self._looks_like_double_circle(candidate):
                self._points.clear()
                self._cooldown_until = now + self.COOLDOWN_SECONDS
                return True
        return False

    def _looks_like_double_circle(
        self,
        points: list[tuple[float, float, float]],
    ) -> bool:
        xs = [point[1] for point in points]
        ys = [point[2] for point in points]
        width = max(xs) - min(xs)
        height = max(ys) - min(ys)
        diameter = max(width, height)
        smaller_diameter = min(width, height)
        if smaller_diameter < self.MIN_DIAMETER or diameter > self.MAX_DIAMETER:
            return False
        if diameter / smaller_diameter > self.MAX_ASPECT_RATIO:
            return False

        center_x = (min(xs) + max(xs)) / 2.0
        center_y = (min(ys) + max(ys)) / 2.0
        radii = [math.hypot(x - center_x, y - center_y) for x, y in zip(xs, ys)]
        mean_radius = sum(radii) / len(radii)
        if mean_radius <= 0:
            return False
        variance = sum((radius - mean_radius) ** 2 for radius in radii) / len(radii)
        if math.sqrt(variance) / mean_radius > self.MAX_RADIUS_VARIATION:
            return False
        if min(radii) < mean_radius * 0.25:
            return False

        closure_distance = math.hypot(xs[-1] - xs[0], ys[-1] - ys[0])
        if closure_distance > max(18.0, mean_radius * 0.8):
            return False

        angles = [math.atan2(y - center_y, x - center_x) for x, y in zip(xs, ys)]
        signed_travel = 0.0
        absolute_travel = 0.0
        for previous, current in zip(angles, angles[1:]):
            delta = (current - previous + math.pi) % (2.0 * math.pi) - math.pi
            signed_travel += delta
            absolute_travel += abs(delta)
        if abs(signed_travel) < self.MIN_ANGULAR_TRAVEL:
            return False
        if absolute_travel <= 0 or abs(signed_travel) / absolute_travel < self.MIN_DIRECTIONALITY:
            return False
        return True
