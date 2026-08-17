from __future__ import annotations

import argparse
import subprocess
import tempfile
import time
from pathlib import Path

from cursorpocket.video import bundled_ffmpeg_path, inspect_video_file


def _base_command(ffmpeg: Path) -> list[str]:
    return [str(ffmpeg), "-y", "-hide_banner", "-loglevel", "error"]


def _encode_fixture(
    ffmpeg: Path,
    output: Path,
    *,
    camera: bool,
    audio: bool,
) -> None:
    command = _base_command(ffmpeg)
    command.extend(["-f", "lavfi", "-i", "testsrc2=size=1280x720:rate=30"])
    if camera:
        command.extend(["-f", "lavfi", "-i", "testsrc2=size=640x360:rate=30"])
    if audio:
        command.extend(["-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000"])
    filters = ["[0:v]setpts=PTS-STARTPTS,format=yuv420p[screen]"]
    if camera:
        filters.extend(
            [
                "[1:v]setpts=PTS-STARTPTS,scale=360:202,format=yuv420p[cam]",
                "[screen][cam]overlay=W-w-32:H-h-32:shortest=0,format=nv12[video]",
            ]
        )
    else:
        filters.append("[screen]format=nv12[video]")
    command.extend(["-filter_complex", ";".join(filters), "-map", "[video]"])
    if audio:
        audio_index = 2 if camera else 1
        command.extend(["-map", f"{audio_index}:a", "-c:a", "aac", "-b:a", "128k"])
    else:
        command.append("-an")
    command.extend(
        [
            "-t",
            "1",
            "-c:v",
            "h264_mf",
            "-rate_control",
            "quality",
            "-quality",
            "72",
            "-movflags",
            "+frag_keyframe+empty_moov+default_base_moof",
            str(output),
        ]
    )
    completed = subprocess.run(command, capture_output=True, timeout=30, check=False)
    if completed.returncode != 0:
        detail = completed.stderr.decode("utf-8", errors="replace")[-1200:]
        raise RuntimeError(f"Fixture encode failed: {detail}")


def _verify_interrupted_fragment(ffmpeg: Path, output: Path) -> None:
    command = _base_command(ffmpeg) + [
        "-re",
        "-f",
        "lavfi",
        "-i",
        "testsrc2=size=640x360:rate=30",
        "-t",
        "20",
        "-c:v",
        "h264_mf",
        "-rate_control",
        "quality",
        "-quality",
        "72",
        "-g",
        "30",
        "-an",
        "-movflags",
        "+frag_keyframe+empty_moov+default_base_moof",
        str(output),
    ]
    process = subprocess.Popen(
        command,
        stdin=subprocess.DEVNULL,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        creationflags=subprocess.CREATE_NO_WINDOW,
    )
    time.sleep(3.0)
    process.terminate()
    process.wait(timeout=5.0)
    inspection = inspect_video_file(ffmpeg, output)
    if inspection.duration_seconds < 1.0:
        raise RuntimeError("Interrupted fragmented MP4 did not retain useful media.")


def main() -> int:
    parser = argparse.ArgumentParser(description="Verify CursorPocket synthetic video pipelines")
    parser.add_argument("--ffmpeg", type=Path, default=bundled_ffmpeg_path())
    args = parser.parse_args()
    ffmpeg = args.ffmpeg.resolve()
    with tempfile.TemporaryDirectory(prefix="cursorpocket-media-") as temp_dir:
        root = Path(temp_dir)
        cases = (
            ("screen", False, False),
            ("screen-mic", False, True),
            ("screen-camera", True, False),
            ("screen-camera-mic", True, True),
        )
        for name, camera, audio in cases:
            output = root / f"{name}.mp4"
            _encode_fixture(ffmpeg, output, camera=camera, audio=audio)
            inspection = inspect_video_file(ffmpeg, output)
            if inspection.width != 1280 or inspection.height != 720:
                raise RuntimeError(f"Unexpected dimensions for {name}: {inspection}")
            if inspection.has_audio != audio:
                raise RuntimeError(f"Unexpected audio streams for {name}: {inspection}")
            print(f"PASS {name}: {inspection}")
        interrupted = root / "interrupted.partial.mp4"
        _verify_interrupted_fragment(ffmpeg, interrupted)
        print(f"PASS interrupted recovery: {inspect_video_file(ffmpeg, interrupted)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
