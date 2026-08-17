from __future__ import annotations

import tempfile
import threading
import unittest
from pathlib import Path

from cursorpocket.media_devices import parse_directshow_devices
from cursorpocket.video import (
    FFmpegVideoRecorder,
    RecordingState,
    VideoOptions,
    VideoSourceKind,
    build_ffmpeg_command,
)


class _FakeProcess:
    def __init__(self) -> None:
        self.stderr = iter(
            [
                "frame=1\n",
                "out_time_ms=33333\n",
                "progress=continue\n",
                "frame=30\n",
                "out_time_ms=1000000\n",
                "progress=continue\n",
            ]
        )
        self.stdin = None

    def wait(self, timeout: float | None = None) -> int:
        del timeout
        return 0

    def terminate(self) -> None:
        pass

    def kill(self) -> None:
        pass


class VideoCommandTests(unittest.TestCase):
    def setUp(self) -> None:
        self.output = Path("C:/Captures/walkthrough.partial.mp4")

    def test_screen_only_uses_desktop_duplication_and_media_foundation(self) -> None:
        command = build_ffmpeg_command(
            "ffmpeg.exe",
            self.output,
            VideoOptions(include_microphone=False),
        )

        rendered = " ".join(command)
        self.assertIn("ddagrab=output_idx=0:framerate=30:draw_mouse=1", command)
        self.assertIn("h264_mf", command)
        self.assertIn("+frag_keyframe+empty_moov+default_base_moof", command)
        self.assertIn("-an", command)
        self.assertNotIn("-f dshow", rendered)

    def test_microphone_and_camera_are_opened_before_the_screen(self) -> None:
        command = build_ffmpeg_command(
            "ffmpeg.exe",
            self.output,
            VideoOptions(
                include_microphone=True,
                microphone_name="Microphone Array (Realtek Audio)",
                include_camera=True,
                camera_name="Integrated Webcam",
            ),
        )

        microphone = command.index("audio=Microphone Array (Realtek Audio)")
        camera = command.index("video=Integrated Webcam")
        screen = command.index("ddagrab=output_idx=0:framerate=30:draw_mouse=1")
        self.assertLess(microphone, camera)
        self.assertLess(camera, screen)
        filters = command[command.index("-filter_complex") + 1]
        self.assertIn("[0:a]asetpts=PTS-STARTPTS", filters)
        self.assertIn("[1:v]setpts=PTS-STARTPTS", filters)
        self.assertIn("[2:v]hwdownload", filters)
        self.assertIn("overlay=W-w-32:H-h-32", filters)

    def test_region_keeps_negative_coordinates_and_even_dimensions(self) -> None:
        command = build_ffmpeg_command(
            "ffmpeg.exe",
            self.output,
            VideoOptions(
                source_kind=VideoSourceKind.REGION,
                bounds=(-1919, 17, -110, 918),
                include_microphone=False,
            ),
        )

        self.assertIn("-1919", command)
        self.assertIn("17", command)
        self.assertIn("1808x900", command)
        self.assertIn("desktop", command)

    def test_window_source_requires_a_handle(self) -> None:
        with self.assertRaisesRegex(ValueError, "Choose a window"):
            build_ffmpeg_command(
                "ffmpeg.exe",
                self.output,
                VideoOptions(
                    source_kind=VideoSourceKind.WINDOW,
                    include_microphone=False,
                ),
            )

    def test_required_devices_are_explicit(self) -> None:
        with self.assertRaisesRegex(ValueError, "microphone"):
            build_ffmpeg_command("ffmpeg.exe", self.output, VideoOptions())
        with self.assertRaisesRegex(ValueError, "camera"):
            build_ffmpeg_command(
                "ffmpeg.exe",
                self.output,
                VideoOptions(
                    include_microphone=False,
                    include_camera=True,
                ),
            )


class DeviceParsingTests(unittest.TestCase):
    def test_parses_names_and_alternative_identifiers(self) -> None:
        output = r'''
[in#0 @ 0001] "Integrated Webcam" (video)
[in#0 @ 0001]   Alternative name "@device_pnp_\\?\\usb#camera"
[in#0 @ 0001] "Microphone Array (Realtek(R) Audio)" (audio)
[in#0 @ 0001]   Alternative name "@device_cm_{audio}"
'''

        devices = parse_directshow_devices(output)

        self.assertEqual([device.name for device in devices], [
            "Integrated Webcam",
            "Microphone Array (Realtek(R) Audio)",
        ])
        self.assertEqual(devices[0].kind, "video")
        self.assertEqual(devices[1].kind, "audio")
        self.assertTrue(devices[0].identifier.startswith("@device_pnp_"))


class RecorderLifecycleTests(unittest.TestCase):
    def test_process_progress_and_completion_are_emitted(self) -> None:
        events: list[object] = []
        finished = threading.Event()

        def emit(event: object) -> None:
            events.append(event)
            if isinstance(event, tuple) and event[0] == "video_finished":
                finished.set()

        with tempfile.TemporaryDirectory() as temp_dir:
            recorder = FFmpegVideoRecorder(
                "ffmpeg.exe",
                emit,
                process_factory=lambda *_args, **_kwargs: _FakeProcess(),
            )
            recorder.start(
                VideoOptions(include_microphone=False),
                Path(temp_dir) / "fixture.partial.mp4",
            )
            self.assertTrue(finished.wait(2.0))

        kinds = [event[0] for event in events if isinstance(event, tuple)]
        self.assertIn("video_started", kinds)
        self.assertIn("video_progress", kinds)
        self.assertEqual(kinds[-1], "video_finished")
        self.assertEqual(recorder.state, RecordingState.IDLE)


if __name__ == "__main__":
    unittest.main()
