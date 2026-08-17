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

    def test_video_recording_has_independent_tray_state(self) -> None:
        tray = TrayManager(lambda _event: None)

        tray.set_state(recording=False, hidden=False, video=True)

        self.assertFalse(tray.recording)
        self.assertTrue(tray.video_recording)


if __name__ == "__main__":
    unittest.main()
