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
    onboarding_seen: bool = False
    panel_geometry: str = ""

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
            return AppSettings(
                capture_dir=capture_dir,
                follow_cursor=bool(raw.get("follow_cursor", True)),
                onboarding_seen=bool(raw.get("onboarding_seen", False)),
                panel_geometry=str(raw.get("panel_geometry", "")),
            )
        except (OSError, json.JSONDecodeError, TypeError):
            return AppSettings.defaults()

    def save(self, settings: AppSettings) -> None:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        temp_path = self.path.with_suffix(".tmp")
        temp_path.write_text(
            json.dumps(asdict(settings), indent=2, ensure_ascii=False),
            encoding="utf-8",
        )
        temp_path.replace(self.path)
