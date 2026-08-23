from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from cursorpocket.settings import AppSettings, SettingsStore


class SettingsStoreTests(unittest.TestCase):
    def test_round_trip(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "settings.json"
            store = SettingsStore(path)
            expected = AppSettings(
                capture_dir="D:/Captures",
                follow_cursor=False,
                mouse_gesture_enabled=False,
                onboarding_seen=True,
                panel_geometry="560x740+100+80",
                video_microphone_enabled=False,
                video_camera_enabled=True,
                video_microphone_name="Desk mic",
                video_camera_name="Integrated camera",
                video_source_kind="region",
                video_camera_position="top-right",
                video_camera_width=480,
                video_fps=60,
                video_countdown_seconds=0,
                video_draw_cursor=False,
            )

            store.save(expected)
            actual = store.load()

            self.assertEqual(actual, expected)

    def test_corrupt_file_falls_back_to_defaults(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "settings.json"
            path.write_text("not-json", encoding="utf-8")

            settings = SettingsStore(path).load()

            self.assertTrue(settings.follow_cursor)
            self.assertTrue(settings.mouse_gesture_enabled)
            self.assertFalse(settings.onboarding_seen)
            self.assertEqual(settings.panel_geometry, "")
            self.assertTrue(settings.video_microphone_enabled)
            self.assertFalse(settings.video_camera_enabled)
            self.assertEqual(settings.video_source_kind, "display")
            self.assertEqual(settings.video_camera_width, 360)
            self.assertEqual(settings.video_fps, 30)
            self.assertEqual(settings.video_countdown_seconds, 3)
            self.assertTrue(settings.video_draw_cursor)
            self.assertIn("CursorPocket Captures", settings.capture_dir)

    def test_older_settings_file_gets_safe_onboarding_default(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "settings.json"
            path.write_text(
                '{"capture_dir": "D:/Captures", "follow_cursor": true}',
                encoding="utf-8",
            )

            settings = SettingsStore(path).load()

            self.assertFalse(settings.onboarding_seen)
            self.assertTrue(settings.mouse_gesture_enabled)

    def test_invalid_video_choices_are_normalized(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            path = Path(temp_dir) / "settings.json"
            path.write_text(
                json.dumps(
                    {
                        "capture_dir": "D:/Captures",
                        "video_source_kind": "entire-internet",
                        "video_camera_position": "middle",
                        "video_camera_width": 999,
                        "video_fps": 999,
                        "video_countdown_seconds": 99,
                    }
                ),
                encoding="utf-8",
            )

            settings = SettingsStore(path).load()

            self.assertEqual(settings.video_source_kind, "display")
            self.assertEqual(settings.video_camera_position, "bottom-right")
            self.assertEqual(settings.video_camera_width, 360)
            self.assertEqual(settings.video_fps, 30)
            self.assertEqual(settings.video_countdown_seconds, 3)


if __name__ == "__main__":
    unittest.main()
