from __future__ import annotations

import os
import re
import subprocess
import sys
import threading
from collections import deque
from dataclasses import dataclass
from enum import Enum
from pathlib import Path
from typing import Callable


class RecordingState(str, Enum):
    IDLE = "idle"
    STARTING = "starting"
    RECORDING = "recording"
    STOPPING = "stopping"
    FAILED = "failed"


class VideoSourceKind(str, Enum):
    DISPLAY = "display"
    REGION = "region"
    WINDOW = "window"


@dataclass(frozen=True)
class VideoOptions:
    source_kind: VideoSourceKind = VideoSourceKind.DISPLAY
    display_index: int = 0
    bounds: tuple[int, int, int, int] | None = None
    window_handle: int | None = None
    fps: int = 30
    quality: int = 72
    draw_cursor: bool = True
    include_microphone: bool = True
    microphone_name: str = ""
    include_camera: bool = False
    camera_name: str = ""
    camera_position: str = "bottom-right"
    camera_width: int = 360


@dataclass(frozen=True)
class VideoCapabilities:
    version: str
    lgpl_only: bool
    ddagrab: bool
    gdigrab: bool
    dshow: bool
    h264_mf: bool
    aac: bool
    mp4: bool

    @property
    def ready(self) -> bool:
        return all(
            (
                self.lgpl_only,
                self.ddagrab,
                self.gdigrab,
                self.dshow,
                self.h264_mf,
                self.aac,
                self.mp4,
            )
        )


@dataclass(frozen=True)
class VideoInspection:
    duration_seconds: float
    width: int
    height: int
    fps: float
    has_audio: bool


@dataclass(frozen=True)
class VideoProcessResult:
    output_path: Path
    return_code: int
    stop_requested: bool
    discard_requested: bool
    forced: bool
    error_detail: str


def bundled_ffmpeg_path() -> Path:
    if getattr(sys, "frozen", False):
        beside_executable = Path(sys.executable).resolve().parent / "ffmpeg.exe"
        if beside_executable.exists():
            return beside_executable
        bundle_root = Path(getattr(sys, "_MEIPASS", Path(sys.executable).parent))
        return bundle_root / "ffmpeg.exe"
    return Path(__file__).resolve().parent.parent / "third_party" / "ffmpeg" / "bin" / "ffmpeg.exe"


def _run_text(ffmpeg_path: Path | str, *arguments: str, timeout: float = 8.0) -> str:
    completed = subprocess.run(
        [str(ffmpeg_path), *arguments],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=timeout,
        check=False,
    )
    return completed.stdout + "\n" + completed.stderr


def probe_video_capabilities(ffmpeg_path: Path | str) -> VideoCapabilities:
    version_output = _run_text(ffmpeg_path, "-hide_banner", "-version")
    devices = _run_text(ffmpeg_path, "-hide_banner", "-devices")
    filters = _run_text(ffmpeg_path, "-hide_banner", "-filters")
    encoders = _run_text(ffmpeg_path, "-hide_banner", "-encoders")
    muxers = _run_text(ffmpeg_path, "-hide_banner", "-muxers")
    first_line = next((line for line in version_output.splitlines() if line.strip()), "")
    configuration = version_output.lower()
    return VideoCapabilities(
        version=first_line.strip(),
        lgpl_only="--enable-gpl" not in configuration and "--enable-nonfree" not in configuration,
        ddagrab=bool(re.search(r"\bddagrab\b", filters)),
        gdigrab=bool(re.search(r"\bgdigrab\b", devices)),
        dshow=bool(re.search(r"\bdshow\b", devices)),
        h264_mf=bool(re.search(r"\bh264_mf\b", encoders)),
        aac=bool(re.search(r"\baac\b", encoders)),
        mp4=bool(re.search(r"\bmp4\b", muxers)),
    )


def _normalized_bounds(bounds: tuple[int, int, int, int] | None) -> tuple[int, int, int, int]:
    if bounds is None:
        raise ValueError("A recording region is required.")
    left, top, right, bottom = bounds
    width = right - left
    height = bottom - top
    if width < 2 or height < 2:
        raise ValueError("The recording region is empty.")
    return left, top, width - width % 2, height - height % 2


def _overlay_position(position: str) -> str:
    return {
        "top-left": "32:32",
        "top-right": "W-w-32:32",
        "bottom-left": "32:H-h-32",
        "bottom-right": "W-w-32:H-h-32",
    }.get(position, "W-w-32:H-h-32")


