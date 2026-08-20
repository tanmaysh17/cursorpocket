from __future__ import annotations

import unittest

from cursorpocket.tray import TrayManager


class TrayIconTests(unittest.TestCase):
    def test_branded_icon_turns_red_while_recording(self) -> None:
        idle = TrayManager._image(False)
        recording = TrayManager._image(True)

        self.assertEqual(idle.size, (64, 64))
        self.assertIsNotNone(idle.getbbox())
        self.assertNotEqual(idle.tobytes(), recording.tobytes())
        # The whole mark changes colour rather than gaining a corner badge, which
        # at 16 px would cover most of the icon.
        self.assertEqual(idle.getpixel((32, 44)), (69, 224, 140, 255))
        self.assertEqual(recording.getpixel((32, 44)), (255, 95, 107, 255))

    def test_recording_icon_keeps_the_dark_ground(self) -> None:
        recording = TrayManager._image(True)

        # Recolouring must repaint the mark only, never the ground behind it.
        self.assertEqual(recording.getpixel((6, 32)), (13, 20, 18, 255))

    def test_video_recording_has_independent_tray_state(self) -> None:
        tray = TrayManager(lambda _event: None)

        tray.set_state(recording=False, hidden=False, video=True)

        self.assertFalse(tray.recording)
        self.assertTrue(tray.video_recording)


if __name__ == "__main__":
    unittest.main()
