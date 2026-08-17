from __future__ import annotations

import argparse
import tempfile
import wave
from pathlib import Path

from PIL import Image

from cursorpocket.app import CursorPocketApp
from cursorpocket.storage import CaptureStore
from cursorpocket.windows import SingleInstance, enable_dpi_awareness


def run_self_test() -> int:
    with tempfile.TemporaryDirectory(prefix="cursorpocket-") as temp_dir:
        store = CaptureStore(Path(temp_dir))
        text = store.save_text("CursorPocket self-test note")
        link = store.save_link("https://example.com/cursorpocket")
        image = store.save_image(Image.new("RGB", (48, 32), "#63B3FF"), (0, 0, 48, 32))
        audio_fixture = Path(temp_dir) / "fixture.wav"
        with wave.open(str(audio_fixture), "wb") as wav_file:
            wav_file.setnchannels(1)
            wav_file.setsampwidth(2)
            wav_file.setframerate(16000)
            wav_file.writeframes(b"\x00\x00" * 1600)
        audio = store.save_audio_file(audio_fixture, duration_seconds=0.1)
        records = store.recent(4)
        expected = {text.id, link.id, image.id, audio.id}
        actual = {record.id for record in records}
        if actual != expected:
            raise RuntimeError("Capture index did not return the saved records.")
        for record in records:
            if not store.absolute_path(record).exists():
                raise RuntimeError(f"Missing capture file: {record.path}")
    print("CursorPocket self-test passed.")
    return 0


def main() -> int:
    parser = argparse.ArgumentParser(description="CursorPocket local capture companion")
    parser.add_argument("--self-test", action="store_true", help="verify capture storage without opening the UI")
    args = parser.parse_args()
    if args.self_test:
        return run_self_test()
    instance = SingleInstance()
    if not instance.acquired:
        return 0
    enable_dpi_awareness()
    app = CursorPocketApp()
    try:
        app.run()
    finally:
        instance.close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
