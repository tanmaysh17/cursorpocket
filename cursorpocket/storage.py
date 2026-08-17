from __future__ import annotations

import json
import re
import shutil
import threading
import uuid
from dataclasses import asdict, dataclass
from datetime import datetime
from pathlib import Path
from typing import Any
from urllib.parse import urlparse

from PIL import Image


@dataclass(frozen=True)
class CaptureRecord:
    id: str
    kind: str
    created_at: str
    path: str
    preview: str
    metadata: dict[str, Any]


def is_web_url(value: str) -> bool:
    """Return True only for ordinary HTTP(S) links with a host."""
    try:
        parsed = urlparse(value.strip())
    except ValueError:
        return False
    return parsed.scheme.lower() in {"http", "https"} and bool(parsed.netloc)


def compact_preview(value: str, limit: int = 78) -> str:
    clean = re.sub(r"\s+", " ", value).strip()
    if len(clean) <= limit:
        return clean
    return clean[: limit - 1].rstrip() + "…"


class CaptureStore:
    """Persists captures in dated folders plus a resilient JSONL index."""

    def __init__(self, base_dir: Path | str) -> None:
        self.base_dir = Path(base_dir).expanduser()
        self._lock = threading.Lock()
        self._ensure_base()

    @property
    def manifest_path(self) -> Path:
        return self.base_dir / "captures.jsonl"

    def set_base_dir(self, base_dir: Path | str) -> None:
        with self._lock:
            self.base_dir = Path(base_dir).expanduser()
            self._ensure_base()

    def _ensure_base(self) -> None:
        self.base_dir.mkdir(parents=True, exist_ok=True)

    def _destination(self, kind: str, suffix: str, now: datetime) -> tuple[str, Path]:
        capture_id = f"{now.strftime('%Y%m%dT%H%M%S')}-{uuid.uuid4().hex[:6]}"
        category = {
            "screenshot": "screenshots",
            "text": "text",
            "link": "links",
            "audio": "audio",
        }.get(kind, kind)
        day_dir = self.base_dir / now.strftime("%Y-%m-%d") / category
        day_dir.mkdir(parents=True, exist_ok=True)
        filename = f"{now.strftime('%H-%M-%S')}_{kind}_{capture_id[-6:]}{suffix}"
        return capture_id, day_dir / filename

    def save_text(self, text: str, now: datetime | None = None) -> CaptureRecord:
        value = text.strip()
        if not value:
            raise ValueError("Text capture is empty.")
        now = now or datetime.now().astimezone()
        with self._lock:
            capture_id, path = self._destination("text", ".txt", now)
            path.write_text(value + "\n", encoding="utf-8")
            record = self._record(capture_id, "text", path, compact_preview(value), now, {})
            self._append(record)
        return record

    def save_link(self, url: str, now: datetime | None = None) -> CaptureRecord:
        value = url.strip()
        if not is_web_url(value):
            raise ValueError("Enter a complete http:// or https:// link.")
        now = now or datetime.now().astimezone()
        with self._lock:
            capture_id, path = self._destination("link", ".url", now)
            path.write_text(f"[InternetShortcut]\nURL={value}\n", encoding="utf-8")
            host = urlparse(value).netloc.removeprefix("www.")
            record = self._record(
                capture_id,
                "link",
                path,
                compact_preview(value),
                now,
                {"url": value, "host": host},
            )
            self._append(record)
        return record

    def save_image(
        self,
        image: Image.Image,
        bounds: tuple[int, int, int, int],
        now: datetime | None = None,
    ) -> CaptureRecord:
        if image.width < 1 or image.height < 1:
            raise ValueError("Screenshot selection is empty.")
        now = now or datetime.now().astimezone()
        with self._lock:
            capture_id, path = self._destination("screenshot", ".png", now)
            image.save(path, format="PNG", optimize=True)
            record = self._record(
                capture_id,
                "screenshot",
                path,
                f"Screenshot · {image.width} × {image.height}",
                now,
                {"bounds": list(bounds), "width": image.width, "height": image.height},
            )
            self._append(record)
        return record

    def save_audio_file(
        self,
        source_path: Path | str,
        duration_seconds: float,
        sample_rate: int = 16000,
        channels: int = 1,
        now: datetime | None = None,
    ) -> CaptureRecord:
        source = Path(source_path)
        if not source.exists() or source.stat().st_size <= 44:
            raise ValueError("Audio capture is empty.")
        if duration_seconds <= 0:
            raise ValueError("Audio capture is empty.")
        now = now or datetime.now().astimezone()
        with self._lock:
            capture_id, path = self._destination("audio", ".wav", now)
            shutil.copyfile(source, path)
            whole_seconds = max(1, round(duration_seconds))
            minutes, seconds = divmod(whole_seconds, 60)
            record = self._record(
                capture_id,
                "audio",
                path,
                f"Audio · {minutes}:{seconds:02d}",
                now,
                {
                    "duration_seconds": round(duration_seconds, 3),
                    "sample_rate": sample_rate,
                    "channels": channels,
                },
            )
            self._append(record)
        return record

    def recent(self, limit: int = 6) -> list[CaptureRecord]:
        if limit <= 0 or not self.manifest_path.exists():
            return []
        records: list[CaptureRecord] = []
        try:
            lines = self.manifest_path.read_text(encoding="utf-8").splitlines()
        except OSError:
            return []
        for line in reversed(lines):
            if not line.strip():
                continue
            try:
                data = json.loads(line)
                record = CaptureRecord(**data)
            except (json.JSONDecodeError, TypeError):
                continue
            records.append(record)
            if len(records) >= limit:
                break
        return records

    def absolute_path(self, record: CaptureRecord) -> Path:
        return self.base_dir / Path(record.path)

    def _record(
        self,
        capture_id: str,
        kind: str,
        path: Path,
        preview: str,
        now: datetime,
        metadata: dict[str, Any],
    ) -> CaptureRecord:
        return CaptureRecord(
            id=capture_id,
            kind=kind,
            created_at=now.isoformat(timespec="seconds"),
            path=path.relative_to(self.base_dir).as_posix(),
            preview=preview,
            metadata=metadata,
        )

    def _append(self, record: CaptureRecord) -> None:
        self._ensure_base()
        with self.manifest_path.open("a", encoding="utf-8", newline="\n") as manifest:
            manifest.write(json.dumps(asdict(record), ensure_ascii=False) + "\n")
