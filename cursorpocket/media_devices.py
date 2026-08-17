from __future__ import annotations

import re
import subprocess
from dataclasses import dataclass
from pathlib import Path


_DEVICE_RE = re.compile(r'^.*?"(?P<name>.+)" \((?P<kind>video|audio)\)\s*$')
_ALTERNATIVE_RE = re.compile(r'^.*?Alternative name "(?P<identifier>.+)"\s*$')


@dataclass(frozen=True)
class MediaDevice:
    name: str
    kind: str
    identifier: str = ""


def parse_directshow_devices(output: str) -> list[MediaDevice]:
    """Parse FFmpeg's DirectShow device listing without depending on log prefixes."""
    devices: list[MediaDevice] = []
    pending: tuple[str, str] | None = None
    for raw_line in output.splitlines():
        line = raw_line.strip()
        device_match = _DEVICE_RE.match(line)
        if device_match:
            if pending:
                devices.append(MediaDevice(pending[0], pending[1]))
            pending = (device_match.group("name"), device_match.group("kind"))
            continue
        alternative_match = _ALTERNATIVE_RE.match(line)
        if alternative_match and pending:
            devices.append(
                MediaDevice(
                    pending[0],
                    pending[1],
                    alternative_match.group("identifier"),
                )
            )
            pending = None
    if pending:
        devices.append(MediaDevice(pending[0], pending[1]))
    return devices


def list_directshow_devices(ffmpeg_path: Path | str, timeout: float = 8.0) -> list[MediaDevice]:
    completed = subprocess.run(
        [
            str(ffmpeg_path),
            "-hide_banner",
            "-list_devices",
            "true",
            "-f",
            "dshow",
            "-i",
            "dummy",
        ],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=timeout,
        check=False,
    )
    return parse_directshow_devices(completed.stdout + "\n" + completed.stderr)
