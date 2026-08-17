from __future__ import annotations

import math
import unittest
import tkinter as tk
from types import SimpleNamespace
from unittest.mock import patch

from PIL import Image

from cursorpocket.annotation import draw_arrow, draw_rectangle, draw_stroke, draw_text
from cursorpocket.app import (
    PANEL_SHORTCUT_HELP,
    CursorPocketApp,
    GREEN,
    RED,
    build_scrollable_panel,
    bind_toplevel_click,
    monitor_for_point,
    panel_key_action,
    panel_scroll_units,
)
from cursorpocket.gesture import DoubleCircleGestureDetector
from cursorpocket.windows import is_supported_browser_window


class FakeCanvas:
    def __init__(self) -> None:
        self.ovals: list[tuple[tuple[int, ...], dict[str, object]]] = []

    def delete(self, _tag: str) -> None:
        self.ovals.clear()

    def create_oval(self, *bounds: int, **options: object) -> None:
        self.ovals.append((bounds, options))


class CompanionTests(unittest.TestCase):
    def test_command_mode_renders_glow_legend_and_library_pulse(self) -> None:
        root = tk.Tk()
        root.withdraw()
        app = object.__new__(CursorPocketApp)
        app.root = root
        app._command_button_center = (0, 0)
        try:
            app._build_command_mode()
            app._render_command_mode(1280, 720)
            root.update_idletasks()

            texts = {
                app.command_canvas.itemcget(item, "text")
                for item in app.command_canvas.find_all()
                if app.command_canvas.type(item) == "text"
            }
            self.assertEqual(len(app.command_canvas.find_withtag("command_glow")), 4)
            self.assertEqual(len(app.command_canvas.find_withtag("command_pulse_outer")), 1)
            self.assertIn("Tap one key", texts)
            self.assertIn("OPEN LIBRARY", texts)
        finally:
            root.destroy()

    def test_command_pulse_opens_full_interface_without_resnapshotting(self) -> None:
        app = object.__new__(CursorPocketApp)
        app._command_button_center = (100, 100)
        calls: list[object] = []
        app.hide_command_mode = lambda: calls.append("hide")
        app.show_panel = lambda snapshot_context=True: calls.append(snapshot_context)

        app._command_mode_click(SimpleNamespace(x=106, y=96))

        self.assertEqual(calls, ["hide", False])

    def test_only_current_command_session_can_auto_close(self) -> None:
        app = object.__new__(CursorPocketApp)
        app.command_mode_open = True
        app._command_session = 4
        calls: list[str] = []
        app.hide_command_mode = lambda: calls.append("hide")

        app._expire_command_mode(3)
        self.assertEqual(calls, [])

        app._expire_command_mode(4)
        self.assertEqual(calls, ["hide"])

    def test_double_circle_opens_command_mode_even_when_dot_is_hidden(self) -> None:
        app = object.__new__(CursorPocketApp)
        app.closing = False
        app.hidden_mode = True
        app.capture_active = False
        app.panel_open = False
        app.command_mode_open = False
        app.recording = False
        app.settings_window = None
        app.settings = SimpleNamespace(mouse_gesture_enabled=True, follow_cursor=False)
        app.gesture_detector = DoubleCircleGestureDetector()
        app.root = SimpleNamespace(after=lambda *_args: None)
        opened: list[bool] = []

        def open_panel() -> None:
            opened.append(True)
            app.command_mode_open = True

        app.show_command_mode = open_panel
        points = [
            (
                round(200 + 32 * math.cos(4 * math.pi * index / 90)),
                round(160 + 32 * math.sin(4 * math.pi * index / 90)),
            )
            for index in range(91)
        ]
        times = iter(1.1 * index / 90 for index in range(91))
        app_positions = iter(points)

        with (
            patch("cursorpocket.app.cursor_position", side_effect=lambda: next(app_positions)),
            patch("cursorpocket.app.time.monotonic", side_effect=lambda: next(times)),
        ):
            for _point in points:
                app._follow_tick()
                if opened:
                    break

        self.assertEqual(opened, [True])

    def test_command_mode_uses_monitor_under_cursor(self) -> None:
        monitors = [(-1920, 0, 0, 1080), (0, 0, 2560, 1440)]

        self.assertEqual(monitor_for_point(monitors, -400, 500), monitors[0])
        self.assertEqual(monitor_for_point(monitors, 1200, 700), monitors[1])

    def test_capture_window_explains_that_shortcuts_are_individual_keys(self) -> None:
        help_text = PANEL_SHORTCUT_HELP.lower()

        self.assertIn("one key at a time", help_text)
        self.assertIn("do not hold", help_text)

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

    def test_panel_key_dispatches_each_shortcut_as_an_individual_key(self) -> None:
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

        expected = {
            "Q": "region",
            "W": "window",
            "E": "all",
            "R": "repeat",
            "A": "audio",
            "S": "save",
            "D": "discard",
            "F": "folder",
            "1": "monitor-0",
            "2": "monitor-1",
            "3": "monitor-2",
            "4": "monitor-3",
            "T": "text",
            "L": "link",
        }

        for key, expected_call in expected.items():
            with self.subTest(key=key):
                calls.clear()
                result = app._handle_panel_key(SimpleNamespace(state=8, keysym=key))
                self.assertEqual(result, "break")
                self.assertEqual(calls, [expected_call])

        calls.clear()
        self.assertIsNone(app._handle_panel_key(SimpleNamespace(state=0x4, keysym="Q")))
        self.assertIsNone(app._handle_panel_key(SimpleNamespace(state=0x20000, keysym="Q")))
        self.assertEqual(calls, [])

        app.panel_open = False
        app.command_mode_open = True
        app.hide_command_mode = lambda: setattr(app, "command_mode_open", False)
        result = app._handle_panel_key(SimpleNamespace(state=8, keysym="Q"))

        self.assertEqual(result, "break")
        self.assertEqual(calls, ["region"])
        self.assertFalse(app.command_mode_open)

    def test_panel_wheel_delta_always_produces_a_scroll_step(self) -> None:
        self.assertEqual(panel_scroll_units(120), -1)
        self.assertEqual(panel_scroll_units(-120), 1)
        self.assertEqual(panel_scroll_units(30), -1)
        self.assertEqual(panel_scroll_units(0), 0)

    def test_capture_panel_has_a_visible_working_scrollbar(self) -> None:
        root = tk.Tk()
        root.geometry("320x240-10000-10000")
        try:
            canvas, content, scrollbar = build_scrollable_panel(root)
            for index in range(40):
                tk.Label(content, text=f"Capture action {index}").pack()
            root.update()
            before = canvas.yview()
            canvas.yview_scroll(4, "units")
            root.update()

            self.assertEqual(scrollbar.winfo_manager(), "pack")
            self.assertLess(before[1], 1.0)
            self.assertGreater(canvas.yview()[0], before[0])
        finally:
            root.destroy()

    def test_toplevel_click_binding_fires_once_for_a_child_click(self) -> None:
        root = tk.Tk()
        root.geometry("20x20-10000-10000")
        label = tk.Label(root, text="Open")
        label.pack()
        calls: list[str] = []
        try:
            bind_toplevel_click(root, lambda _event: calls.append("open"))
            root.update()
            label.event_generate("<ButtonPress-1>", x=1, y=1)
            root.update()
            self.assertEqual(calls, ["open"])
        finally:
            root.destroy()

    def test_link_capture_ignores_a_stale_clipboard_when_no_browser_page_exists(self) -> None:
        app = object.__new__(CursorPocketApp)
        app.capture_active = False
        app.panel_open = False
        app._capture_source_window = lambda: 321
        app.hide_panel = lambda: None
        app._clipboard_text = lambda: "https://stale.example/page"
        app._restore_companion = lambda: None
        saved: list[str] = []
        notices: list[str] = []
        app._save_link = saved.append
        app.show_toast = lambda title, _detail, error=False: notices.append(title)

        with patch("cursorpocket.app.copy_browser_url", return_value=False):
            app.capture_link()

        self.assertEqual(saved, [])
        self.assertEqual(notices, ["No webpage detected"])
        self.assertFalse(app.capture_active)

    def test_link_and_text_capture_use_fresh_source_window_content(self) -> None:
        app = object.__new__(CursorPocketApp)
        app.capture_active = False
        app.panel_open = False
        app._capture_source_window = lambda: 321
        app.hide_panel = lambda: None
        app._restore_companion = lambda: None
        app.show_toast = lambda *_args, **_kwargs: None
        clipboard = iter(("https://current.example/page", "Highlighted words"))
        app._clipboard_text = lambda: next(clipboard)
        links: list[str] = []
        texts: list[str] = []
        app._save_link = links.append
        app._save_text = texts.append

        with (
            patch("cursorpocket.app.copy_browser_url", return_value=True),
            patch("cursorpocket.app.copy_selected_text", return_value=True),
        ):
            app.capture_link()
            app.capture_active = False
            app.capture_text()

        self.assertEqual(links, ["https://current.example/page"])
        self.assertEqual(texts, ["Highlighted words"])

    def test_panel_context_snapshot_keeps_selection_and_page_separately(self) -> None:
        app = object.__new__(CursorPocketApp)
        clipboard = iter(("Highlighted paragraph", "https://current.example/article"))
        app._clipboard_text = lambda: next(clipboard)

        with (
            patch("cursorpocket.app.copy_selected_text", return_value=True),
            patch("cursorpocket.app.copy_browser_url", return_value=True),
        ):
            app._snapshot_source_context(321)

        self.assertEqual(app.source_selected_text, "Highlighted paragraph")
        self.assertEqual(app.source_page_url, "https://current.example/article")

    def test_annotation_primitives_change_the_full_resolution_image(self) -> None:
        original = Image.new("RGB", (120, 80), "white")
        marked = draw_stroke(original, [(5, 5), (50, 5)], "#FF0000", 5)
        marked = draw_rectangle(marked, (10, 15), (60, 45), "#00AA00", 3)
        marked = draw_arrow(marked, (70, 60), (105, 25), "#0000FF", 4)
        marked = draw_text(marked, (8, 50), "Note", "#111111", 16)

        self.assertEqual(marked.size, original.size)
        self.assertNotEqual(marked.convert("RGB").tobytes(), original.tobytes())

    def test_browser_detection_is_process_scoped(self) -> None:
        with patch("cursorpocket.windows.window_process_name", return_value="chrome.exe"):
            self.assertTrue(is_supported_browser_window(123))
        with patch("cursorpocket.windows.window_process_name", return_value="notepad.exe"):
            self.assertFalse(is_supported_browser_window(123))


if __name__ == "__main__":
    unittest.main()
