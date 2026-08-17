from __future__ import annotations

import unittest

from cursorpocket.tray import TrayManager


class TrayIconTests(unittest.TestCase):
    def test_icon_changes_from_green_to_red_while_recording(self) -> None:
        idle = TrayManager._image(False)
        recording = TrayManager._image(True)

        self.assertEqual(idle.getpixel((32, 32)), (66, 211, 146, 255))
        self.assertEqual(recording.getpixel((32, 32)), (255, 93, 104, 255))


if __name__ == "__main__":
    unittest.main()
