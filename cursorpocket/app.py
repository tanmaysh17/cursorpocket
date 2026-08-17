from __future__ import annotations

import os
import queue
import sys
import time
import tkinter as tk
from datetime import datetime
from pathlib import Path
from tkinter import filedialog, messagebox
from typing import Callable

from PIL import Image, ImageDraw, ImageEnhance, ImageGrab, ImageTk

from .annotation import ScreenshotAnnotator
from .audio import AudioRecorder
from .hotkeys import DEFAULT_HOTKEYS, GlobalHotkeyManager
from .settings import AppSettings, SettingsStore
from .startup import StartupManager
from .storage import CaptureRecord, CaptureStore, is_web_url
from .tray import TrayManager
from .windows import (
    copy_browser_url,
    copy_selected_text,
    cursor_position,
    foreground_window_handle,
    foreground_window_bounds,
    make_window_no_activate,
    monitor_bounds,
    position_window,
    virtual_screen_bounds,
)


INK = "#10151D"
PANEL = "#18212D"
PANEL_RAISED = "#202B39"
LINE = "#2D3A4B"
PAPER = "#F4F7FB"
MUTED = "#8D9AAD"
BLUE = "#63B3FF"
BLUE_ACTIVE = "#88C6FF"
ORANGE = "#FFB86B"
GREEN = "#42D392"
RED = "#FF5D68"
TRANSPARENT = "#FF00FF"

FONT_BODY = "Segoe UI"
FONT_DISPLAY = "Segoe UI Variable Display"
FONT_MONO = "Cascadia Mono"

PANEL_KEY_ACTIONS = {
    "q": "region_screenshot",
    "w": "window_screenshot",
    "e": "all_screens",
    "r": "repeat_region",
    "a": "toggle_audio",
    "s": "save_audio",
    "d": "discard_audio",
    "f": "audio_folder",
    "1": "monitor_1",
    "2": "monitor_2",
    "3": "monitor_3",
    "4": "monitor_4",
    "t": "text",
    "l": "link",
}


def panel_key_action(keysym: str) -> str | None:
    normalized = keysym.lower()
    if normalized.startswith("kp_"):
        normalized = normalized[3:]
    return PANEL_KEY_ACTIONS.get(normalized)


def _bind_tree(widget: tk.Misc, sequence: str, callback: Callable) -> None:
    widget.bind(sequence, callback)
    for child in widget.winfo_children():
        _bind_tree(child, sequence, callback)


def bind_toplevel_click(window: tk.Misc, callback: Callable) -> None:
    """Bind once through Tk's toplevel bind tag so child clicks do not duplicate."""
    window.bind("<Button-1>", callback)


