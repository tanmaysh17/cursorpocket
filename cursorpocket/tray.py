from __future__ import annotations

from typing import Callable

from PIL import Image

from .branding import tray_icon

try:
    import pystray
except ImportError:  # pragma: no cover - development fallback
    pystray = None  # type: ignore[assignment]


class TrayManager:
    def __init__(self, emit: Callable[[str], None]) -> None:
        self.emit = emit
        self.recording = False
        self.hidden = False
        self.icon: object | None = None

    def start(self) -> bool:
        if pystray is None:
            return False
        self.icon = pystray.Icon(
            "CursorPocket",
            self._image(False),
            "CursorPocket — click to capture",
            pystray.Menu(
                pystray.MenuItem("Open command mode", self._action("panel"), default=True),
                pystray.Menu.SEPARATOR,
                pystray.MenuItem("Screenshot", self._action("screenshot")),
                pystray.MenuItem(
                    lambda _item: "Stop recording" if self.recording else "Record audio",
                    self._action("audio"),
                ),
                pystray.MenuItem("Text snippet", self._action("text")),
                pystray.MenuItem("Web link", self._action("link")),
                pystray.Menu.SEPARATOR,
                pystray.MenuItem("Open captures folder", self._action("folder")),
                pystray.MenuItem("Settings", self._action("settings")),
                pystray.MenuItem(
                    lambda _item: "Show cursor dot" if self.hidden else "Hide cursor dot",
                    self._action("hidden"),
                ),
                pystray.Menu.SEPARATOR,
                pystray.MenuItem("Quit CursorPocket", self._action("quit")),
            ),
        )
        self.icon.run_detached()  # type: ignore[union-attr]
        return True

    def stop(self) -> None:
        if self.icon is not None:
            self.icon.stop()  # type: ignore[union-attr]
            self.icon = None

    def set_state(self, *, recording: bool, hidden: bool) -> None:
        self.recording = recording
        self.hidden = hidden
        if self.icon is None:
            return
        self.icon.icon = self._image(recording)  # type: ignore[union-attr]
        self.icon.title = (  # type: ignore[union-attr]
            "CursorPocket — recording audio" if recording else "CursorPocket — click to capture"
        )
        self.icon.update_menu()  # type: ignore[union-attr]

    def _action(self, action: str) -> Callable:
        def callback(_icon: object, _item: object) -> None:
            self.emit(action)

        return callback

    @staticmethod
    def _image(recording: bool) -> Image.Image:
        return tray_icon(recording)
