from __future__ import annotations

import argparse
import subprocess
import tempfile
import wave
from pathlib import Path

from PIL import Image

from cursorpocket.app import CursorPocketApp
from cursorpocket.storage import CaptureStore
from cursorpocket.video import bundled_ffmpeg_path, inspect_video_file, probe_video_capabilities
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
        ffmpeg_path = bundled_ffmpeg_path()
        capabilities = probe_video_capabilities(ffmpeg_path)
        if not capabilities.ready:
            raise RuntimeError("The local video component is missing required capture features.")
        reservation = store.reserve_video({"self_test": True})
        video_command = [
            str(ffmpeg_path),
            "-y",
            "-hide_banner",
            "-loglevel",
            "error",
            "-f",
            "lavfi",
            "-i",
            "testsrc2=size=640x360:rate=30",
            "-f",
            "lavfi",
            "-i",
            "sine=frequency=440:sample_rate=48000",
            "-t",
            "1",
            "-c:v",
            "h264_mf",
            "-rate_control",
            "quality",
            "-quality",
            "70",
            "-pix_fmt",
            "nv12",
            "-c:a",
            "aac",
            str(reservation.partial_path),
        ]
        completed = subprocess.run(video_command, capture_output=True, timeout=20, check=False)
        if completed.returncode != 0:
            detail = completed.stderr.decode("utf-8", errors="replace")[-800:]
            raise RuntimeError(f"Video encode self-test failed: {detail}")
        inspection = inspect_video_file(ffmpeg_path, reservation.partial_path)
        video = store.finalize_video(
            reservation,
            inspection.duration_seconds,
            inspection.width,
            inspection.height,
            inspection.fps,
            {"has_audio": inspection.has_audio, "self_test": True},
        )
        records = store.recent(5)
        expected = {text.id, link.id, image.id, audio.id, video.id}
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
    app = CursorPocketApp(activation_check=instance.consume_activation)
    try:
        app.run()
    finally:
        instance.close()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
