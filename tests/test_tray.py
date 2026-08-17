from __future__ import annotations

import unittest

from cursorpocket.tray import TrayManager


class TrayIconTests(unittest.TestCase):
    def test_branded_icon_adds_a_red_recording_badge(self) -> None:
        idle = TrayManager._image(False)
        recording = TrayManager._image(True)

        self.assertEqual(idle.size, (64, 64))
        self.assertIsNotNone(idle.getbbox())
        self.assertNotEqual(idle.tobytes(), recording.tobytes())
        self.assertEqual(recording.getpixel((53, 53)), (255, 93, 104, 255))


if __name__ == "__main__":
    unittest.main()
