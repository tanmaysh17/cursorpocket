from __future__ import annotations

import ctypes
import sys
import threading
from ctypes import wintypes
from dataclasses import dataclass
from typing import Callable


MOD_ALT = 0x0001
MOD_CONTROL = 0x0002
MOD_SHIFT = 0x0004
MOD_WIN = 0x0008
MOD_NOREPEAT = 0x4000
WM_HOTKEY = 0x0312
WM_QUIT = 0x0012


@dataclass(frozen=True)
class HotkeySpec:
    identifier: int
    modifiers: int
    virtual_key: int
    action: str
    label: str
    fallback_modifiers: int = 0
    fallback_label: str = ""


DEFAULT_HOTKEYS = (
    HotkeySpec(
        0xC001,
        MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT,
        ord("Q"),
        "screenshot",
        "Ctrl + Shift + Q",
        MOD_CONTROL | MOD_ALT | MOD_NOREPEAT,
        "Ctrl + Alt + S",
    ),
    HotkeySpec(
        0xC002,
        MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT,
        ord("T"),
        "text",
        "Ctrl + Shift + T",
        MOD_CONTROL | MOD_ALT | MOD_NOREPEAT,
        "Ctrl + Alt + T",
    ),
    HotkeySpec(
        0xC003,
        MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT,
        ord("L"),
        "link",
        "Ctrl + Shift + L",
        MOD_CONTROL | MOD_ALT | MOD_NOREPEAT,
        "Ctrl + Alt + L",
    ),
    HotkeySpec(
        0xC004,
        MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT,
        0x20,
        "panel",
        "Ctrl + Shift + Space",
        MOD_CONTROL | MOD_ALT | MOD_NOREPEAT,
        "Ctrl + Alt + Space",
    ),
    HotkeySpec(
        0xC005,
        MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT,
        ord("A"),
        "audio",
        "Ctrl + Shift + A",
        MOD_CONTROL | MOD_ALT | MOD_NOREPEAT,
        "Ctrl + Alt + R",
    ),
    HotkeySpec(
        0xC006,
        MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT,
        ord("H"),
        "hidden",
        "Ctrl + Shift + H",
        MOD_CONTROL | MOD_ALT | MOD_NOREPEAT,
        "Ctrl + Alt + H",
    ),
    HotkeySpec(
        0xC007,
        MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT,
        ord("1"),
        "full_screenshot",
        "Ctrl + Shift + 1",
    ),
)


class POINT(ctypes.Structure):
    _fields_ = [("x", wintypes.LONG), ("y", wintypes.LONG)]


class MSG(ctypes.Structure):
    _fields_ = [
        ("hwnd", wintypes.HWND),
        ("message", wintypes.UINT),
        ("wParam", wintypes.WPARAM),
        ("lParam", wintypes.LPARAM),
        ("time", wintypes.DWORD),
        ("pt", POINT),
        ("lPrivate", wintypes.DWORD),
    ]


class GlobalHotkeyManager:
    """Runs the Win32 hotkey message pump without blocking Tk."""

    def __init__(
        self,
        callback: Callable[[str], None],
        error_callback: Callable[[str], None] | None = None,
        status_callback: Callable[[str, str, bool], None] | None = None,
        hotkeys: tuple[HotkeySpec, ...] = DEFAULT_HOTKEYS,
    ) -> None:
        self.callback = callback
        self.error_callback = error_callback
        self.status_callback = status_callback
        self.hotkeys = hotkeys
        self._thread: threading.Thread | None = None
        self._thread_id = 0
        self._ready = threading.Event()

    def start(self) -> None:
        if sys.platform != "win32" or self._thread:
            return
        self._thread = threading.Thread(target=self._run, name="CursorPocketHotkeys", daemon=True)
        self._thread.start()
        self._ready.wait(timeout=1.5)

    def stop(self) -> None:
        if sys.platform != "win32" or not self._thread_id:
            return
        ctypes.windll.user32.PostThreadMessageW(self._thread_id, WM_QUIT, 0, 0)
        if self._thread:
            self._thread.join(timeout=1.0)
        self._thread = None
        self._thread_id = 0

    def _run(self) -> None:
        user32 = ctypes.windll.user32
        kernel32 = ctypes.windll.kernel32
        self._thread_id = kernel32.GetCurrentThreadId()
        registered: list[HotkeySpec] = []
        by_id = {hotkey.identifier: hotkey for hotkey in self.hotkeys}
        for hotkey in self.hotkeys:
            used_fallback = False
            registered_label = hotkey.label
            success = user32.RegisterHotKey(
                None, hotkey.identifier, hotkey.modifiers, hotkey.virtual_key
            )
            if not success and hotkey.fallback_modifiers:
                success = user32.RegisterHotKey(
                    None,
                    hotkey.identifier,
                    hotkey.fallback_modifiers,
                    hotkey.virtual_key,
                )
                if success:
                    used_fallback = True
                    registered_label = hotkey.fallback_label
            if success:
                registered.append(hotkey)
                if self.status_callback:
                    self.status_callback(hotkey.action, registered_label, used_fallback)
            elif self.error_callback:
                alternatives = (
                    f" and {hotkey.fallback_label}" if hotkey.fallback_label else ""
                )
                self.error_callback(
                    f"{hotkey.label}{alternatives} are already used by another app."
                )
        self._ready.set()
        message = MSG()
        while user32.GetMessageW(ctypes.byref(message), None, 0, 0) > 0:
            if message.message == WM_HOTKEY:
                hotkey = by_id.get(int(message.wParam))
                if hotkey:
                    self.callback(hotkey.action)
        for hotkey in registered:
            user32.UnregisterHotKey(None, hotkey.identifier)
