from __future__ import annotations

import ctypes
import queue
import sys
import unittest

from cursorpocket.hotkeys import (
    DEFAULT_HOTKEYS,
    MOD_ALT,
    MOD_CONTROL,
    MOD_NOREPEAT,
    MOD_SHIFT,
    GlobalHotkeyManager,
    HotkeySpec,
)


@unittest.skipUnless(sys.platform == "win32", "Windows-only hotkey test")
class HotkeyFallbackTests(unittest.TestCase):
    def test_defaults_use_the_keyboard_row_shortcut_pattern(self) -> None:
        labels = {hotkey.action: hotkey.label for hotkey in DEFAULT_HOTKEYS}

        self.assertEqual(labels["panel"], "Ctrl + Shift + Space")
        self.assertEqual(labels["screenshot"], "Ctrl + Shift + Q")
        self.assertEqual(labels["audio"], "Ctrl + Shift + A")
        self.assertEqual(labels["text"], "Ctrl + Shift + T")
        self.assertEqual(labels["link"], "Ctrl + Shift + L")
        self.assertEqual(labels["full_screenshot"], "Ctrl + Shift + 1")

    def test_uses_fallback_when_primary_shortcut_is_reserved(self) -> None:
        user32 = ctypes.windll.user32
        primary_id = 0xE101
        manager_id = 0xE102
        virtual_key = 0x7B  # F12
        primary_modifiers = MOD_CONTROL | MOD_ALT | MOD_NOREPEAT
        fallback_modifiers = primary_modifiers | MOD_SHIFT
        self.assertTrue(
            user32.RegisterHotKey(None, primary_id, primary_modifiers, virtual_key)
        )
        statuses: queue.Queue[tuple[str, str, bool]] = queue.Queue()
        errors: queue.Queue[str] = queue.Queue()
        manager = GlobalHotkeyManager(
            lambda _action: None,
            errors.put,
            lambda action, label, fallback: statuses.put((action, label, fallback)),
            hotkeys=(
                HotkeySpec(
                    manager_id,
                    primary_modifiers,
                    virtual_key,
                    "test",
                    "Ctrl + Alt + F12",
                    fallback_modifiers,
                    "Ctrl + Alt + Shift + F12",
                ),
            ),
        )
        try:
            manager.start()
            self.assertEqual(
                statuses.get(timeout=2.0),
                ("test", "Ctrl + Alt + Shift + F12", True),
            )
            self.assertTrue(errors.empty())
        finally:
            manager.stop()
            user32.UnregisterHotKey(None, primary_id)


if __name__ == "__main__":
    unittest.main()