def build_ffmpeg_command(
    ffmpeg_path: Path | str,
    output_path: Path | str,
    options: VideoOptions,
) -> list[str]:
    if options.fps not in {15, 24, 30, 60}:
        raise ValueError("Video frame rate must be 15, 24, 30, or 60 fps.")
    if not 1 <= options.quality <= 100:
        raise ValueError("Video quality must be between 1 and 100.")
    if options.include_microphone and not options.microphone_name.strip():
        raise ValueError("Choose a microphone or turn microphone recording off.")
    if options.include_camera and not options.camera_name.strip():
        raise ValueError("Choose a camera or turn webcam recording off.")

    command = [str(ffmpeg_path), "-n", "-hide_banner", "-loglevel", "warning"]
    microphone_index: int | None = None
    camera_index: int | None = None
    input_index = 0

    # Open physical devices before Desktop Duplication. DirectShow initialization can
    # take several seconds; opening the screen first starves its initial frame queue.
    if options.include_microphone:
        microphone_index = input_index
        input_index += 1
        command.extend(
            [
                "-thread_queue_size",
                "2048",
                "-rtbufsize",
                "64M",
                "-f",
                "dshow",
                "-audio_buffer_size",
                "50",
                "-i",
                f"audio={options.microphone_name}",
            ]
        )
    if options.include_camera:
        camera_index = input_index
        input_index += 1
        command.extend(
            [
                "-thread_queue_size",
                "2048",
                "-rtbufsize",
                "256M",
                "-f",
                "dshow",
                "-video_size",
                "640x360",
                "-framerate",
                str(options.fps),
                "-pixel_format",
                "nv12",
                "-i",
                f"video={options.camera_name}",
            ]
        )

    screen_index = input_index
    draw_mouse = "1" if options.draw_cursor else "0"
    if options.source_kind == VideoSourceKind.DISPLAY:
        command.extend(
            [
                "-thread_queue_size",
                "1024",
                "-f",
                "lavfi",
                "-i",
                (
                    f"ddagrab=output_idx={max(0, options.display_index)}:"
                    f"framerate={options.fps}:draw_mouse={draw_mouse}"
                ),
            ]
        )
        screen_filter = (
            f"[{screen_index}:v]hwdownload,format=bgra,setpts=PTS-STARTPTS,"
            "format=yuv420p[screen]"
        )
    elif options.source_kind == VideoSourceKind.REGION:
        left, top, width, height = _normalized_bounds(options.bounds)
        command.extend(
            [
                "-thread_queue_size",
                "1024",
                "-f",
                "gdigrab",
                "-framerate",
                str(options.fps),
                "-draw_mouse",
                draw_mouse,
                "-offset_x",
                str(left),
                "-offset_y",
                str(top),
                "-video_size",
                f"{width}x{height}",
                "-i",
                "desktop",
            ]
        )
        screen_filter = f"[{screen_index}:v]setpts=PTS-STARTPTS,format=yuv420p[screen]"
    else:
        if not options.window_handle:
            raise ValueError("Choose a window to record.")
        command.extend(
            [
                "-thread_queue_size",
                "1024",
                "-f",
                "gdigrab",
                "-framerate",
                str(options.fps),
                "-draw_mouse",
                draw_mouse,
                "-i",
                f"hwnd={options.window_handle}",
            ]
        )
        screen_filter = f"[{screen_index}:v]setpts=PTS-STARTPTS,format=yuv420p[screen]"

    filters = [screen_filter]
    if camera_index is not None:
        camera_width = max(160, min(640, options.camera_width))
        camera_height = max(90, round(camera_width * 9 / 16))
        camera_height -= camera_height % 2
        filters.append(
            f"[{camera_index}:v]setpts=PTS-STARTPTS,scale={camera_width}:{camera_height},"
            "format=yuv420p[cam]"
        )
        filters.append(
            f"[screen][cam]overlay={_overlay_position(options.camera_position)}:"
            "shortest=0,format=nv12[video]"
        )
    else:
        filters.append("[screen]format=nv12[video]")
    if microphone_index is not None:
        filters.append(
            f"[{microphone_index}:a]asetpts=PTS-STARTPTS,"
            "aresample=48000:async=1:first_pts=0[audio]"
        )

    command.extend(["-filter_complex", ";".join(filters), "-map", "[video]"])
    if microphone_index is not None:
        command.extend(["-map", "[audio]"])
    command.extend(
        [
            "-c:v",
            "h264_mf",
            "-rate_control",
            "quality",
            "-quality",
            str(options.quality),
            "-g",
            str(options.fps * 2),
            "-fps_mode",
            "cfr",
        ]
    )
    if microphone_index is not None:
        command.extend(["-c:a", "aac", "-b:a", "128k"])
    else:
        command.append("-an")
    command.extend(
        [
            "-movflags",
            "+frag_keyframe+empty_moov+default_base_moof",
            "-metadata",
            "title=CursorPocket walkthrough",
            "-progress",
            "pipe:2",
            "-nostats",
            str(output_path),
        ]
    )
    return command


_DURATION_RE = re.compile(r"Duration:\s*(\d+):(\d+):(\d+(?:\.\d+)?)")
_VIDEO_RE = re.compile(r"Video:.*?,\s*(\d{2,5})x(\d{2,5})(?:\s|,)")
_FPS_RE = re.compile(r"(?:,|\s)(\d+(?:\.\d+)?)\s+fps(?:,|\s)")


