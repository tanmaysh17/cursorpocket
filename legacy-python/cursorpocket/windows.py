from __future__ import annotations

import ctypes
import os
import sys
import time
from ctypes import wintypes
from pathlib import Path


SM_XVIRTUALSCREEN = 76
SM_YVIRTUALSCREEN = 77
SM_CXVIRTUALSCREEN = 78
SM_CYVIRTUALSCREEN = 79
HWND_TOPMOST = -1
SWP_NOACTIVATE = 0x0010
SWP_SHOWWINDOW = 0x0040
ERROR_ALREADY_EXISTS = 183
WAIT_OBJECT_0 = 0
PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
SW_RESTORE = 9
VK_CONTROL = 0x11
VK_ESCAPE = 0x1B
KEYEVENTF_KEYUP = 0x0002
GWL_EXSTYLE = -20
WS_EX_NOACTIVATE = 0x08000000
WS_EX_TOOLWINDOW = 0x00000080
WDA_EXCLUDEFROMCAPTURE = 0x00000011

BROWSER_EXECUTABLES = frozenset(
    {
        "brave.exe",
        "chrome.exe",
        "firefox.exe",
        "msedge.exe",
        "opera.exe",
        "opera_gx.exe",
        "vivaldi.exe",
    }
)


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
    hwnd = foreground_window_handle()
    return window_bounds(hwnd)


def window_bounds(hwnd: int | None) -> tuple[int, int, int, int] | None:
    """Return visible bounds for a live, non-minimized top-level window."""
    if sys.platform != "win32" or not hwnd:
        return None
    user32 = ctypes.windll.user32
    if not user32.IsWindow(wintypes.HWND(hwnd)) or user32.IsIconic(wintypes.HWND(hwnd)):
        return None
    rect = RECT()
    if not user32.GetWindowRect(wintypes.HWND(hwnd), ctypes.byref(rect)):
        return None
    if rect.right <= rect.left or rect.bottom <= rect.top:
        return None
    return int(rect.left), int(rect.top), int(rect.right), int(rect.bottom)


def foreground_window_handle() -> int | None:
    """Return the current external foreground window, excluding CursorPocket itself."""
    if sys.platform != "win32":
        return None
    user32 = ctypes.windll.user32
    hwnd = int(user32.GetForegroundWindow() or 0)
    if not hwnd:
        return None
    pid = wintypes.DWORD()
    user32.GetWindowThreadProcessId(wintypes.HWND(hwnd), ctypes.byref(pid))
    if int(pid.value) == os.getpid():
        return None
    return hwnd


def window_process_name(hwnd: int | None) -> str:
    """Return the lowercase executable name that owns a top-level window."""
    if sys.platform != "win32" or not hwnd:
        return ""
    user32 = ctypes.windll.user32
    kernel32 = ctypes.windll.kernel32
    kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel32.OpenProcess.restype = wintypes.HANDLE
    kernel32.QueryFullProcessImageNameW.argtypes = [
        wintypes.HANDLE,
        wintypes.DWORD,
        wintypes.LPWSTR,
        ctypes.POINTER(wintypes.DWORD),
    ]
    kernel32.QueryFullProcessImageNameW.restype = wintypes.BOOL
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel32.CloseHandle.restype = wintypes.BOOL
    pid = wintypes.DWORD()
    user32.GetWindowThreadProcessId(wintypes.HWND(hwnd), ctypes.byref(pid))
    if not pid.value:
        return ""
    process = kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid.value)
    if not process:
        return ""
    try:
        buffer = ctypes.create_unicode_buffer(32768)
        size = wintypes.DWORD(len(buffer))
        if not kernel32.QueryFullProcessImageNameW(process, 0, buffer, ctypes.byref(size)):
            return ""
        return Path(buffer.value).name.lower()
    finally:
        kernel32.CloseHandle(process)


def is_supported_browser_window(hwnd: int | None) -> bool:
    return window_process_name(hwnd) in BROWSER_EXECUTABLES


