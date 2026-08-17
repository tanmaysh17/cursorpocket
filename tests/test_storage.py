from __future__ import annotations

import json
import tempfile
import unittest
import wave
from datetime import datetime, timezone
from pathlib import Path

from PIL import Image

from cursorpocket.storage import CaptureStore, compact_preview, is_web_url


class CaptureStoreTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.store = CaptureStore(self.root)
        self.now = datetime(2026, 8, 16, 14, 5, 9, tzinfo=timezone.utc)

    def tearDown(self) -> None:
        self.temp.cleanup()

    def test_saves_every_capture_type_and_returns_newest_first(self) -> None:
        text = self.store.save_text("A useful text snippet", now=self.now)
        link = self.store.save_link("https://example.com/docs", now=self.now)
        screenshot = self.store.save_image(
            Image.new("RGB", (80, 40), "#63B3FF"),
            (-20, 10, 60, 50),
            now=self.now,
        )
        audio_fixture = self.root / "fixture.wav"
        with wave.open(str(audio_fixture), "wb") as wav_file:
            wav_file.setnchannels(1)
            wav_file.setsampwidth(2)
            wav_file.setframerate(16000)
            wav_file.writeframes(b"\x00\x00" * 3200)
        audio = self.store.save_audio_file(audio_fixture, 0.2, now=self.now)

        records = self.store.recent(4)

        self.assertEqual([item.kind for item in records], ["audio", "screenshot", "link", "text"])
        self.assertEqual(screenshot.metadata["bounds"], [-20, 10, 60, 50])
        self.assertEqual(audio.preview, "Audio · 0:01")
        for record in (text, link, screenshot, audio):
            self.assertTrue(self.store.absolute_path(record).exists())

    def test_manifest_is_json_lines_and_paths_are_relative(self) -> None:
        record = self.store.save_text("hello", now=self.now)
        payload = json.loads(self.store.manifest_path.read_text(encoding="utf-8"))

        self.assertEqual(payload["id"], record.id)
        self.assertFalse(Path(payload["path"]).is_absolute())
        self.assertEqual(Path(payload["path"]).parts[:2], ("2026-08-16", "text"))

    def test_recent_skips_a_corrupt_manifest_line(self) -> None:
        record = self.store.save_text("still readable", now=self.now)
        with self.store.manifest_path.open("a", encoding="utf-8") as manifest:
            manifest.write("{not-json}\n")

        self.assertEqual(self.store.recent(1)[0].id, record.id)

    def test_empty_text_and_invalid_link_are_rejected(self) -> None:
        with self.assertRaises(ValueError):
            self.store.save_text("   ", now=self.now)
        with self.assertRaises(ValueError):
            self.store.save_link("example.com/no-scheme", now=self.now)


class ValidationTests(unittest.TestCase):
    def test_web_url_validation(self) -> None:
        self.assertTrue(is_web_url("https://clicky.foo/"))
        self.assertTrue(is_web_url("http://localhost:3000/path"))
        self.assertFalse(is_web_url("javascript:alert(1)"))
        self.assertFalse(is_web_url("file:///C:/secrets.txt"))
        self.assertFalse(is_web_url("not a link"))

    def test_preview_is_single_line_and_bounded(self) -> None:
        preview = compact_preview("one\n\n two " + "x" * 100, limit=24)
        self.assertNotIn("\n", preview)
        self.assertEqual(len(preview), 24)
        self.assertTrue(preview.endswith("…"))


if __name__ == "__main__":
    unittest.main()
