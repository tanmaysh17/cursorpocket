from __future__ import annotations

import unittest

from cursorpocket.tray import TrayManager


class TrayIconTests(unittest.TestCase):
    def test_brand_identity_is_stable_while_recording(self) -> None:
        idle = TrayManager._image(False)
        recording = TrayManager._image(True)

        self.assertEqual(idle.size, (64, 64))
        self.assertIsNotNone(idle.getbbox())
        self.assertEqual(idle.tobytes(), recording.tobytes())

    def test_brand_icon_keeps_transparent_negative_space(self) -> None:
        icon = TrayManager._image(False)

        self.assertEqual(icon.mode, "RGBA")
        self.assertEqual(icon.getchannel("A").getextrema(), (0, 255))

    def test_video_recording_has_independent_tray_state(self) -> None:
        tray = TrayManager(lambda _event: None)

        tray.set_state(recording=False, hidden=False, video=True)

        self.assertFalse(tray.recording)
        self.assertTrue(tray.video_recording)


if __name__ == "__main__":
    unittest.main()
