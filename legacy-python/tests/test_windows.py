from __future__ import annotations

import ctypes
import sys
import tkinter as tk
import unittest
import uuid
from ctypes import wintypes
from types import SimpleNamespace
from unittest.mock import MagicMock, patch

import cursorpocket.windows as windows
from cursorpocket.windows import SingleInstance, exclude_window_from_capture, position_window


@unittest.skipUnless(sys.platform == "win32", "Windows-only activation test")
class WindowActivationTests(unittest.TestCase):
    @staticmethod
    def _fake_windows_api(hwnd: int, *, minimized: bool) -> tuple[MagicMock, MagicMock]:
        user32 = MagicMock()
        user32.IsWindow.return_value = True
        user32.IsIconic.return_value = minimized
        user32.GetForegroundWindow.return_value = hwnd
        user32.GetWindowThreadProcessId.return_value = 11
        kernel32 = MagicMock()
        kernel32.GetCurrentThreadId.return_value = 11
        return user32, kernel32

    def test_activating_visible_window_does_not_restore_its_window_state(self) -> None:
        hwnd = 404
        user32, kernel32 = self._fake_windows_api(hwnd, minimized=False)

        with patch.object(
            windows.ctypes,
            "windll",
            SimpleNamespace(user32=user32, kernel32=kernel32),
        ):
            self.assertTrue(windows._activate_window(hwnd))

        user32.ShowWindowAsync.assert_not_called()

    def test_activating_minimized_window_still_restores_it(self) -> None:
        hwnd = 405
        user32, kernel32 = self._fake_windows_api(hwnd, minimized=True)

        with patch.object(
            windows.ctypes,
            "windll",
            SimpleNamespace(user32=user32, kernel32=kernel32),
        ):
            self.assertTrue(windows._activate_window(hwnd))

        user32.ShowWindowAsync.assert_called_once_with(
            unittest.mock.ANY,
            windows.SW_RESTORE,
        )


@unittest.skipUnless(sys.platform == "win32", "Windows-only positioning test")
class WindowPositionTests(unittest.TestCase):
    def test_top_level_can_be_excluded_from_supported_screen_capture(self) -> None:
        root = tk.Tk()
        root.withdraw()
        window = tk.Toplevel(root)
        window.overrideredirect(True)
        try:
            self.assertTrue(exclude_window_from_capture(window))
        finally:
            window.destroy()
            root.destroy()

    def test_position_window_moves_the_tk_toplevel_wrapper(self) -> None:
        root = tk.Tk()
        root.withdraw()
        window = tk.Toplevel(root)
        window.overrideredirect(True)
        window.geometry("34x34+0+0")
        window.update_idletasks()
        try:
            position_window(window, 420, 310, 34, 34)
            window.update()

            user32 = ctypes.windll.user32
            user32.GetParent.argtypes = [wintypes.HWND]
            user32.GetParent.restype = wintypes.HWND
            client_hwnd = wintypes.HWND(window.winfo_id())
            wrapper_hwnd = user32.GetParent(client_hwnd)
            rect = wintypes.RECT()
            self.assertTrue(user32.GetWindowRect(wrapper_hwnd, ctypes.byref(rect)))
            self.assertEqual((rect.left, rect.top), (420, 310))
            self.assertEqual((window.winfo_x(), window.winfo_y()), (420, 310))
        finally:
            window.destroy()
            root.destroy()

    def test_second_instance_signals_the_first_to_open(self) -> None:
        token = uuid.uuid4().hex
        first = SingleInstance(
            name=f"Local\\CursorPocket.Test.{token}",
            activation_event_name=f"Local\\CursorPocket.Test.Show.{token}",
        )
        second = SingleInstance(
            name=f"Local\\CursorPocket.Test.{token}",
            activation_event_name=f"Local\\CursorPocket.Test.Show.{token}",
        )
        try:
            self.assertTrue(first.acquired)
            self.assertFalse(second.acquired)
            self.assertTrue(first.consume_activation())
            self.assertFalse(first.consume_activation())
        finally:
            second.close()
            first.close()


class SingleInstanceFallbackTests(unittest.TestCase):
    @unittest.skipIf(sys.platform == "win32", "Non-Windows fallback behavior")
    def test_activation_check_is_safely_disabled_off_windows(self) -> None:
        instance = SingleInstance()
        self.assertTrue(instance.acquired)
        self.assertFalse(instance.consume_activation())
