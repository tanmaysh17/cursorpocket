from __future__ import annotations

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
                onboarding_seen=True,
                panel_geometry="560x740+100+80",
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
            self.assertFalse(settings.onboarding_seen)
            self.assertEqual(settings.panel_geometry, "")
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


if __name__ == "__main__":
    unittest.main()
