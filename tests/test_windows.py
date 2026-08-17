from __future__ import annotations

import ctypes
import sys
import tkinter as tk
import unittest
from ctypes import wintypes

from cursorpocket.windows import position_window


@unittest.skipUnless(sys.platform == "win32", "Windows-only positioning test")
class WindowPositionTests(unittest.TestCase):
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
