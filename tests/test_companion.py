from __future__ import annotations

import unittest
from types import SimpleNamespace

from cursorpocket.app import CursorPocketApp, GREEN, RED, panel_key_action


class FakeCanvas:
    def __init__(self) -> None:
        self.ovals: list[tuple[tuple[int, ...], dict[str, object]]] = []

    def delete(self, _tag: str) -> None:
        self.ovals.clear()

    def create_oval(self, *bounds: int, **options: object) -> None:
        self.ovals.append((bounds, options))


class CompanionTests(unittest.TestCase):
    def test_companion_is_small_borderless_and_uses_status_color(self) -> None:
        app = object.__new__(CursorPocketApp)
        app.companion_canvas = FakeCanvas()

        app.recording = False
        app._draw_companion(False)
        self.assertEqual(len(app.companion_canvas.ovals), 1)
        bounds, options = app.companion_canvas.ovals[0]
        self.assertEqual(bounds[2] - bounds[0], 8)
        self.assertEqual(options, {"fill": GREEN, "outline": ""})

        app.recording = True
        app._draw_companion(False)
        self.assertEqual(app.companion_canvas.ovals[0][1]["fill"], RED)

    def test_companion_tracks_directly_beside_cursor(self) -> None:
        self.assertEqual(CursorPocketApp._companion_target(100, 200), (108, 205))
        self.assertLessEqual(CursorPocketApp.COMPANION_SIZE, 18)

    def test_panel_keyboard_rows_are_case_and_numpad_independent(self) -> None:
        self.assertEqual(panel_key_action("q"), "region_screenshot")
        self.assertEqual(panel_key_action("Q"), "region_screenshot")
        self.assertEqual(panel_key_action("A"), "toggle_audio")
        self.assertEqual(panel_key_action("KP_1"), "monitor_1")
        self.assertEqual(panel_key_action("4"), "monitor_4")
        self.assertIsNone(panel_key_action("Escape"))

    def test_panel_key_dispatch_does_not_depend_on_child_focus(self) -> None:
        app = object.__new__(CursorPocketApp)
        app.panel_open = True
        calls: list[str] = []
        app.capture_screenshot = lambda: calls.append("region")
        app.capture_active_window = lambda: calls.append("window")
        app.capture_all_screens = lambda: calls.append("all")
        app.repeat_last_region = lambda: calls.append("repeat")
        app.toggle_audio_recording = lambda: calls.append("audio")
        app.stop_audio_recording = lambda: calls.append("save")
        app.discard_audio_recording = lambda: calls.append("discard")
        app.open_audio_folder = lambda: calls.append("folder")
        app.capture_text = lambda: calls.append("text")
        app.capture_link = lambda: calls.append("link")
        app.capture_monitor = lambda index: calls.append(f"monitor-{index}")

        result = app._handle_panel_key(SimpleNamespace(state=0, keysym="Q"))

        self.assertEqual(result, "break")
        self.assertEqual(calls, ["region"])


if __name__ == "__main__":
    unittest.main()