def inspect_video_file(ffmpeg_path: Path | str, path: Path | str) -> VideoInspection:
    media_path = Path(path)
    if not media_path.exists() or media_path.stat().st_size < 1024:
        raise ValueError("Video capture is empty.")
    output = _run_text(ffmpeg_path, "-hide_banner", "-i", str(media_path), timeout=15.0)
    duration_match = _DURATION_RE.search(output)
    video_match = _VIDEO_RE.search(output)
    if not duration_match or not video_match or "Video:" not in output:
        raise ValueError("Video capture could not be validated.")
    hours, minutes, seconds = duration_match.groups()
    duration = int(hours) * 3600 + int(minutes) * 60 + float(seconds)
    if duration <= 0:
        raise ValueError("Video capture is empty.")
    fps_match = _FPS_RE.search(output)
    return VideoInspection(
        duration_seconds=duration,
        width=int(video_match.group(1)),
        height=int(video_match.group(2)),
        fps=float(fps_match.group(1)) if fps_match else 0.0,
        has_audio="Audio:" in output,
    )


class FFmpegVideoRecorder:
    """Own one FFmpeg recording process and publish lifecycle events to the app queue."""

    def __init__(
        self,
        ffmpeg_path: Path | str,
        emit: Callable[[object], None],
        process_factory: Callable[..., subprocess.Popen[str]] = subprocess.Popen,
    ) -> None:
        self.ffmpeg_path = Path(ffmpeg_path)
        self.emit = emit
        self.process_factory = process_factory
        self.state = RecordingState.IDLE
        self.process: subprocess.Popen[str] | None = None
        self.output_path: Path | None = None
        self._stop_requested = False
        self._discard_requested = False
        self._forced = False
        self._stop_thread: threading.Thread | None = None
        self._lock = threading.Lock()

    @property
    def is_recording(self) -> bool:
        return self.state in {RecordingState.STARTING, RecordingState.RECORDING, RecordingState.STOPPING}

    def start(self, options: VideoOptions, output_path: Path | str) -> None:
        with self._lock:
            if self.is_recording:
                raise RuntimeError("A video recording is already in progress.")
            destination = Path(output_path)
            if destination.exists():
                raise FileExistsError(f"Recording destination already exists: {destination}")
            destination.parent.mkdir(parents=True, exist_ok=True)
            command = build_ffmpeg_command(self.ffmpeg_path, destination, options)
            creation_flags = subprocess.CREATE_NO_WINDOW if sys.platform == "win32" else 0
            self.state = RecordingState.STARTING
            self.output_path = destination
            self._stop_requested = False
            self._discard_requested = False
            self._forced = False
            self.process = self.process_factory(
                command,
                stdin=subprocess.PIPE,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.PIPE,
                text=True,
                encoding="utf-8",
                errors="replace",
                bufsize=1,
                creationflags=creation_flags,
            )
        threading.Thread(target=self._monitor, name="CursorPocketVideo", daemon=True).start()

    def stop(self, *, discard: bool = False) -> None:
        with self._lock:
            if not self.is_recording or not self.process:
                return
            self._stop_requested = True
            self._discard_requested = discard
            self.state = RecordingState.STOPPING
            if self._stop_thread and self._stop_thread.is_alive():
                return
            self._stop_thread = threading.Thread(
                target=self._graceful_stop,
                name="CursorPocketVideoStop",
                daemon=True,
            )
            self._stop_thread.start()

    def _graceful_stop(self) -> None:
        process = self.process
        if process is None:
            return
        try:
            if process.stdin:
                process.stdin.write("q\n")
                process.stdin.flush()
            process.wait(timeout=8.0)
            return
        except (BrokenPipeError, OSError, subprocess.TimeoutExpired):
            pass
        self._forced = True
        try:
            process.terminate()
            process.wait(timeout=2.0)
        except (OSError, subprocess.TimeoutExpired):
            try:
                process.kill()
            except OSError:
                pass

    def _monitor(self) -> None:
        process = self.process
        output_path = self.output_path
        if process is None or output_path is None:
            return
        tail: deque[str] = deque(maxlen=12)
        frame = 0
        out_time_ms = 0
        if process.stderr:
            for raw_line in process.stderr:
                line = raw_line.strip()
                if not line:
                    continue
                tail.append(line)
                if line.startswith("frame="):
                    try:
                        frame = int(line.partition("=")[2])
                    except ValueError:
                        pass
                elif line.startswith("out_time_ms="):
                    try:
                        out_time_ms = int(line.partition("=")[2])
                    except ValueError:
                        pass
                elif line == "progress=continue":
                    if self.state == RecordingState.STARTING and frame > 0:
                        self.state = RecordingState.RECORDING
                        self.emit(("video_started",))
                    self.emit(("video_progress", out_time_ms / 1_000_000.0, frame))
        return_code = process.wait()
        result = VideoProcessResult(
            output_path=output_path,
            return_code=return_code,
            stop_requested=self._stop_requested,
            discard_requested=self._discard_requested,
            forced=self._forced,
            error_detail="\n".join(tail),
        )
        self.state = RecordingState.IDLE if return_code == 0 or self._stop_requested else RecordingState.FAILED
        self.process = None
        self.emit(("video_finished", result))