def _activate_window(hwnd: int) -> bool:
    if sys.platform != "win32" or not hwnd:
        return False
    user32 = ctypes.windll.user32
    target = wintypes.HWND(hwnd)
    if not user32.IsWindow(target):
        return False
    if user32.IsIconic(target):
        user32.ShowWindowAsync(target, SW_RESTORE)
    current_thread = int(ctypes.windll.kernel32.GetCurrentThreadId())
    target_thread = int(user32.GetWindowThreadProcessId(target, None))
    foreground = wintypes.HWND(user32.GetForegroundWindow())
    foreground_thread = int(user32.GetWindowThreadProcessId(foreground, None)) if foreground else 0
    attached_threads: list[int] = []
    for thread_id in {foreground_thread, target_thread}:
        if (
            thread_id
            and thread_id != current_thread
            and user32.AttachThreadInput(current_thread, thread_id, True)
        ):
            attached_threads.append(thread_id)
    try:
        user32.BringWindowToTop(target)
        user32.SetForegroundWindow(target)
    finally:
        for thread_id in attached_threads:
            user32.AttachThreadInput(current_thread, thread_id, False)
    for _attempt in range(8):
        if int(user32.GetForegroundWindow() or 0) == hwnd:
            return True
        time.sleep(0.025)
    return False


def _key_event(virtual_key: int, key_up: bool = False) -> None:
    ctypes.windll.user32.keybd_event(
        virtual_key,
        0,
        KEYEVENTF_KEYUP if key_up else 0,
        0,
    )


def _copy_with_shortcut(hwnd: int, *, select_address: bool = False) -> bool:
    """Copy from a target window and prove the clipboard was actually updated."""
    if sys.platform == "win32":
        user32 = ctypes.windll.user32
        for _attempt in range(20):
            if not any(user32.GetAsyncKeyState(key) & 0x8000 for key in (0x10, 0x11, 0x12)):
                break
            time.sleep(0.015)
    if sys.platform != "win32" or not _activate_window(hwnd):
        return False
    user32 = ctypes.windll.user32
    before = int(user32.GetClipboardSequenceNumber())
    if select_address:
        _key_event(VK_CONTROL)
        _key_event(ord("L"))
        _key_event(ord("L"), key_up=True)
        _key_event(VK_CONTROL, key_up=True)
        time.sleep(0.045)
    _key_event(VK_CONTROL)
    _key_event(ord("C"))
    _key_event(ord("C"), key_up=True)
    _key_event(VK_CONTROL, key_up=True)
    if select_address:
        _key_event(VK_ESCAPE)
        _key_event(VK_ESCAPE, key_up=True)
    for _attempt in range(12):
        if int(user32.GetClipboardSequenceNumber()) != before:
            return True
        time.sleep(0.025)
    return False


def copy_selected_text(hwnd: int | None) -> bool:
    """Copy only an active selection. False means no new clipboard value appeared."""
    return bool(hwnd and _copy_with_shortcut(hwnd))


def copy_browser_url(hwnd: int | None) -> bool:
    """Copy the address bar only when the source window is a supported browser."""
    return bool(
        hwnd
        and is_supported_browser_window(hwnd)
        and _copy_with_shortcut(hwnd, select_address=True)
    )


def make_window_no_activate(window: object) -> None:
    """Keep a utility window clickable without stealing focus from the source app."""
    if sys.platform != "win32":
        return
    try:
        window.update_idletasks()
        user32 = ctypes.windll.user32
        user32.GetParent.argtypes = [wintypes.HWND]
        user32.GetParent.restype = wintypes.HWND
        client = wintypes.HWND(int(window.winfo_id()))
        wrapper = user32.GetParent(client) or client
        get_style = getattr(user32, "GetWindowLongPtrW", user32.GetWindowLongW)
        set_style = getattr(user32, "SetWindowLongPtrW", user32.SetWindowLongW)
        get_style.argtypes = [wintypes.HWND, ctypes.c_int]
        get_style.restype = ctypes.c_ssize_t
        set_style.argtypes = [wintypes.HWND, ctypes.c_int, ctypes.c_ssize_t]
        set_style.restype = ctypes.c_ssize_t
        style = int(get_style(wrapper, GWL_EXSTYLE))
        set_style(wrapper, GWL_EXSTYLE, style | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW)
    except (AttributeError, OSError, TypeError):
        return


