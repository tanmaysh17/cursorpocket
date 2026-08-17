from __future__ import annotations

import json
import os
from dataclasses import asdict, dataclass
from pathlib import Path


def default_capture_dir() -> Path:
    documents = Path.home() / "Documents"
    return documents / "CursorPocket Captures"


def default_settings_path() -> Path:
    local_app_data = os.environ.get("LOCALAPPDATA")
    root = Path(local_app_data) if local_app_data else Path.home() / ".cursorpocket"
    return root / "CursorPocket" / "settings.json"


@dataclass
class AppSettings:
    capture_dir: str
    follow_cursor: bool = True
    mouse_gesture_enabled: bool = True
    onboarding_seen: bool = False
    panel_geometry: str = ""
    video_microphone_enabled: bool = True
    video_camera_enabled: bool = False
    video_microphone_name: str = ""
    video_camera_name: str = ""
    video_camera_position: str = "bottom-right"
    video_fps: int = 30
    video_countdown_seconds: int = 3

    @classmethod
    def defaults(cls) -> "AppSettings":
        return cls(capture_dir=str(default_capture_dir()))


class SettingsStore:
    def __init__(self, path: Path | str | None = None) -> None:
        self.path = Path(path) if path else default_settings_path()

    def load(self) -> AppSettings:
        if not self.path.exists():
            return AppSettings.defaults()
        try:
            raw = json.loads(self.path.read_text(encoding="utf-8"))
            capture_dir = str(raw.get("capture_dir") or default_capture_dir())
            video_fps = int(raw.get("video_fps", 30))
            if video_fps not in {15, 24, 30, 60}:
                video_fps = 30
            video_countdown = int(raw.get("video_countdown_seconds", 3))
            if video_countdown not in {0, 3, 5}:
                video_countdown = 3
            video_position = str(raw.get("video_camera_position", "bottom-right"))
            if video_position not in {"top-left", "top-right", "bottom-left", "bottom-right"}:
                video_position = "bottom-right"
            return AppSettings(
                capture_dir=capture_dir,
                follow_cursor=bool(raw.get("follow_cursor", True)),
                mouse_gesture_enabled=bool(raw.get("mouse_gesture_enabled", True)),
                onboarding_seen=bool(raw.get("onboarding_seen", False)),
                panel_geometry=str(raw.get("panel_geometry", "")),
                video_microphone_enabled=bool(raw.get("video_microphone_enabled", True)),
                video_camera_enabled=bool(raw.get("video_camera_enabled", False)),
                video_microphone_name=str(raw.get("video_microphone_name", "")),
                video_camera_name=str(raw.get("video_camera_name", "")),
                video_camera_position=video_position,
                video_fps=video_fps,
                video_countdown_seconds=video_countdown,
            )
        except (OSError, json.JSONDecodeError, TypeError, ValueError):
            return AppSettings.defaults()

    def save(self, settings: AppSettings) -> None:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        temp_path = self.path.with_suffix(".tmp")
        temp_path.write_text(
            json.dumps(asdict(settings), indent=2, ensure_ascii=False),
            encoding="utf-8",
        )
        temp_path.replace(self.path)