def panel_scroll_units(delta: int) -> int:
    """Translate a Windows mouse-wheel delta into a useful Tk scroll step."""
    if delta == 0:
        return 0
    steps = max(1, abs(delta) // 120)
    return -steps if delta > 0 else steps


def build_scrollable_panel(parent: tk.Misc) -> tuple[tk.Canvas, tk.Frame, tk.Scrollbar]:
    """Create the capture window's visible, vertically scrollable content area."""
    viewport = tk.Frame(parent, bg=PANEL)
    viewport.pack(fill="both", expand=True)
    canvas = tk.Canvas(
        viewport,
        bg=PANEL,
        highlightthickness=0,
        bd=0,
        relief="flat",
    )
    scrollbar = tk.Scrollbar(
        viewport,
        orient="vertical",
        command=canvas.yview,
        width=12,
        bg=PANEL_RAISED,
        activebackground=BLUE,
        troughcolor=INK,
        relief="flat",
        bd=0,
        highlightthickness=0,
    )
    canvas.configure(yscrollcommand=scrollbar.set)
    scrollbar.pack(side="right", fill="y")
    canvas.pack(side="left", fill="both", expand=True)
    content = tk.Frame(canvas, bg=PANEL, padx=20, pady=18)
    content_window = canvas.create_window(0, 0, anchor="nw", window=content)

    def update_scroll_region(_event: tk.Event) -> None:
        canvas.configure(scrollregion=canvas.bbox("all"))

    def fit_content_width(event: tk.Event) -> None:
        canvas.itemconfigure(content_window, width=max(1, int(event.width)))

    content.bind("<Configure>", update_scroll_region)
    canvas.bind("<Configure>", fit_content_width)
    return canvas, content, scrollbar


class RegionSelector:
    def __init__(
        self,
        root: tk.Tk,
        image: Image.Image,
        virtual_origin: tuple[int, int],
        on_select: Callable[[Image.Image, tuple[int, int, int, int]], None],
        on_cancel: Callable[[], None],
    ) -> None:
        self.root = root
        self.image = image
        self.origin_x, self.origin_y = virtual_origin
        self.on_select = on_select
        self.on_cancel = on_cancel
        self.start: tuple[int, int] | None = None
        self.rect_id: int | None = None
        self.size_id: int | None = None
        self.selection_image_id: int | None = None
        self.selection_photo: ImageTk.PhotoImage | None = None
        self.closed = False

        self.window = tk.Toplevel(root)
        self.window.overrideredirect(True)
        self.window.attributes("-topmost", True)
        self.window.configure(bg=INK)
        self.canvas = tk.Canvas(
            self.window,
            width=image.width,
            height=image.height,
            bg=INK,
            highlightthickness=0,
            cursor="crosshair",
        )
        self.canvas.pack(fill="both", expand=True)

        dimmed = ImageEnhance.Brightness(image.convert("RGB")).enhance(0.46)
        self.background_photo = ImageTk.PhotoImage(dimmed)
        self.canvas.create_image(0, 0, anchor="nw", image=self.background_photo)
        self.canvas.create_rectangle(18, 18, 472, 78, fill=INK, outline=LINE, width=1)
        self.canvas.create_text(
            36,
            38,
            anchor="nw",
            fill=PAPER,
            font=(FONT_DISPLAY, 13, "bold"),
            text="Drag to capture a region",
        )
        self.canvas.create_text(
            36,
            61,
            anchor="nw",
            fill=MUTED,
            font=(FONT_BODY, 9),
            text="Enter captures every screen  ·  Esc cancels",
        )

        position_window(
            self.window,
            self.origin_x,
            self.origin_y,
            image.width,
            image.height,
            activate=True,
        )
        self.window.lift()
        self.window.focus_force()
        self.window.grab_set()
        self.canvas.bind("<ButtonPress-1>", self._press)
        self.canvas.bind("<B1-Motion>", self._drag)
        self.canvas.bind("<ButtonRelease-1>", self._release)
        self.window.bind("<Escape>", lambda _event: self.cancel())
        self.window.bind("<Button-3>", lambda _event: self.cancel())
        self.window.bind("<Return>", self._capture_full)

    def _press(self, event: tk.Event) -> None:
        self.start = self._clamp(event.x, event.y)
        if self.selection_image_id:
            self.canvas.delete(self.selection_image_id)
            self.selection_image_id = None
        if self.rect_id:
            self.canvas.delete(self.rect_id)
        if self.size_id:
            self.canvas.delete(self.size_id)
        x, y = self.start
        self.rect_id = self.canvas.create_rectangle(x, y, x, y, outline=BLUE, width=2)

    def _drag(self, event: tk.Event) -> None:
        if not self.start or not self.rect_id:
            return
        x, y = self._clamp(event.x, event.y)
        x1, y1, x2, y2 = self._ordered(self.start[0], self.start[1], x, y)
        if x2 <= x1 or y2 <= y1:
            return
        self.canvas.coords(self.rect_id, x1, y1, x2, y2)
        crop = self.image.crop((x1, y1, x2, y2))
        self.selection_photo = ImageTk.PhotoImage(crop)
        if self.selection_image_id:
            self.canvas.itemconfigure(self.selection_image_id, image=self.selection_photo)
            self.canvas.coords(self.selection_image_id, x1, y1)
        else:
            self.selection_image_id = self.canvas.create_image(
                x1, y1, anchor="nw", image=self.selection_photo
            )
        self.canvas.tag_raise(self.rect_id)
        if self.size_id:
            self.canvas.delete(self.size_id)
        self.size_id = self.canvas.create_text(
            x1 + 8,
            max(10, y1 - 23),
            anchor="nw",
            fill=PAPER,
            font=(FONT_MONO, 9, "bold"),
            text=f"{x2 - x1} × {y2 - y1}",
        )
        self.canvas.tag_raise(self.size_id)

    def _release(self, event: tk.Event) -> None:
        if not self.start:
            return
        x, y = self._clamp(event.x, event.y)
        x1, y1, x2, y2 = self._ordered(self.start[0], self.start[1], x, y)
        if x2 - x1 < 6 or y2 - y1 < 6:
            self.start = None
            return
        crop = self.image.crop((x1, y1, x2, y2))
        bounds = (
            self.origin_x + x1,
            self.origin_y + y1,
            self.origin_x + x2,
            self.origin_y + y2,
        )
        self._close()
        self.on_select(crop, bounds)

    def _capture_full(self, _event: tk.Event | None = None) -> None:
        bounds = (
            self.origin_x,
            self.origin_y,
            self.origin_x + self.image.width,
            self.origin_y + self.image.height,
        )
        image = self.image.copy()
        self._close()
        self.on_select(image, bounds)

    def cancel(self) -> None:
        self._close()
        self.on_cancel()

    def _close(self) -> None:
        if self.closed:
            return
        self.closed = True
        try:
            self.window.grab_release()
        except tk.TclError:
            pass
        self.window.destroy()

    def _clamp(self, x: int, y: int) -> tuple[int, int]:
        return max(0, min(self.image.width, x)), max(0, min(self.image.height, y))

    @staticmethod
    def _ordered(x1: int, y1: int, x2: int, y2: int) -> tuple[int, int, int, int]:
        return min(x1, x2), min(y1, y2), max(x1, x2), max(y1, y2)


class TextEditor:
    def __init__(
        self,
        root: tk.Tk,
        initial: str,
        on_save: Callable[[str], None],
        on_close: Callable[[], None],
    ) -> None:
        self.on_save = on_save
        self.on_close = on_close
        self.window = tk.Toplevel(root)
        self.window.overrideredirect(True)
        self.window.attributes("-topmost", True)
        self.window.configure(bg=BLUE)
        self.window.bind("<Escape>", lambda _event: self.close())
        self.window.bind("<Control-Return>", self._save)

        shell = tk.Frame(self.window, bg=PANEL, padx=18, pady=16)
        shell.pack(fill="both", expand=True, padx=1, pady=1)
        tk.Label(
            shell,
            text="TEXT CAPTURE",
            bg=PANEL,
            fg=BLUE,
            font=(FONT_MONO, 9, "bold"),
        ).pack(anchor="w")
        tk.Label(
            shell,
            text="Keep the useful part.",
            bg=PANEL,
            fg=PAPER,
            font=(FONT_DISPLAY, 18, "bold"),
        ).pack(anchor="w", pady=(4, 12))
        self.text = tk.Text(
            shell,
            bg=INK,
            fg=PAPER,
            insertbackground=BLUE,
            selectbackground="#315A7E",
            relief="flat",
            wrap="word",
            padx=12,
            pady=10,
            font=(FONT_BODY, 11),
            undo=True,
        )
        self.text.pack(fill="both", expand=True)
        if initial:
            self.text.insert("1.0", initial[:100000])
            self.text.tag_add("sel", "1.0", "end-1c")

        footer = tk.Frame(shell, bg=PANEL)
        footer.pack(fill="x", pady=(12, 0))
        tk.Label(
            footer,
            text="Ctrl + Enter to save",
            bg=PANEL,
            fg=MUTED,
            font=(FONT_MONO, 8),
        ).pack(side="left")
        self._button(footer, "Cancel", self.close, PANEL_RAISED, PAPER).pack(side="right")
        self._button(footer, "Save text", self._save, BLUE, INK).pack(side="right", padx=(0, 8))

        x, y = cursor_position()
        vx, vy, vw, vh = virtual_screen_bounds()
        width, height = 430, 330
        px = min(max(vx + 14, x + 34), vx + vw - width - 14)
        py = min(max(vy + 14, y - 50), vy + vh - height - 14)
        position_window(self.window, px, py, width, height, activate=True)
        self.window.lift()
        self.window.focus_force()
        self.text.focus_set()
        self.window.grab_set()

    def _button(
        self,
        parent: tk.Misc,
        text: str,
        command: Callable,
        bg: str,
        fg: str,
    ) -> tk.Button:
        return tk.Button(
            parent,
            text=text,
            command=command,
            bg=bg,
            fg=fg,
            activebackground=BLUE_ACTIVE if bg == BLUE else LINE,
            activeforeground=INK if bg == BLUE else PAPER,
            relief="flat",
            bd=0,
            padx=14,
            pady=7,
            cursor="hand2",
            font=(FONT_BODY, 9, "bold"),
        )

    def _save(self, _event: tk.Event | None = None) -> str:
        value = self.text.get("1.0", "end-1c")
        if not value.strip():
            self.text.configure(highlightthickness=1, highlightbackground=ORANGE)
            return "break"
        self._destroy()
        self.on_save(value)
        return "break"

    def close(self) -> None:
        self._destroy()
        self.on_close()

    def _destroy(self) -> None:
        try:
            self.window.grab_release()
        except tk.TclError:
            pass
        self.window.destroy()


class LinkEditor:
    def __init__(
        self,
        root: tk.Tk,
        initial: str,
        on_save: Callable[[str], None],
        on_close: Callable[[], None],
    ) -> None:
        self.on_save = on_save
        self.on_close = on_close
        self.window = tk.Toplevel(root)
        self.window.overrideredirect(True)
        self.window.attributes("-topmost", True)
        self.window.configure(bg=BLUE)
        self.window.bind("<Escape>", lambda _event: self.close())
        self.window.bind("<Return>", self._save)

        shell = tk.Frame(self.window, bg=PANEL, padx=18, pady=16)
        shell.pack(fill="both", expand=True, padx=1, pady=1)
        tk.Label(shell, text="LINK CAPTURE", bg=PANEL, fg=BLUE, font=(FONT_MONO, 9, "bold")).pack(anchor="w")
        tk.Label(
            shell,
            text="Save a page for later.",
            bg=PANEL,
            fg=PAPER,
            font=(FONT_DISPLAY, 18, "bold"),
        ).pack(anchor="w", pady=(4, 12))
        self.value = tk.StringVar(value=initial[:4000])
        self.entry = tk.Entry(
            shell,
            textvariable=self.value,
            bg=INK,
            fg=PAPER,
            insertbackground=BLUE,
            selectbackground="#315A7E",
            relief="flat",
            font=(FONT_BODY, 10),
        )
        self.entry.pack(fill="x", ipady=10)
        self.error = tk.Label(shell, text="", bg=PANEL, fg=ORANGE, font=(FONT_BODY, 9))
        self.error.pack(anchor="w", pady=(7, 0))
        footer = tk.Frame(shell, bg=PANEL)
        footer.pack(fill="x", side="bottom")
        self._button(footer, "Cancel", self.close, PANEL_RAISED, PAPER).pack(side="right")
        self._button(footer, "Save link", self._save, BLUE, INK).pack(side="right", padx=(0, 8))

        x, y = cursor_position()
        vx, vy, vw, vh = virtual_screen_bounds()
        width, height = 430, 190
        px = min(max(vx + 14, x + 34), vx + vw - width - 14)
        py = min(max(vy + 14, y - 30), vy + vh - height - 14)
        position_window(self.window, px, py, width, height, activate=True)
        self.window.lift()
        self.window.focus_force()
        self.entry.focus_set()
        self.entry.select_range(0, "end")
        self.window.grab_set()

    def _button(self, parent: tk.Misc, text: str, command: Callable, bg: str, fg: str) -> tk.Button:
        return tk.Button(
            parent,
            text=text,
            command=command,
            bg=bg,
            fg=fg,
            activebackground=BLUE_ACTIVE if bg == BLUE else LINE,
            activeforeground=INK if bg == BLUE else PAPER,
            relief="flat",
            bd=0,
            padx=14,
            pady=7,
            cursor="hand2",
            font=(FONT_BODY, 9, "bold"),
        )

    def _save(self, _event: tk.Event | None = None) -> str:
        value = self.value.get().strip()
        if not is_web_url(value):
            self.error.configure(text="Enter a complete http:// or https:// link.")
            return "break"
        self._destroy()
        self.on_save(value)
        return "break"

    def close(self) -> None:
        self._destroy()
        self.on_close()

    def _destroy(self) -> None:
        try:
            self.window.grab_release()
        except tk.TclError:
            pass
        self.window.destroy()


class RecordingIndicator:
    def __init__(self, root: tk.Tk, on_stop: Callable[[], None]) -> None:
        self.started_at = 0.0
        self.visible = False
        self.window = tk.Toplevel(root)
        self.window.withdraw()
        self.window.overrideredirect(True)
        self.window.attributes("-topmost", True)
        self.window.configure(bg=RED)
        inner = tk.Frame(self.window, bg=PANEL, padx=12, pady=8)
        inner.pack(fill="both", expand=True, padx=1, pady=1)
        tk.Label(
            inner,
            text="●",
            bg=PANEL,
            fg=RED,
            font=(FONT_BODY, 10, "bold"),
        ).pack(side="left")
        self.label = tk.Label(
            inner,
            text="Recording 00:00",
            bg=PANEL,
            fg=PAPER,
            font=(FONT_BODY, 9, "bold"),
        )
        self.label.pack(side="left", padx=(7, 12))
        tk.Button(
            inner,
            text="Stop",
            command=on_stop,
            bg=RED,
            fg=INK,
            activebackground="#FF7B84",
            activeforeground=INK,
            relief="flat",
            bd=0,
            padx=12,
            pady=4,
            cursor="hand2",
            font=(FONT_BODY, 8, "bold"),
        ).pack(side="right")

    def show(self, started_at: float | None = None) -> None:
        if started_at is not None:
            self.started_at = started_at
        self.visible = True
        width, height = 210, 48
        vx, vy, vw, _vh = virtual_screen_bounds()
        position_window(self.window, vx + vw - width - 16, vy + 16, width, height)
        self.window.deiconify()
        self.window.lift()
        self._tick()

    def hide(self) -> None:
        self.visible = False
        self.window.withdraw()

    def _tick(self) -> None:
        if not self.visible:
            return
        elapsed = max(0, int(time.monotonic() - self.started_at))
        minutes, seconds = divmod(elapsed, 60)
        self.label.configure(text=f"Recording {minutes:02d}:{seconds:02d}")
        self.window.after(500, self._tick)


class SettingsWindow:
    WIDTH = 500
    HEIGHT = 570

    def __init__(
        self,
        root: tk.Tk,
        settings: AppSettings,
        startup_enabled: bool,
        shortcuts: dict[str, str],
        on_save: Callable[[AppSettings, bool], None],
        on_close: Callable[[], None],
    ) -> None:
        self.settings = settings
        self.on_save = on_save
        self.on_close = on_close
        self.closed = False
        self.window = tk.Toplevel(root)
        self.window.title("CursorPocket settings")
        self.window.attributes("-topmost", True)
        self.window.resizable(False, False)
        self.window.configure(bg=LINE)
        self.window.protocol("WM_DELETE_WINDOW", self.close)
        self.window.bind("<Escape>", lambda _event: self.close())

        shell = tk.Frame(self.window, bg=PANEL, padx=24, pady=22)
        shell.pack(fill="both", expand=True, padx=1, pady=1)
        tk.Label(
            shell,
            text="Settings",
            bg=PANEL,
            fg=PAPER,
            font=(FONT_DISPLAY, 20, "bold"),
        ).pack(anchor="w")
        tk.Label(
            shell,
            text="Keep CursorPocket ready without getting in your way.",
            bg=PANEL,
            fg=MUTED,
            font=(FONT_BODY, 9),
        ).pack(anchor="w", pady=(4, 18))

        self.startup_var = tk.BooleanVar(value=startup_enabled)
        self.follow_var = tk.BooleanVar(value=settings.follow_cursor)
        self._check(
            shell,
            "Start CursorPocket when I sign in",
            "Keeps the tray icon and capture shortcut ready after Windows starts.",
            self.startup_var,
        )
        self._check(
            shell,
            "Keep the green dot beside my cursor",
            "Turn this off if you prefer the tray icon and keyboard menu.",
            self.follow_var,
        )

        self._section_label(shell, "SAVE LOCATION").pack(anchor="w", pady=(19, 7))
        folder_row = tk.Frame(shell, bg=PANEL_RAISED, padx=12, pady=10)
        folder_row.pack(fill="x")
        self.folder_var = tk.StringVar(value=settings.capture_dir)
        tk.Label(
            folder_row,
            textvariable=self.folder_var,
            bg=PANEL_RAISED,
            fg=PAPER,
            anchor="w",
            justify="left",
            wraplength=330,
            font=(FONT_BODY, 9),
        ).pack(side="left", fill="x", expand=True)
        self._button(folder_row, "Change…", self._choose_folder).pack(side="right", padx=(10, 0))

        self._section_label(shell, "SHORTCUTS").pack(anchor="w", pady=(19, 7))
        shortcut_frame = tk.Frame(shell, bg=PANEL)
        shortcut_frame.pack(fill="x")
        rows = (
            ("Open capture menu", shortcuts.get("panel", "Ctrl + Shift + Space")),
            ("Screenshots in the capture window", "Q / W / E / R"),
            ("Audio in the capture window", "A / S / D / F"),
            ("Full displays in the capture window", "1 / 2 / 3 / 4"),
            ("Clipboard captures", "T for text · L for link"),
        )
        for label, shortcut in rows:
            row = tk.Frame(shortcut_frame, bg=PANEL)
            row.pack(fill="x", pady=2)
            tk.Label(row, text=label, bg=PANEL, fg=PAPER, font=(FONT_BODY, 9)).pack(side="left")
            tk.Label(row, text=shortcut, bg=PANEL, fg=MUTED, font=(FONT_MONO, 8)).pack(side="right")

        footer = tk.Frame(shell, bg=PANEL)
        footer.pack(fill="x", side="bottom", pady=(20, 0))
        self._button(footer, "Cancel", self.close).pack(side="right")
        self._button(footer, "Save settings", self._save, primary=True).pack(side="right", padx=(0, 8))

        self.window.update_idletasks()
        vx, vy, vw, vh = virtual_screen_bounds()
        px = vx + max(12, (vw - self.WIDTH) // 2)
        py = vy + max(12, (vh - self.HEIGHT) // 2)
        position_window(self.window, px, py, self.WIDTH, self.HEIGHT, activate=True)
        self.window.lift()
        self.window.focus_force()

    def _check(
        self,
        parent: tk.Misc,
        title: str,
        description: str,
        variable: tk.BooleanVar,
    ) -> None:
        row = tk.Frame(parent, bg=PANEL, pady=5)
        row.pack(fill="x")
        check = tk.Checkbutton(
            row,
            text=title,
            variable=variable,
            bg=PANEL,
            fg=PAPER,
            activebackground=PANEL,
            activeforeground=PAPER,
            selectcolor=PANEL_RAISED,
            anchor="w",
            cursor="hand2",
            font=(FONT_BODY, 10, "bold"),
        )
        check.pack(anchor="w")
        tk.Label(
            row,
            text=description,
            bg=PANEL,
            fg=MUTED,
            anchor="w",
            font=(FONT_BODY, 8),
        ).pack(anchor="w", padx=(24, 0), pady=(1, 0))

    def _section_label(self, parent: tk.Misc, text: str) -> tk.Label:
        return tk.Label(parent, text=text, bg=PANEL, fg=MUTED, font=(FONT_MONO, 8, "bold"))

    def _button(
        self,
        parent: tk.Misc,
        text: str,
        command: Callable,
        primary: bool = False,
    ) -> tk.Button:
        bg = BLUE if primary else PANEL_RAISED
        fg = INK if primary else PAPER
        return tk.Button(
            parent,
            text=text,
            command=command,
            bg=bg,
            fg=fg,
            activebackground=BLUE_ACTIVE if primary else LINE,
            activeforeground=INK if primary else PAPER,
            relief="flat",
            bd=0,
            padx=13,
            pady=7,
            cursor="hand2",
            font=(FONT_BODY, 9, "bold"),
        )

    def _choose_folder(self) -> None:
        initial = self.folder_var.get()
        chosen = filedialog.askdirectory(
            title="Choose the CursorPocket capture folder",
            initialdir=initial if Path(initial).exists() else str(Path.home()),
            parent=self.window,
            mustexist=False,
        )
        if chosen:
            self.folder_var.set(chosen)

    def _save(self) -> None:
        updated = AppSettings(
            capture_dir=self.folder_var.get(),
            follow_cursor=self.follow_var.get(),
            onboarding_seen=self.settings.onboarding_seen,
            panel_geometry=self.settings.panel_geometry,
        )
        self.on_save(updated, self.startup_var.get())
        self._destroy()

    def close(self) -> None:
        self._destroy()

    def _destroy(self) -> None:
        if self.closed:
            return
        self.closed = True
        self.window.destroy()
        self.on_close()


class CursorPocketApp:
    COMPANION_SIZE = 18
    COMPANION_DOT_SIZE = 8
    COMPANION_OFFSET = (8, 5)
    PANEL_WIDTH = 560
    PANEL_HEIGHT = 820

    def __init__(self) -> None:
        self.root = tk.Tk()
        self.root.withdraw()
        self.root.title("CursorPocket")
        self.root.report_callback_exception = self._report_tk_error
        self.settings_store = SettingsStore()
        self.settings = self.settings_store.load()
        self.store = CaptureStore(self.settings.capture_dir)
        self.audio_recorder = AudioRecorder()
        self.startup_manager = StartupManager()
        self.events: queue.SimpleQueue[object] = queue.SimpleQueue()
        self.registered_shortcuts = {
            hotkey.action: hotkey.label for hotkey in DEFAULT_HOTKEYS
        }
        self.hotkeys = GlobalHotkeyManager(
            self.events.put,
            lambda message: self.events.put(("error", message)),
            lambda action, label, fallback: self.events.put(
                ("hotkey_status", action, label, fallback)
            ),
        )
        self.panel_open = False
        self.capture_active = False
        self.recording = False
        self.hidden_mode = not self.settings.follow_cursor
        self.closing = False
        self.toasts: list[tk.Toplevel] = []
        self.settings_window: SettingsWindow | None = None
        self.recording_started_at = 0.0
        self.last_region_bounds: tuple[int, int, int, int] | None = None
        self.last_foreground_bounds: tuple[int, int, int, int] | None = None
        self.last_foreground_handle: int | None = None
        self.source_selected_text = ""
        self.source_page_url = ""
        self.display_bounds = monitor_bounds()
        self.tray = TrayManager(self.events.put)
        initial_cursor = cursor_position()
        self._companion_x, self._companion_y = initial_cursor
        self._last_cursor = initial_cursor
        self._last_cursor_move = time.monotonic()
        self._companion_pinned = False
        self._companion_hover = False
        self._build_icon()
        self._build_companion()
        self._build_panel()
        self.recording_indicator = RecordingIndicator(self.root, self.stop_audio_recording)
        if self.hidden_mode:
            self.companion.withdraw()
        self.hotkeys.start()
        self.tray.start()
        self.tray.set_state(recording=False, hidden=self.hidden_mode)
        self._refresh_history()
        self.root.after(16, self._follow_tick)
        self.root.after(50, self._poll_events)
        if not self.settings.onboarding_seen:
            self.root.after(850, self._show_first_run)

    def run(self) -> None:
        self.root.mainloop()

    def _build_icon(self) -> None:
        icon = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
        draw = ImageDraw.Draw(icon)
        draw.rounded_rectangle((5, 5, 59, 59), radius=16, fill=PANEL)
        draw.ellipse((13, 13, 51, 51), fill="#0B1119", outline="#2D3A4B", width=3)
        draw.ellipse((22, 22, 42, 42), fill=GREEN)
        self.icon_photo = ImageTk.PhotoImage(icon)
        self.root.iconphoto(True, self.icon_photo)

    def _build_companion(self) -> None:
        self.companion = tk.Toplevel(self.root)
        self.companion.overrideredirect(True)
        self.companion.attributes("-topmost", True)
        try:
            self.companion.attributes("-transparentcolor", TRANSPARENT)
        except tk.TclError:
            pass
        self.companion.configure(bg=TRANSPARENT)
        self.companion_canvas = tk.Canvas(
            self.companion,
            width=self.COMPANION_SIZE,
            height=self.COMPANION_SIZE,
            bg=TRANSPARENT,
            highlightthickness=0,
            cursor="hand2",
        )
        self.companion_canvas.pack()
        make_window_no_activate(self.companion)
        self._draw_companion(False)
        self.companion_canvas.bind("<Enter>", self._companion_enter)
        self.companion_canvas.bind("<Leave>", self._companion_leave)
        self.companion_canvas.bind("<Button-1>", lambda _event: self._companion_primary_action())
        self.companion_canvas.bind("<Button-3>", lambda _event: self.toggle_panel())

    def _draw_companion(self, hover: bool) -> None:
        canvas = self.companion_canvas
        canvas.delete("all")
        dot_color = RED if self.recording else GREEN
        inset = (self.COMPANION_SIZE - self.COMPANION_DOT_SIZE) // 2
        edge = inset + self.COMPANION_DOT_SIZE
        canvas.create_oval(inset, inset, edge, edge, fill=dot_color, outline="")

    @classmethod
    def _companion_target(cls, cursor_x: int, cursor_y: int) -> tuple[int, int]:
        offset_x, offset_y = cls.COMPANION_OFFSET
        return cursor_x + offset_x, cursor_y + offset_y

    def _companion_primary_action(self) -> None:
        if self.recording:
            self.stop_audio_recording()
        else:
            self.toggle_panel()

    def _build_panel(self) -> None:
        self.panel = tk.Toplevel(self.root)
        self.panel.withdraw()
        self.panel.title("CursorPocket — Capture")
        self.panel.attributes("-topmost", True)
        self.panel.resizable(True, True)
        self.panel.minsize(520, 640)
        self.panel.protocol("WM_DELETE_WINDOW", self.hide_panel)
        self.panel.configure(bg=LINE)
        self.panel.bind("<Escape>", lambda _event: self.hide_panel())
        self.panel.bind("<MouseWheel>", self._scroll_panel, add="+")
        self.panel.bind_all("<KeyPress>", self._handle_panel_key, add="+")
        self.panel_positioned = False
        if self.settings.panel_geometry:
            try:
                self.panel.geometry(self.settings.panel_geometry)
                self.panel_positioned = True
            except tk.TclError:
                pass

        shell = tk.Frame(self.panel, bg=PANEL)
        shell.pack(fill="both", expand=True, padx=1, pady=1)
        self.panel_canvas, content, self.panel_scrollbar = build_scrollable_panel(shell)

        header = tk.Frame(content, bg=PANEL)
        header.pack(fill="x")
        title_block = tk.Frame(header, bg=PANEL)
        title_block.pack(side="left")
        tk.Label(
            title_block,
            text="CURSORPOCKET",
            bg=PANEL,
            fg=BLUE,
            font=(FONT_MONO, 9, "bold"),
        ).pack(anchor="w")
        tk.Label(
            title_block,
            text="Capture",
            bg=PANEL,
            fg=PAPER,
            font=(FONT_DISPLAY, 20, "bold"),
        ).pack(anchor="w", pady=(3, 0))
        self._header_button(header, "Settings", self.open_settings).pack(
            side="right", anchor="n"
        )

        tk.Label(
            content,
            text="With this window open, press QWER, ASDF, or 1234 without Ctrl or Alt. The green dot and tray icon bring it back.",
            bg=PANEL,
            fg=MUTED,
            justify="left",
            wraplength=350,
            font=(FONT_BODY, 10),
        ).pack(anchor="w", pady=(8, 12))

        self.screenshot_buttons = self._keyboard_group(
            content,
            "SCREENSHOTS  ·  QWER",
            (
                ("Q", "Region", self.capture_screenshot, True),
                ("W", "Window", self.capture_active_window, True),
                ("E", "All screens", self.capture_all_screens, True),
                ("R", "Repeat", self.repeat_last_region, True),
            ),
        )
        self.audio_buttons = self._keyboard_group(
            content,
            "AUDIO  ·  ASDF",
            (
                ("A", "Record", self.toggle_audio_recording, True),
                ("S", "Save", self.stop_audio_recording, False),
                ("D", "Discard", self.discard_audio_recording, False),
                ("F", "Folder", self.open_audio_folder, True),
            ),
        )
        display_actions = []
        for index in range(4):
            available = index < len(self.display_bounds)
            display_actions.append(
                (
                    str(index + 1),
                    f"Display {index + 1}" if available else "Unavailable",
                    lambda target=index: self.capture_monitor(target),
                    available,
                )
            )
        self.display_buttons = self._keyboard_group(
            content,
            "FULL DISPLAY  ·  1234",
            tuple(display_actions),
        )

        self._section_label(content, "CURRENT CONTEXT  ·  T / L").pack(anchor="w", pady=(13, 3))
        (
            _text_glyph,
            _text_title,
            _text_description,
            self.text_shortcut,
        ) = self._action_row(
            content,
            "T",
            "Text snippet",
            "Save text highlighted in the previous window",
            "T",
            self.capture_text,
        )
        (
            _link_glyph,
            _link_title,
            _link_description,
            self.link_shortcut,
        ) = self._action_row(
            content,
            "L",
            "Web link",
            "Save the page open in your browser",
            "L",
            self.capture_link,
        )
        self.shortcut_widgets: dict[str, tk.Label] = {}

        history_header = tk.Frame(content, bg=PANEL)
        history_header.pack(fill="x", pady=(15, 6))
        tk.Label(
            history_header,
            text="RECENT CAPTURES",
            bg=PANEL,
            fg=MUTED,
            font=(FONT_MONO, 9, "bold"),
        ).pack(side="left")
        self.folder_hint = tk.Label(
            history_header,
            text="",
            bg=PANEL,
            fg=MUTED,
            font=(FONT_BODY, 9),
        )
        self.folder_hint.pack(side="right")
        self.history_frame = tk.Frame(content, bg=PANEL)
        self.history_frame.pack(fill="both", expand=True)

        footer = tk.Frame(content, bg=PANEL)
        footer.pack(fill="x", pady=(11, 0))
        self._footer_button(footer, "Open captures", self.open_folder).pack(side="left")
        self._footer_button(footer, "Hide dot", self.toggle_hidden_mode).pack(side="left", padx=(7, 0))
        self._footer_button(footer, "Quit", self.quit).pack(side="right")

        self.status = tk.Label(
            content,
            text="",
            bg=PANEL,
            fg=ORANGE,
            font=(FONT_BODY, 9),
            anchor="w",
        )
        self.status.pack(fill="x", pady=(8, 0))

        self.panel_shortcut_hint = tk.Label(
            content,
            text=f"Open this menu anywhere: {self.registered_shortcuts['panel']}",
            bg=PANEL,
            fg=MUTED,
            font=(FONT_MONO, 8),
            anchor="w",
        )
        self.panel_shortcut_hint.pack(fill="x", pady=(6, 0))

    def _scroll_panel(self, event: tk.Event) -> str | None:
        if not self.panel_open:
            return None
        units = panel_scroll_units(int(getattr(event, "delta", 0)))
        if units == 0:
            return None
        self.panel_canvas.yview_scroll(units, "units")
        return "break"

    def _action_row(
        self,
        parent: tk.Misc,
        glyph: str,
        title: str,
        description: str,
        shortcut: str,
        command: Callable,
        accent: str = BLUE,
        priority: bool = False,
    ) -> tuple[tk.Label, tk.Label, tk.Label, tk.Label]:
        row_bg = "#263445" if priority else PANEL_RAISED
        row = tk.Frame(parent, bg=row_bg, padx=13, pady=11 if priority else 9, cursor="hand2")
        row.pack(fill="x", pady=3)
        glyph_label = tk.Label(
            row,
            text=glyph,
            width=2,
            bg=row_bg,
            fg=accent,
            font=(FONT_MONO, 13, "bold"),
        )
        glyph_label.pack(side="left", padx=(0, 8))
        copy = tk.Frame(row, bg=row_bg)
        copy.pack(side="left", fill="x", expand=True)
        title_label = tk.Label(copy, text=title, bg=row_bg, fg=PAPER, font=(FONT_BODY, 12 if priority else 11, "bold"))
        title_label.pack(anchor="w")
        description_label = tk.Label(copy, text=description, bg=row_bg, fg=MUTED, font=(FONT_BODY, 9))
        description_label.pack(anchor="w", pady=(2, 0))
        shortcut_label = tk.Label(
            row,
            text=shortcut,
            bg=row_bg,
            fg=MUTED,
            font=(FONT_MONO, 8),
        )
        shortcut_label.pack(side="right", padx=(8, 0))

        def enter(_event: tk.Event) -> None:
            self._paint_tree(row, "#2C3C4F")

        def leave(_event: tk.Event) -> None:
            self._paint_tree(row, row_bg)

        _bind_tree(row, "<Button-1>", lambda _event: command())
        _bind_tree(row, "<Enter>", enter)
        _bind_tree(row, "<Leave>", leave)
        return glyph_label, title_label, description_label, shortcut_label

    def _section_label(self, parent: tk.Misc, text: str) -> tk.Label:
        return tk.Label(parent, text=text, bg=PANEL, fg=MUTED, font=(FONT_MONO, 9, "bold"))

    def _keyboard_group(
        self,
        parent: tk.Misc,
        title: str,
        actions: tuple[tuple[str, str, Callable, bool], ...],
    ) -> dict[str, tk.Button]:
        self._section_label(parent, title).pack(anchor="w", pady=(12, 5))
        row = tk.Frame(parent, bg=PANEL)
        row.pack(fill="x")
        buttons: dict[str, tk.Button] = {}
        for index, (key, label, command, enabled) in enumerate(actions):
            button = tk.Button(
                row,
                text=f"{key}\n{label}",
                command=command,
                state="normal" if enabled else "disabled",
                bg=PANEL_RAISED,
                fg=PAPER,
                disabledforeground="#667386",
                activebackground="#2C3C4F",
                activeforeground=PAPER,
                relief="flat",
                bd=0,
                padx=7,
                pady=7,
                cursor="hand2" if enabled else "arrow",
                font=(FONT_BODY, 9, "bold"),
            )
            button.grid(row=0, column=index, sticky="ew", padx=(0 if index == 0 else 3, 0))
            row.grid_columnconfigure(index, weight=1, uniform=title)
            buttons[key] = button
        return buttons

    def _handle_panel_key(self, event: tk.Event) -> str | None:
        if not self.panel_open:
            return None
        if int(getattr(event, "state", 0)) & 0x2000C:
            return None
        action = panel_key_action(str(getattr(event, "keysym", "")))
        if action is None:
            return None
        commands: dict[str, Callable[[], None]] = {
            "region_screenshot": self.capture_screenshot,
            "window_screenshot": self.capture_active_window,
            "all_screens": self.capture_all_screens,
            "repeat_region": self.repeat_last_region,
            "toggle_audio": self.toggle_audio_recording,
            "save_audio": self.stop_audio_recording,
            "discard_audio": self.discard_audio_recording,
            "audio_folder": self.open_audio_folder,
            "text": self.capture_text,
            "link": self.capture_link,
        }
        if action.startswith("monitor_"):
            self.capture_monitor(int(action.removeprefix("monitor_")) - 1)
        else:
            commands[action]()
        return "break"

    def _header_button(self, parent: tk.Misc, text: str, command: Callable) -> tk.Button:
        return tk.Button(
            parent,
            text=text,
            command=command,
            bg=PANEL_RAISED,
            fg=PAPER,
            activebackground=LINE,
            activeforeground=PAPER,
            relief="flat",
            bd=0,
            padx=10,
            pady=5,
            cursor="hand2",
            font=(FONT_BODY, 9, "bold"),
        )

    def _small_button(self, parent: tk.Misc, text: str, command: Callable) -> tk.Button:
        return tk.Button(
            parent,
            text=text,
            command=command,
            bg=PANEL_RAISED,
            fg=MUTED,
            activebackground=LINE,
            activeforeground=PAPER,
            relief="flat",
            bd=0,
            width=3,
            cursor="hand2",
            font=(FONT_BODY, 11, "bold"),
        )

    def _footer_button(self, parent: tk.Misc, text: str, command: Callable) -> tk.Button:
        return tk.Button(
            parent,
            text=text,
            command=command,
            bg=PANEL_RAISED,
            fg=PAPER,
            activebackground=LINE,
            activeforeground=PAPER,
            relief="flat",
            bd=0,
            padx=10,
            pady=6,
            cursor="hand2",
            font=(FONT_BODY, 9, "bold"),
        )

    def _paint_tree(self, widget: tk.Misc, color: str) -> None:
        try:
            widget.configure(bg=color)
        except tk.TclError:
            pass
        for child in widget.winfo_children():
            self._paint_tree(child, color)

    def _follow_tick(self) -> None:
        if self.closing:
            return
        if not self.hidden_mode and not self.capture_active and not self.panel_open and self.settings.follow_cursor:
            cursor_x, cursor_y = cursor_position()
            now = time.monotonic()
            if (cursor_x, cursor_y) != self._last_cursor:
                self._last_cursor = (cursor_x, cursor_y)
                self._last_cursor_move = now
            if not self._companion_pinned and now - self._last_cursor_move >= 0.48:
                self._companion_pinned = True
            center_x = self._companion_x + self.COMPANION_SIZE / 2
            center_y = self._companion_y + self.COMPANION_SIZE / 2
            distance = ((cursor_x - center_x) ** 2 + (cursor_y - center_y) ** 2) ** 0.5
            if self._companion_pinned and not self._companion_hover and distance > 132:
                self._companion_pinned = False
            if not self._companion_pinned:
                self._companion_x, self._companion_y = self._companion_target(cursor_x, cursor_y)
            position_window(
                self.companion,
                round(self._companion_x),
                round(self._companion_y),
                self.COMPANION_SIZE,
                self.COMPANION_SIZE,
            )
        self.root.after(16, self._follow_tick)

    def _companion_enter(self, _event: tk.Event) -> None:
        self._companion_hover = True
        self._companion_pinned = True
        self._draw_companion(True)

    def _companion_leave(self, _event: tk.Event) -> None:
        self._companion_hover = False
        self._draw_companion(False)

    def toggle_panel(self) -> None:
        if self.panel_open:
            self.hide_panel()
        else:
            self.show_panel()

    def _show_first_run(self) -> None:
        if self.closing:
            return
        self.settings.onboarding_seen = True
        self.settings_store.save(self.settings)
        self.show_panel()
        self.status.configure(
            text="Ready. Click the green dot or use the tray icon to return here.",
            fg=BLUE,
        )

    def show_panel(self) -> None:
        if self.capture_active:
            return
        foreground_handle = foreground_window_handle()
        if foreground_handle:
            self.last_foreground_handle = foreground_handle
            self._snapshot_source_context(foreground_handle)
        else:
            self.source_selected_text = ""
            self.source_page_url = ""
        foreground = foreground_window_bounds()
        if foreground:
            self.last_foreground_bounds = foreground
        if self.hidden_mode:
            self.hidden_mode = False
            self.companion.deiconify()
            self.tray.set_state(recording=self.recording, hidden=False)
        self.panel_open = True
        self._draw_companion(True)
        self._refresh_history()
        self.panel.deiconify()
        if not self.panel_positioned:
            cursor_x, cursor_y = cursor_position()
            vx, vy, vw, vh = virtual_screen_bounds()
            if cursor_x + 42 + self.PANEL_WIDTH <= vx + vw - 12:
                panel_x = cursor_x + 42
            else:
                panel_x = cursor_x - self.PANEL_WIDTH - 24
            panel_y = min(
                max(vy + 12, cursor_y - 72),
                vy + vh - self.PANEL_HEIGHT - 12,
            )
            position_window(
                self.panel,
                panel_x,
                panel_y,
                self.PANEL_WIDTH,
                self.PANEL_HEIGHT,
                activate=True,
            )
            self.panel_positioned = True
        self.panel.lift()
        self.panel.focus_force()

    def hide_panel(self) -> None:
        if self.panel_open:
            try:
                geometry = self.panel.geometry()
                if geometry and geometry != self.settings.panel_geometry:
                    self.settings.panel_geometry = geometry
                    self.settings_store.save(self.settings)
            except (OSError, tk.TclError):
                pass
        self.panel_open = False
        self._companion_pinned = False
        self._last_cursor_move = time.monotonic()
        self._draw_companion(False)
        self.panel.withdraw()

    def capture_screenshot(self) -> None:
        if self.capture_active:
            return
        self.capture_active = True
        self.hide_panel()
        self.companion.withdraw()
        if self.recording:
            self.recording_indicator.hide()
        self.root.after(180, self._begin_screen_selection)

    def capture_active_window(self) -> None:
        bounds = self.last_foreground_bounds or foreground_window_bounds()
        if not bounds:
            self.show_toast(
                "No window to capture",
                "Return to the window, reopen CursorPocket, and press W.",
                error=True,
            )
            return
        self._capture_fixed_bounds(bounds)

    def capture_all_screens(self) -> None:
        vx, vy, vw, vh = virtual_screen_bounds()
        self._capture_fixed_bounds((vx, vy, vx + vw, vy + vh))

    def repeat_last_region(self) -> None:
        if not self.last_region_bounds:
            self.show_toast(
                "No region to repeat yet",
                "Capture a region with Q first.",
                error=True,
            )
            return
        self._capture_fixed_bounds(self.last_region_bounds)

    def capture_monitor(self, index: int) -> None:
        if index < 0 or index >= len(self.display_bounds):
            self.show_toast(
                f"Display {index + 1} isn’t available",
                f"Windows currently reports {len(self.display_bounds)} display(s).",
                error=True,
            )
            return
        self._capture_fixed_bounds(self.display_bounds[index])

    def _capture_fixed_bounds(self, bounds: tuple[int, int, int, int]) -> None:
        if self.capture_active:
            return
        self.capture_active = True
        self.hide_panel()
        self.companion.withdraw()
        if self.recording:
            self.recording_indicator.hide()
        self.root.after(180, lambda: self._grab_fixed_bounds(bounds))

    def _grab_fixed_bounds(self, bounds: tuple[int, int, int, int]) -> None:
        try:
            image = ImageGrab.grab(bbox=bounds, all_screens=True)
        except Exception as error:
            self.capture_active = False
            self._restore_companion()
            if self.recording:
                self.recording_indicator.show(self.recording_started_at)
            self.show_toast("Couldn’t capture the screen", str(error), error=True)
            return
        if self.recording:
            self.recording_indicator.show(self.recording_started_at)
        self._annotate_screenshot(image, bounds)

    def _begin_screen_selection(self) -> None:
        try:
            screenshot = ImageGrab.grab(all_screens=True)
        except Exception as error:
            self.capture_active = False
            self._restore_companion()
            if self.recording:
                self.recording_indicator.show(self.recording_started_at)
            self.show_toast("Couldn’t capture the screen", str(error), error=True)
            return
        if self.recording:
            self.recording_indicator.show(self.recording_started_at)
        vx, vy, _vw, _vh = virtual_screen_bounds()
        RegionSelector(
            self.root,
            screenshot,
            (vx, vy),
            self._save_selected_screenshot,
            self._cancel_screenshot,
        )

    def _save_selected_screenshot(
        self,
        image: Image.Image,
        bounds: tuple[int, int, int, int],
    ) -> None:
        self.last_region_bounds = bounds
        self._annotate_screenshot(image, bounds)

    def _annotate_screenshot(
        self,
        image: Image.Image,
        bounds: tuple[int, int, int, int],
    ) -> None:
        ScreenshotAnnotator(
            self.root,
            image,
            bounds,
            self._save_screenshot,
            self._cancel_screenshot,
        )

    def _save_screenshot(self, image: Image.Image, bounds: tuple[int, int, int, int]) -> None:
        try:
            record = self.store.save_image(image, bounds)
            self._capture_finished(record, "Screenshot saved")
        except Exception as error:
            self.capture_active = False
            self._restore_companion()
            self.show_toast("Screenshot wasn’t saved", str(error), error=True)

    def _cancel_screenshot(self) -> None:
        self.capture_active = False
        self._restore_companion()
        self.show_toast("Capture cancelled", "Nothing was saved")

    def capture_text(self) -> None:
        if self.capture_active:
            return
        cached_selection = self.source_selected_text if self.panel_open else ""
        source_window = self._capture_source_window()
        self.capture_active = True
        self.hide_panel()
        selected = cached_selection
        if not selected and source_window and copy_selected_text(source_window):
            selected = self._clipboard_text()
        if not selected.strip():
            self._nothing_captured(
                "No highlighted text",
                "Highlight text in another app, open CursorPocket, then press T.",
            )
            return
        self._save_text(selected)

    def _save_text(self, value: str) -> None:
        try:
            record = self.store.save_text(value)
            self._capture_finished(record, "Text saved")
        except Exception as error:
            self.capture_active = False
            self.show_toast("Text wasn’t saved", str(error), error=True)

    def capture_link(self) -> None:
        if self.capture_active:
            return
        cached_url = self.source_page_url if self.panel_open else ""
        source_window = self._capture_source_window()
        self.capture_active = True
        self.hide_panel()
        current_url = cached_url
        if not current_url and source_window and copy_browser_url(source_window):
            current_url = self._clipboard_text().strip()
        if not is_web_url(current_url):
            self._nothing_captured(
                "No webpage detected",
                "Open a page in your browser, open CursorPocket, then press L.",
            )
            return
        self._save_link(current_url)

    def _snapshot_source_context(self, source_window: int) -> None:
        self.source_selected_text = ""
        self.source_page_url = ""
        if copy_selected_text(source_window):
            selected = self._clipboard_text()
            if selected.strip():
                self.source_selected_text = selected
        if copy_browser_url(source_window):
            url = self._clipboard_text().strip()
            if is_web_url(url):
                self.source_page_url = url

    def _capture_source_window(self) -> int | None:
        current = foreground_window_handle()
        if current:
            self.last_foreground_handle = current
        return current or self.last_foreground_handle

    def _nothing_captured(self, title: str, detail: str) -> None:
        self.capture_active = False
        self._restore_companion()
        self.show_toast(title, detail, error=True)

    def _save_link(self, value: str) -> None:
        try:
            record = self.store.save_link(value)
            self._capture_finished(record, "Link saved")
        except Exception as error:
            self.capture_active = False
            self.show_toast("Link wasn’t saved", str(error), error=True)

    def _editor_cancelled(self) -> None:
        self.capture_active = False
        self.show_toast("Capture cancelled", "Nothing was saved")

    def _capture_finished(self, record: CaptureRecord, title: str) -> None:
        self.capture_active = False
        self._companion_pinned = False
        self._restore_companion()
        self._refresh_history()
        self.show_toast(title, f"{record.preview}  ·  Click to open", action=lambda: self.open_capture(record))

    def _restore_companion(self) -> None:
        if not self.hidden_mode:
            self.companion.deiconify()

    def toggle_audio_recording(self) -> None:
        if self.recording:
            self.stop_audio_recording()
        else:
            self.start_audio_recording()

    def start_audio_recording(self) -> None:
        if self.capture_active:
            self.show_toast("Finish the current capture first", "Audio recording did not start", error=True)
            return
        self.hide_panel()
        try:
            self.audio_recorder.start()
        except Exception as error:
            self.show_toast("Microphone didn’t start", str(error), error=True)
            return
        self.recording = True
        self.recording_started_at = time.monotonic()
        self._companion_pinned = True
        self._update_audio_ui()
        self.recording_indicator.show(self.recording_started_at)
        self.tray.set_state(recording=True, hidden=self.hidden_mode)
        shortcut = self.registered_shortcuts.get("audio", "Ctrl + Shift + 2")
        self.show_toast("Recording audio", f"Use the red Stop bar, click the red dot, or press {shortcut}")

    def stop_audio_recording(self) -> None:
        if not self.recording:
            return
        source_path: Path | None = None
        try:
            result = self.audio_recorder.stop()
            source_path = result.path
            record = self.store.save_audio_file(
                result.path,
                result.duration_seconds,
                sample_rate=result.sample_rate,
                channels=result.channels,
            )
            self._refresh_history()
            self.show_toast(
                "Audio saved",
                f"{record.preview}  ·  Click to play",
                action=lambda: self.open_capture(record),
            )
        except Exception as error:
            self.show_toast("Audio wasn’t saved", str(error), error=True)
        finally:
            if source_path:
                try:
                    source_path.unlink(missing_ok=True)
                except OSError:
                    pass
            self.recording = False
            self._companion_pinned = False
            self.recording_indicator.hide()
            self.tray.set_state(recording=False, hidden=self.hidden_mode)
            self._update_audio_ui()

    def discard_audio_recording(self) -> None:
        if not self.recording:
            self.show_toast("Nothing is recording", "Press A to start a voice note.")
            return
        if not messagebox.askyesno(
            "Discard recording?",
            "Discard the current audio without saving it?",
            icon="warning",
            parent=self.recording_indicator.window,
        ):
            return
        self.audio_recorder.cancel()
        self.recording = False
        self._companion_pinned = False
        self.recording_indicator.hide()
        self.tray.set_state(recording=False, hidden=self.hidden_mode)
        self._update_audio_ui()
        self.show_toast("Recording discarded", "No audio file was saved")

    def open_audio_folder(self) -> None:
        self.open_folder()

    def _update_audio_ui(self) -> None:
        self._draw_companion(self._companion_hover)
        if hasattr(self, "audio_buttons"):
            self.audio_buttons["A"].configure(
                text="A\nStop" if self.recording else "A\nRecord"
            )
            self.audio_buttons["S"].configure(
                state="normal" if self.recording else "disabled"
            )
            self.audio_buttons["D"].configure(
                state="normal" if self.recording else "disabled"
            )
        if self.recording:
            self.status.configure(text="Recording locally", fg=RED)
        else:
            if "Recording locally" in self.status.cget("text"):
                self.status.configure(text="", fg=ORANGE)

    def toggle_hidden_mode(self) -> None:
        if self.hidden_mode:
            self.hidden_mode = False
            self._companion_pinned = False
            self._last_cursor_move = time.monotonic()
            self.companion.deiconify()
            self.tray.set_state(recording=self.recording, hidden=False)
            shortcut = self.registered_shortcuts.get("hidden", "Ctrl + Shift + H")
            self.show_toast("Dot visible", f"{shortcut} toggles the dot")
            return
        self.hide_panel()
        self.hidden_mode = True
        self.companion.withdraw()
        self.tray.set_state(recording=self.recording, hidden=True)
        shortcut = self.registered_shortcuts.get("hidden", "Ctrl + Shift + H")
        self.show_toast("Dot hidden", f"Use the tray icon or press {shortcut} to bring it back")

    def _clipboard_text(self) -> str:
        try:
            value = self.root.clipboard_get()
            return value if isinstance(value, str) else ""
        except tk.TclError:
            return ""

    def _refresh_history(self) -> None:
        if not hasattr(self, "history_frame"):
            return
        for child in self.history_frame.winfo_children():
            child.destroy()
        records = self.store.recent(3)
        self.folder_hint.configure(text=Path(self.settings.capture_dir).name)
        if not records:
            empty = tk.Frame(self.history_frame, bg=INK, padx=12, pady=13)
            empty.pack(fill="x")
            tk.Label(
                empty,
                text="Your first capture will appear here.",
                bg=INK,
                fg=MUTED,
                font=(FONT_BODY, 9),
            ).pack(anchor="w")
            return
        glyphs = {"screenshot": "▣", "text": "¶", "link": "↗", "audio": "●"}
        for record in records:
            row = tk.Frame(self.history_frame, bg=INK, padx=10, pady=7, cursor="hand2")
            row.pack(fill="x", pady=2)
            tk.Label(
                row,
                text=glyphs.get(record.kind, "•"),
                bg=INK,
                fg=GREEN if record.kind == "audio" else BLUE,
                width=2,
                font=("Segoe UI Symbol", 10, "bold"),
            ).pack(side="left")
            tk.Label(
                row,
                text=record.preview,
                bg=INK,
                fg=PAPER,
                anchor="w",
                font=(FONT_BODY, 10),
            ).pack(side="left", fill="x", expand=True, padx=(5, 8))
            try:
                stamp = datetime.fromisoformat(record.created_at).strftime("%H:%M")
            except ValueError:
                stamp = ""
            tk.Label(row, text=stamp, bg=INK, fg=MUTED, font=(FONT_MONO, 8)).pack(side="right")
            _bind_tree(row, "<Button-1>", lambda _event, item=record: self.open_capture(item))

    def open_capture(self, record: CaptureRecord) -> None:
        path = self.store.absolute_path(record)
        try:
            os.startfile(path)  # type: ignore[attr-defined]
        except OSError as error:
            self.show_toast("Couldn’t open capture", str(error), error=True)

    def open_folder(self) -> None:
        try:
            self.store.base_dir.mkdir(parents=True, exist_ok=True)
            os.startfile(self.store.base_dir)  # type: ignore[attr-defined]
        except OSError as error:
            self.show_toast("Couldn’t open folder", str(error), error=True)

    def open_settings(self) -> None:
        if self.settings_window is not None:
            self.settings_window.window.lift()
            self.settings_window.window.focus_force()
            return
        self.hide_panel()
        self.settings_window = SettingsWindow(
            self.root,
            self.settings,
            self.startup_manager.is_enabled(),
            self.registered_shortcuts,
            self._save_settings,
            self._settings_closed,
        )

    def _save_settings(self, updated: AppSettings, startup_enabled: bool) -> None:
        try:
            Path(updated.capture_dir).mkdir(parents=True, exist_ok=True)
            self.startup_manager.set_enabled(startup_enabled)
            self.settings = updated
            self.settings_store.save(self.settings)
            self.store.set_base_dir(updated.capture_dir)
            if updated.follow_cursor:
                self.hidden_mode = False
                self._companion_pinned = False
                self.companion.deiconify()
            else:
                self.hidden_mode = True
                self.companion.withdraw()
            self.tray.set_state(recording=self.recording, hidden=self.hidden_mode)
            self._refresh_history()
            startup_text = "starts with Windows" if startup_enabled else "opens when you launch it"
            self.show_toast("Settings saved", f"CursorPocket {startup_text}")
        except OSError as error:
            self.show_toast("Settings weren’t saved", str(error), error=True)

    def _settings_closed(self) -> None:
        self.settings_window = None

    def show_toast(
        self,
        title: str,
        detail: str,
        error: bool = False,
        action: Callable[[], None] | None = None,
    ) -> None:
        toast = tk.Toplevel(self.root)
        toast.overrideredirect(True)
        toast.attributes("-topmost", True)
        toast.configure(bg=ORANGE if error else BLUE)
        inner = tk.Frame(toast, bg=PANEL, padx=14, pady=10)
        inner.pack(fill="both", expand=True, padx=1, pady=1)
        tk.Label(
            inner,
            text=title,
            bg=PANEL,
            fg=ORANGE if error else BLUE,
            font=(FONT_BODY, 9, "bold"),
            anchor="w",
        ).pack(fill="x")
        tk.Label(
            inner,
            text=detail,
            bg=PANEL,
            fg=PAPER,
            font=(FONT_BODY, 9),
            anchor="w",
            wraplength=300,
        ).pack(fill="x", pady=(2, 0))
        if action is not None:
            toast.configure(cursor="hand2")
            bind_toplevel_click(toast, lambda _event: action())
        x, y = cursor_position()
        vx, vy, vw, vh = virtual_screen_bounds()
        width, height = 330, 68
        px = min(max(vx + 10, x + 44), vx + vw - width - 10)
        py = min(max(vy + 10, y + 32), vy + vh - height - 10)
        position_window(toast, px, py, width, height)
        toast.lift()
        toast.after(2300, lambda: self._fade_toast(toast, 0.92))
        self.toasts.append(toast)

    def _fade_toast(self, toast: tk.Toplevel, opacity: float) -> None:
        if not toast.winfo_exists():
            return
        if opacity <= 0.05:
            toast.destroy()
            if toast in self.toasts:
                self.toasts.remove(toast)
            return
        try:
            toast.attributes("-alpha", opacity)
        except tk.TclError:
            toast.destroy()
            return
        toast.after(28, lambda: self._fade_toast(toast, opacity - 0.10))

    def _poll_events(self) -> None:
        if self.closing:
            return
        while True:
            try:
                event = self.events.get_nowait()
            except queue.Empty:
                break
            if isinstance(event, tuple) and event and event[0] == "error":
                message = str(event[1])
                self.status.configure(text=message)
                self.show_toast("Shortcut unavailable", message, error=True)
                continue
            if isinstance(event, tuple) and event and event[0] == "hotkey_status":
                _kind, action, label, used_fallback = event
                self.registered_shortcuts[action] = label
                widget = getattr(self, "shortcut_widgets", {}).get(action)
                if widget is not None:
                    widget.configure(text=label)
                if action == "panel" and hasattr(self, "panel_shortcut_hint"):
                    self.panel_shortcut_hint.configure(
                        text=f"Open this menu anywhere: {label}"
                    )
                if used_fallback:
                    if action == "panel":
                        self.status.configure(
                            text=f"Open-menu shortcut changed to {label}",
                            fg=ORANGE,
                        )
                continue
            if event == "screenshot":
                self.capture_screenshot()
            elif event == "full_screenshot":
                self.capture_monitor(0)
            elif event == "text":
                self.capture_text()
            elif event == "link":
                self.capture_link()
            elif event == "panel":
                self.toggle_panel()
            elif event == "audio":
                self.toggle_audio_recording()
            elif event == "hidden":
                self.toggle_hidden_mode()
            elif event == "folder":
                self.open_folder()
            elif event == "settings":
                self.open_settings()
            elif event == "quit":
                self.quit()
        self.root.after(50, self._poll_events)

    def _report_tk_error(self, exception_type: type[BaseException], value: BaseException, traceback: object) -> None:
        del exception_type, traceback
        self.capture_active = False
        try:
            self._restore_companion()
            self.show_toast("CursorPocket hit an error", str(value), error=True)
        except tk.TclError:
            print(f"CursorPocket error: {value}", file=sys.stderr)

    def quit(self) -> None:
        if self.closing:
            return
        self.closing = True
        if self.recording:
            try:
                result = self.audio_recorder.stop()
                self.store.save_audio_file(
                    result.path,
                    result.duration_seconds,
                    sample_rate=result.sample_rate,
                    channels=result.channels,
                )
                result.path.unlink(missing_ok=True)
            except Exception:
                self.audio_recorder.cancel()
        self.recording_indicator.hide()
        self.tray.stop()
        self.hotkeys.stop()
        for toast in list(self.toasts):
            try:
                toast.destroy()
            except tk.TclError:
                pass
        self.root.destroy()


def hotkey_summary() -> list[tuple[str, str]]:
    return [(hotkey.label, hotkey.action) for hotkey in DEFAULT_HOTKEYS]