def exclude_window_from_capture(window: object) -> bool:
    """Ask Windows to omit one CursorPocket top-level window from screen capture."""
    if sys.platform != "win32":
        return False
    try:
        window.update_idletasks()
        user32 = ctypes.windll.user32
        user32.GetParent.argtypes = [wintypes.HWND]
        user32.GetParent.restype = wintypes.HWND
        user32.SetWindowDisplayAffinity.argtypes = [wintypes.HWND, wintypes.DWORD]
        user32.SetWindowDisplayAffinity.restype = wintypes.BOOL
        client = wintypes.HWND(int(window.winfo_id()))
        wrapper = user32.GetParent(client) or client
        return bool(user32.SetWindowDisplayAffinity(wrapper, WDA_EXCLUDEFROMCAPTURE))
    except (AttributeError, OSError, TypeError):
        return False


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
    """Owns a per-user mutex and signals the running app on a second launch."""

    def __init__(
        self,
        name: str = "Local\\CursorPocket.SingleInstance",
        activation_event_name: str = "Local\\CursorPocket.ShowWindow",
    ) -> None:
        self.handle: int | None = None
        self.activation_event: int | None = None
        self.acquired = True
        if sys.platform != "win32":
            return
        kernel32 = ctypes.windll.kernel32
        kernel32.CreateEventW.argtypes = [
            ctypes.c_void_p,
            wintypes.BOOL,
            wintypes.BOOL,
            wintypes.LPCWSTR,
        ]
        kernel32.CreateEventW.restype = wintypes.HANDLE
        kernel32.SetEvent.argtypes = [wintypes.HANDLE]
        kernel32.SetEvent.restype = wintypes.BOOL
        kernel32.ResetEvent.argtypes = [wintypes.HANDLE]
        kernel32.ResetEvent.restype = wintypes.BOOL
        kernel32.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
        kernel32.WaitForSingleObject.restype = wintypes.DWORD
        kernel32.CreateMutexW.argtypes = [ctypes.c_void_p, wintypes.BOOL, wintypes.LPCWSTR]
        kernel32.CreateMutexW.restype = wintypes.HANDLE
        kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
        kernel32.CloseHandle.restype = wintypes.BOOL
        event = kernel32.CreateEventW(None, True, False, activation_event_name)
        if event:
            self.activation_event = int(event)
        kernel32.SetLastError(0)
        handle = kernel32.CreateMutexW(None, False, name)
        if not handle:
            self.acquired = False
            return
        self.handle = int(handle)
        if kernel32.GetLastError() == ERROR_ALREADY_EXISTS:
            self.acquired = False
            if self.activation_event:
                kernel32.SetEvent(wintypes.HANDLE(self.activation_event))
            kernel32.CloseHandle(handle)
            self.handle = None

    def consume_activation(self) -> bool:
        """Return True once for each pending request to show the existing app."""
        if not self.activation_event or sys.platform != "win32":
            return False
        kernel32 = ctypes.windll.kernel32
        event = wintypes.HANDLE(self.activation_event)
        if kernel32.WaitForSingleObject(event, 0) != WAIT_OBJECT_0:
            return False
        kernel32.ResetEvent(event)
        return True

    def close(self) -> None:
        if self.handle and sys.platform == "win32":
            ctypes.windll.kernel32.CloseHandle(wintypes.HANDLE(self.handle))
            self.handle = None
        if self.activation_event and sys.platform == "win32":
            ctypes.windll.kernel32.CloseHandle(wintypes.HANDLE(self.activation_event))
            self.activation_event = None

    def __del__(self) -> None:
        self.close()
