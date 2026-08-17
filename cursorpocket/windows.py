from __future__ import annotations

import ctypes
import os
import sys
from ctypes import wintypes


SM_XVIRTUALSCREEN = 76
SM_YVIRTUALSCREEN = 77
SM_CXVIRTUALSCREEN = 78
SM_CYVIRTUALSCREEN = 79
HWND_TOPMOST = -1
SWP_NOACTIVATE = 0x0010
SWP_SHOWWINDOW = 0x0040
ERROR_ALREADY_EXISTS = 183


class POINT(ctypes.Structure):
    _fields_ = [("x", wintypes.LONG), ("y", wintypes.LONG)]


class RECT(ctypes.Structure):
    _fields_ = [
        ("left", wintypes.LONG),
        ("top", wintypes.LONG),
        ("right", wintypes.LONG),
        ("bottom", wintypes.LONG),
    ]


def enable_dpi_awareness() -> None:
    if sys.platform != "win32":
        return
    try:
        ctypes.windll.user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
    except (AttributeError, OSError):
        try:
            ctypes.windll.shcore.SetProcessDpiAwareness(2)
        except (AttributeError, OSError):
            ctypes.windll.user32.SetProcessDPIAware()


def cursor_position() -> tuple[int, int]:
    if sys.platform != "win32":
        return (120, 120)
    point = POINT()
    ctypes.windll.user32.GetCursorPos(ctypes.byref(point))
    return int(point.x), int(point.y)


def virtual_screen_bounds() -> tuple[int, int, int, int]:
    if sys.platform != "win32":
        return (0, 0, 1920, 1080)
    user32 = ctypes.windll.user32
    return (
        int(user32.GetSystemMetrics(SM_XVIRTUALSCREEN)),
        int(user32.GetSystemMetrics(SM_YVIRTUALSCREEN)),
        int(user32.GetSystemMetrics(SM_CXVIRTUALSCREEN)),
        int(user32.GetSystemMetrics(SM_CYVIRTUALSCREEN)),
    )


def foreground_window_bounds() -> tuple[int, int, int, int] | None:
    if sys.platform != "win32":
        return None
    user32 = ctypes.windll.user32
    hwnd = user32.GetForegroundWindow()
    if not hwnd or user32.IsIconic(hwnd):
        return None
    pid = wintypes.DWORD()
    user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    if int(pid.value) == os.getpid():
        return None
    rect = RECT()
    if not user32.GetWindowRect(hwnd, ctypes.byref(rect)):
        return None
    if rect.right <= rect.left or rect.bottom <= rect.top:
        return None
    return int(rect.left), int(rect.top), int(rect.right), int(rect.bottom)


def monitor_bounds() -> list[tuple[int, int, int, int]]:
    if sys.platform != "win32":
        return [(0, 0, 1920, 1080)]
    monitors: list[tuple[int, int, int, int]] = []
    callback_type = ctypes.WINFUNCTYPE(
        wintypes.BOOL,
        wintypes.HMONITOR,
        wintypes.HDC,
        ctypes.POINTER(RECT),
        wintypes.LPARAM,
    )

    @callback_type
    def collect(
        _monitor: int,
        _dc: int,
        rect: ctypes.POINTER(RECT),
        _data: int,
    ) -> bool:
        value = rect.contents
        monitors.append(
            (int(value.left), int(value.top), int(value.right), int(value.bottom))
        )
        return True

    ctypes.windll.user32.EnumDisplayMonitors(None, None, collect, 0)
    monitors.sort(
        key=lambda bounds: (
            0
            if bounds[0] <= 0 < bounds[2] and bounds[1] <= 0 < bounds[3]
            else 1,
            bounds[0],
            bounds[1],
        )
    )
    return monitors


def position_window(window: object, x: int, y: int, width: int, height: int, activate: bool = False) -> None:
    """Position a Tk window correctly even on negative-coordinate monitors."""
    if sys.platform == "win32":
        try:
            window.update_idletasks()
            user32 = ctypes.windll.user32
            user32.GetParent.argtypes = [wintypes.HWND]
            user32.GetParent.restype = wintypes.HWND
            user32.SetWindowPos.argtypes = [
                wintypes.HWND,
                wintypes.HWND,
                ctypes.c_int,
                ctypes.c_int,
                ctypes.c_int,
                ctypes.c_int,
                wintypes.UINT,
            ]
            user32.SetWindowPos.restype = wintypes.BOOL
            client_hwnd = wintypes.HWND(int(window.winfo_id()))
            wrapper_hwnd = user32.GetParent(client_hwnd)
            hwnd = wrapper_hwnd or client_hwnd
            flags = SWP_SHOWWINDOW | (0 if activate else SWP_NOACTIVATE)
            if user32.SetWindowPos(
                hwnd,
                wintypes.HWND(HWND_TOPMOST),
                x,
                y,
                width,
                height,
                flags,
            ):
                return
        except (AttributeError, OSError, TypeError):
            pass
    sign_x = "+" if x >= 0 else ""
    sign_y = "+" if y >= 0 else ""
    window.geometry(f"{width}x{height}{sign_x}{x}{sign_y}{y}")


class SingleInstance:
    """Owns a per-user Win32 mutex for the lifetime of the process."""

    def __init__(self, name: str = "Local\\CursorPocket.SingleInstance") -> None:
        self.handle: int | None = None
        self.acquired = True
        if sys.platform != "win32":
            return
        kernel32 = ctypes.windll.kernel32
        kernel32.CreateMutexW.argtypes = [ctypes.c_void_p, wintypes.BOOL, wintypes.LPCWSTR]
        kernel32.CreateMutexW.restype = wintypes.HANDLE
        kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
        kernel32.CloseHandle.restype = wintypes.BOOL
        kernel32.SetLastError(0)
        handle = kernel32.CreateMutexW(None, False, name)
        if not handle:
            self.acquired = False
            return
        self.handle = int(handle)
        if kernel32.GetLastError() == ERROR_ALREADY_EXISTS:
            self.acquired = False
            kernel32.CloseHandle(handle)
            self.handle = None

    def close(self) -> None:
        if self.handle and sys.platform == "win32":
            ctypes.windll.kernel32.CloseHandle(wintypes.HANDLE(self.handle))
            self.handle = None

    def __del__(self) -> None:
        self.close()
