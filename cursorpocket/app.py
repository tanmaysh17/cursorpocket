from __future__ import annotations

import math
import os
import queue
import shutil
import sys
import threading
import time
import tkinter as tk
from datetime import datetime
from pathlib import Path
from tkinter import filedialog, messagebox
from typing import Callable

from PIL import Image, ImageChops, ImageDraw, ImageEnhance, ImageFilter, ImageGrab, ImageTk

from .annotation import ScreenshotAnnotator
from .audio import AudioRecorder
from .branding import load_logo
from .gesture import DoubleCircleGestureDetector
from .hotkeys import DEFAULT_HOTKEYS, GlobalHotkeyManager
from .media_devices import MediaDevice, list_directshow_devices
from .settings import AppSettings, SettingsStore
from .startup import StartupManager
from .storage import CaptureRecord, CaptureStore, VideoReservation, is_web_url
from .tray import TrayManager
from .video import (
    FFmpegVideoRecorder,
    RecordingState,
    VideoCapabilities,
    VideoOptions,
    VideoProcessResult,
    VideoSourceKind,
    bundled_ffmpeg_path,
    inspect_video_file,
    probe_video_capabilities,
)
from .windows import (
    copy_browser_url,
    copy_selected_text,
    cursor_position,
    exclude_window_from_capture,
    foreground_window_handle,
    foreground_window_bounds,
    make_window_no_activate,
    monitor_bounds,
    position_window,
    virtual_screen_bounds,
    window_bounds,
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
COMMAND_MODE_TIMEOUT_MS = 30_000
COMPANION_PIN_SECONDS = 0.32
COMPANION_IDLE_HIDE_SECONDS = 1.4

FONT_BODY = "Bahnschrift"
FONT_DISPLAY = "Bahnschrift SemiBold"
FONT_MONO = "Bahnschrift SemiCondensed"

PANEL_SHORTCUT_HELP = (
    "Tap one key at a time while this window is open: V for a screen walkthrough; "
    "C to include the camera; Q, W, E, or R for screenshots; "
    "A, S, D, or F for audio; 1, 2, 3, or 4 for a display. These are individual "
    "keys—do not hold a row together."
)

# Windows/Tk can include Mod1 (0x8) on an otherwise plain injected key event.
# Block only the state bits observed for Ctrl and Alt so plain capture keys dispatch.
PANEL_BLOCKED_MODIFIER_MASK = 0x20004

COMMAND_SHORTCUT_ROWS = (
    ("VIDEO", "V Record display  ·  C Camera on/off"),
    ("SCREENSHOT", "Q Region  ·  W Window  ·  E All  ·  R Repeat"),
    ("AUDIO", "A Record  ·  S Save  ·  D Discard  ·  F Folder"),
    ("DISPLAY", "1  2  3  4   Capture numbered display"),
    ("CONTEXT", "T Selected text  ·  L Current webpage"),
)

PANEL_KEY_ACTIONS = {
    "v": "toggle_video",
    "c": "toggle_video_camera",
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


def monitor_for_point(
    monitors: list[tuple[int, int, int, int]],
    x: int,
    y: int,
) -> tuple[int, int, int, int]:
    for bounds in monitors:
        left, top, right, bottom = bounds
        if left <= x < right and top <= y < bottom:
            return bounds
    if not monitors:
        return (0, 0, 1920, 1080)
    return min(
        monitors,
        key=lambda bounds: (
            (x - (bounds[0] + bounds[2]) / 2.0) ** 2
            + (y - (bounds[1] + bounds[3]) / 2.0) ** 2
        ),
    )


def liquid_glass_image(
    backdrop: Image.Image | None,
    box: tuple[int, int, int, int],
    radius: int,
    *,
    tint_alpha: int = 178,
    feather: int = 0,
    boundary: bool = True,
    core_box: tuple[int, int, int, int] | None = None,
) -> Image.Image:
    """Render a frosted, tinted crop that visually preserves the desktop behind it."""
    left, top, right, bottom = box
    width = max(1, right - left)
    height = max(1, bottom - top)
    if backdrop is None:
        backdrop = Image.new("RGB", (max(right, width), max(bottom, height)), INK)
    source = backdrop.convert("RGB")
    original = source.crop((left, top, right, bottom))
    if original.size != (width, height):
        original = original.resize((width, height), Image.Resampling.BILINEAR)

    blur_padding = 48
    expanded_box = (
        max(0, left - blur_padding),
        max(0, top - blur_padding),
        min(source.width, right + blur_padding),
        min(source.height, bottom + blur_padding),
    )
    expanded = source.crop(expanded_box).filter(ImageFilter.GaussianBlur(radius=20))
    glass = expanded.crop(
        (
            left - expanded_box[0],
            top - expanded_box[1],
            right - expanded_box[0],
            bottom - expanded_box[1],
        )
    ).convert("RGBA")
    if glass.size != (width, height):
        glass = glass.resize((width, height), Image.Resampling.BILINEAR)

    tint = Image.new("RGBA", (width, height), (8, 17, 24, tint_alpha))
    glass = Image.alpha_composite(glass, tint)
    if boundary:
        sheen = Image.new("RGBA", (width, height), (0, 0, 0, 0))
        sheen_draw = ImageDraw.Draw(sheen)
        sheen_draw.rounded_rectangle(
            (1, 1, width - 2, height - 2),
            radius=max(1, radius - 1),
            outline=(172, 255, 234, 72),
            width=1,
        )
        sheen_draw.line(
            (radius, 2, max(radius, width - radius), 2),
            fill=(220, 255, 247, 58),
            width=1,
        )
        glass = Image.alpha_composite(glass, sheen)
    glass = glass.convert("RGB")

    mask = Image.new("L", (width, height), 0)
    if core_box is not None:
        core_left, core_top, core_right, core_bottom = core_box
        relative_core = (
            max(0, core_left - left),
            max(0, core_top - top),
            min(width - 1, core_right - left - 1),
            min(height - 1, core_bottom - top - 1),
        )
        ImageDraw.Draw(mask).rectangle(relative_core, fill=255)
        if feather:
            fade = mask.filter(ImageFilter.GaussianBlur(radius=feather))
            mask = ImageChops.lighter(mask, fade)
    else:
        mask_inset = min(
            max(0, feather * 2),
            max(0, (min(width, height) - 1) // 2),
        )
        ImageDraw.Draw(mask).rounded_rectangle(
            (mask_inset, mask_inset, width - 1 - mask_inset, height - 1 - mask_inset),
            radius=max(1, radius),
            fill=255,
        )
        if feather:
            mask = mask.filter(ImageFilter.GaussianBlur(radius=feather))
    result = original.copy()
    result.paste(glass, (0, 0), mask)
    return result


def ambient_edge_glow_images(
    backdrop: Image.Image | None,
    width: int,
    height: int,
    depth: int,
) -> list[tuple[tuple[int, int], Image.Image]]:
    """Build broad, soft screenshot-matched glows for each display edge."""
    if backdrop is None:
        backdrop = Image.new("RGB", (width, height), INK)
    source = backdrop.convert("RGB")
    strips: list[tuple[tuple[int, int], Image.Image]] = []
    definitions = (
        ("top", (0, 0, width, depth), (0, 0)),
        ("bottom", (0, height - depth, width, height), (0, height - depth)),
        ("left", (0, 0, depth, height), (0, 0)),
        ("right", (width - depth, 0, width, height), (width - depth, 0)),
    )
    for edge, box, position in definitions:
        original = source.crop(box)
        softened = original.filter(ImageFilter.GaussianBlur(radius=12))
        color = Image.new("RGB", original.size, (38, 194, 167))
        glowing = Image.blend(softened, color, 0.48)
        mask = Image.new("L", original.size, 0)
        mask_draw = ImageDraw.Draw(mask)
        axis_length = original.height if edge in {"top", "bottom"} else original.width
        for offset in range(axis_length):
            distance = offset if edge in {"top", "left"} else axis_length - 1 - offset
            strength = max(0.0, 1.0 - distance / max(1, axis_length - 1))
            alpha = round(118 * strength**2)
            if edge in {"top", "bottom"}:
                mask_draw.line((0, offset, original.width, offset), fill=alpha)
            else:
                mask_draw.line((offset, 0, offset, original.height), fill=alpha)
        strips.append((position, Image.composite(glowing, original, mask)))
    return strips


def companion_should_show(
    *,
    follow_ready: bool,
    hovered: bool,
    recording: bool,
    idle_seconds: float,
) -> bool:
    return follow_ready and (
        hovered or recording or idle_seconds < COMPANION_IDLE_HIDE_SECONDS
    )


def launcher_logo_frames(
    mark: Image.Image,
    canvas_size: int = 112,
    frame_count: int = 36,
) -> list[Image.Image]:
    """Build a soft two-beat green pulse with no disc, label, or boundary."""
    frames: list[Image.Image] = []
    for index in range(frame_count):
        phase = index / frame_count
        first_beat = math.exp(-((phase - 0.12) / 0.055) ** 2)
        second_beat = 0.68 * math.exp(-((phase - 0.29) / 0.075) ** 2)
        beat = min(1.0, first_beat + second_beat)
        mark_size = round(canvas_size * (0.58 + beat * 0.045))
        resized = mark.resize((mark_size, mark_size), Image.Resampling.LANCZOS)
        alpha = resized.getchannel("A")
        glow_mask = alpha.filter(ImageFilter.GaussianBlur(radius=7 + beat * 4))
        glow_mask = glow_mask.point(lambda value: round(value * (0.25 + beat * 0.62)))
        glow = Image.new("RGBA", resized.size, (62, 236, 183, 0))
        glow.putalpha(glow_mask)
        frame = Image.new("RGBA", (canvas_size, canvas_size), (0, 0, 0, 0))
        position = ((canvas_size - mark_size) // 2, (canvas_size - mark_size) // 2)
        frame.alpha_composite(glow, position)
        frame.alpha_composite(resized, position)
        frames.append(frame)
    return frames


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
        title_text: str = "Drag to capture a region",
        enter_text: str = "Enter captures every screen  ·  Esc cancels",
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
            text=title_text,
        )
        self.canvas.create_text(
            36,
            61,
            anchor="nw",
            fill=MUTED,
            font=(FONT_BODY, 9),
            text=enter_text,
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
        self.mode_label = "Audio"
        self.static_text = ""
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
        exclude_window_from_capture(self.window)

    def show(self, started_at: float | None = None, label: str = "Audio") -> None:
        if started_at is not None:
            self.started_at = started_at
        self.mode_label = label
        self.static_text = ""
        self.visible = True
        width, height = 410, 48
        vx, vy, vw, _vh = virtual_screen_bounds()
        position_window(self.window, vx + vw - width - 16, vy + 16, width, height)
        self.window.deiconify()
        self.window.lift()
        self._tick()

    def show_message(self, text: str) -> None:
        self.static_text = text
        self.visible = True
        width, height = 410, 48
        vx, vy, vw, _vh = virtual_screen_bounds()
        position_window(self.window, vx + vw - width - 16, vy + 16, width, height)
        self.label.configure(text=text)
        self.window.deiconify()
        self.window.lift()

    def hide(self) -> None:
        self.visible = False
        self.window.withdraw()

    def _tick(self) -> None:
        if not self.visible:
            return
        if self.static_text:
            return
        elapsed = max(0, int(time.monotonic() - self.started_at))
        minutes, seconds = divmod(elapsed, 60)
        self.label.configure(text=f"{self.mode_label}  {minutes:02d}:{seconds:02d}")
        self.window.after(500, self._tick)


class SettingsWindow:
    WIDTH = 540
    HEIGHT = 760

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
        self.window.resizable(True, True)
        self.window.minsize(500, 560)
        self.window.configure(bg=LINE)
        self.window.protocol("WM_DELETE_WINDOW", self.close)
        self.window.bind("<Escape>", lambda _event: self.close())
        self.window.bind("<MouseWheel>", self._scroll, add="+")
        exclude_window_from_capture(self.window)

        frame = tk.Frame(self.window, bg=PANEL)
        frame.pack(fill="both", expand=True, padx=1, pady=1)
        self.settings_canvas, shell, self.settings_scrollbar = build_scrollable_panel(frame)
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
        self.gesture_var = tk.BooleanVar(value=settings.mouse_gesture_enabled)
        self.video_microphone_var = tk.BooleanVar(value=settings.video_microphone_enabled)
        self.video_camera_var = tk.BooleanVar(value=settings.video_camera_enabled)
        self.video_countdown_var = tk.BooleanVar(value=settings.video_countdown_seconds > 0)
        self.video_cursor_var = tk.BooleanVar(value=settings.video_draw_cursor)
        self.video_60fps_var = tk.BooleanVar(value=settings.video_fps == 60)
        self.video_source_var = tk.StringVar(value=settings.video_source_kind)
        self.video_position_var = tk.StringVar(value=settings.video_camera_position)
        self.video_size_var = tk.StringVar(value=str(settings.video_camera_width))
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
        self._check(
            shell,
            "Open CursorPocket with two quick mouse circles",
            "Draw two small circles in either direction. Turn this off if it triggers accidentally.",
            self.gesture_var,
        )

        self._section_label(shell, "SCREEN RECORDING").pack(anchor="w", pady=(16, 5))
        self._choice(
            shell,
            "Default source",
            self.video_source_var,
            (("Display under pointer", "display"), ("Select a region", "region"), ("Previous window", "window")),
        )
        self._check(
            shell,
            "Include microphone narration by default",
            "CursorPocket names the active microphone before recording starts.",
            self.video_microphone_var,
        )
        self._check(
            shell,
            "Include my webcam in the next walkthrough",
            "Adds a camera bubble in the remembered corner. Toggle quickly with C.",
            self.video_camera_var,
        )
        self._check(
            shell,
            "Show a three-second recording countdown",
            "Gives you time to return to the screen before capture begins.",
            self.video_countdown_var,
        )
        self._check(
            shell,
            "Show the Windows pointer in walkthroughs",
            "Turn this off for clean demos where pointer motion is distracting.",
            self.video_cursor_var,
        )
        self._check(
            shell,
            "Record at 60 frames per second",
            "Smoother motion with a larger file and higher system load. Default is 30 fps.",
            self.video_60fps_var,
        )
        self._choice(
            shell,
            "Webcam corner",
            self.video_position_var,
            (("Bottom right", "bottom-right"), ("Bottom left", "bottom-left"), ("Top right", "top-right"), ("Top left", "top-left")),
        )
        self._choice(
            shell,
            "Webcam size",
            self.video_size_var,
            (("Small", "240"), ("Medium", "360"), ("Large", "480")),
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
            ("Open command mode", shortcuts.get("panel", "Ctrl + Shift + Space")),
            (
                "Screen walkthrough",
                f"V in menu · {shortcuts.get('video', 'Ctrl + Shift + V')} anywhere",
            ),
            ("Tap one screenshot key", "Q / W / E / R"),
            ("Tap one audio key", "A / S / D / F"),
            ("Tap one display key", "1 / 2 / 3 / 4"),
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
        height = min(self.HEIGHT, max(560, vh - 48))
        px = vx + max(12, (vw - self.WIDTH) // 2)
        py = vy + max(12, (vh - height) // 2)
        position_window(self.window, px, py, self.WIDTH, height, activate=True)
        self.window.lift()
        self.window.focus_force()

    def _scroll(self, event: tk.Event) -> str:
        units = panel_scroll_units(int(getattr(event, "delta", 0)))
        if units:
            self.settings_canvas.yview_scroll(units, "units")
        return "break"

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

    def _choice(
        self,
        parent: tk.Misc,
        title: str,
        variable: tk.StringVar,
        choices: tuple[tuple[str, str], ...],
    ) -> None:
        row = tk.Frame(parent, bg=PANEL, pady=5)
        row.pack(fill="x")
        tk.Label(
            row,
            text=title,
            bg=PANEL,
            fg=PAPER,
            font=(FONT_BODY, 10, "bold"),
        ).pack(side="left")
        labels = {value: label for label, value in choices}
        menu = tk.OptionMenu(row, variable, *[value for _label, value in choices])
        menu.configure(
            bg=PANEL_RAISED,
            fg=PAPER,
            activebackground=LINE,
            activeforeground=PAPER,
            highlightthickness=0,
            relief="flat",
            bd=0,
            width=20,
            font=(FONT_BODY, 9),
        )
        menu["menu"].configure(bg=PANEL_RAISED, fg=PAPER, font=(FONT_BODY, 9))
        menu.pack(side="right")
        variable.trace_add(
            "write",
            lambda *_args, widget=menu, mapping=labels, source=variable: widget.configure(
                text=mapping.get(source.get(), source.get())
            ),
        )
        menu.configure(text=labels.get(variable.get(), variable.get()))

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
            mouse_gesture_enabled=self.gesture_var.get(),
            onboarding_seen=self.settings.onboarding_seen,
            panel_geometry=self.settings.panel_geometry,
            video_microphone_enabled=self.video_microphone_var.get(),
            video_camera_enabled=self.video_camera_var.get(),
            video_microphone_name=self.settings.video_microphone_name,
            video_camera_name=self.settings.video_camera_name,
            video_source_kind=self.video_source_var.get(),
            video_camera_position=self.video_position_var.get(),
            video_camera_width=int(self.video_size_var.get()),
            video_fps=60 if self.video_60fps_var.get() else 30,
            video_countdown_seconds=3 if self.video_countdown_var.get() else 0,
            video_draw_cursor=self.video_cursor_var.get(),
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

    def __init__(self, activation_check: Callable[[], bool] | None = None) -> None:
        self.root = tk.Tk()
        self.root.withdraw()
        self.root.title("CursorPocket")
        self.root.report_callback_exception = self._report_tk_error
        self.settings_store = SettingsStore()
        self.settings = self.settings_store.load()
        self.store = CaptureStore(self.settings.capture_dir)
        self.audio_recorder = AudioRecorder()
        self.events: queue.SimpleQueue[object] = queue.SimpleQueue()
        self.ffmpeg_path = bundled_ffmpeg_path()
        self.video_recorder = FFmpegVideoRecorder(self.ffmpeg_path, self.events.put)
        self.video_capabilities: VideoCapabilities | None = None
        self.video_devices: list[MediaDevice] = []
        self.video_available = False
        self.video_recording = False
        self.video_finalizing = False
        self._video_started_once = False
        self.video_reservation: VideoReservation | None = None
        self.video_options: VideoOptions | None = None
        self.video_recording_started_at = 0.0
        self.video_countdown_session = 0
        self._video_last_disk_check = 0.0
        self._video_disk_stop = False
        self._quit_after_video = False
        self.startup_manager = StartupManager()
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
        self.command_mode_open = False
        self._command_session = 0
        self._command_pulse_phase = 0
        self._command_button_center = (0, 0)
        self._command_button_bounds = (0, 0, 0, 0)
        self._command_backdrop: Image.Image | None = None
        self._command_glass_photos: list[ImageTk.PhotoImage] = []
        self._command_logo_photo: ImageTk.PhotoImage | None = None
        self._command_launcher_frames: list[ImageTk.PhotoImage] = []
        self.capture_active = False
        self.recording = False
        self.hidden_mode = not self.settings.follow_cursor
        self.closing = False
        self.activation_check = activation_check
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
        self.gesture_detector = DoubleCircleGestureDetector()
        self._companion_pinned = False
        self._companion_hover = False
        self._companion_idle_hidden = False
        self._build_icon()
        self._build_companion()
        self._build_command_mode()
        self._build_panel()
        self.recording_indicator = RecordingIndicator(self.root, self.stop_active_recording)
        if self.hidden_mode:
            self._hide_companion_window()
        self.hotkeys.start()
        self.tray.start()
        self.tray.set_state(recording=False, hidden=self.hidden_mode)
        self._refresh_history()
        self.root.after(16, self._follow_tick)
        self.root.after(50, self._poll_events)
        threading.Thread(
            target=self._probe_video_backend,
            name="CursorPocketVideoProbe",
            daemon=True,
        ).start()
        if not self.settings.onboarding_seen:
            self.root.after(850, self._show_first_run)

    def run(self) -> None:
        self.root.mainloop()

    def _build_icon(self) -> None:
        self.icon_photo = ImageTk.PhotoImage(load_logo(64))
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
        exclude_window_from_capture(self.companion)
        self._draw_companion(False)
        self.companion_canvas.bind("<Enter>", self._companion_enter)
        self.companion_canvas.bind("<Leave>", self._companion_leave)
        self.companion_canvas.bind("<Button-1>", lambda _event: self._companion_primary_action())
        self.companion_canvas.bind("<Button-3>", lambda _event: self.toggle_command_mode())

    def _draw_companion(self, hover: bool) -> None:
        canvas = self.companion_canvas
        canvas.delete("all")
        dot_color = RED if self.recording or getattr(self, "video_recording", False) else GREEN
        inset = (self.COMPANION_SIZE - self.COMPANION_DOT_SIZE) // 2
        edge = inset + self.COMPANION_DOT_SIZE
        canvas.create_oval(inset, inset, edge, edge, fill=dot_color, outline="")

    def _show_companion_window(self) -> None:
        self.companion.deiconify()
        self._companion_idle_hidden = False

    def _hide_companion_window(self) -> None:
        self.companion.withdraw()
        self._companion_idle_hidden = True

    @classmethod
    def _companion_target(cls, cursor_x: int, cursor_y: int) -> tuple[int, int]:
        offset_x, offset_y = cls.COMPANION_OFFSET
        return cursor_x + offset_x, cursor_y + offset_y

    def _companion_primary_action(self) -> None:
        if self.recording or self.video_recording:
            self.stop_active_recording()
        else:
            self.toggle_command_mode()

    def _build_command_mode(self) -> None:
        self.command_mode = tk.Toplevel(self.root)
        self.command_mode.withdraw()
        self.command_mode.overrideredirect(True)
        self.command_mode.attributes("-topmost", True)
        try:
            self.command_mode.attributes("-transparentcolor", TRANSPARENT)
        except tk.TclError:
            pass
        self.command_mode.configure(bg=TRANSPARENT)
        self.command_mode.bind("<Escape>", lambda _event: self.hide_command_mode())
        self.command_canvas = tk.Canvas(
            self.command_mode,
            bg=TRANSPARENT,
            highlightthickness=0,
            bd=0,
            cursor="arrow",
        )
        self.command_canvas.pack(fill="both", expand=True)
        self.command_canvas.bind("<Button-1>", self._command_mode_click)
        exclude_window_from_capture(self.command_mode)
        self._command_glass_photos = []
        self._command_logo_photo = None
        self._command_launcher_frames = []

    def _render_command_mode(self, width: int, height: int) -> None:
        canvas = self.command_canvas
        canvas.configure(width=width, height=height)
        canvas.delete("all")
        self._command_glass_photos = []

        glow_depth = max(72, min(132, min(width, height) // 8))
        for position, glow_image in ambient_edge_glow_images(
            getattr(self, "_command_backdrop", None),
            width,
            height,
            glow_depth,
        ):
            glow_photo = ImageTk.PhotoImage(glow_image)
            self._command_glass_photos.append(glow_photo)
            canvas.create_image(
                position[0],
                position[1],
                image=glow_photo,
                anchor="nw",
                tags=("command_glow",),
            )

        legend_width = max(1, min(590, width - 36))
        legend_left = max(18, width - legend_width - 18)
        legend_top = 18
        legend_right = width - 18
        video_is_recording = getattr(self, "video_recording", False)
        command_rows = COMMAND_SHORTCUT_ROWS
        if video_is_recording:
            mic = "Mic on" if self.video_options and self.video_options.include_microphone else "Muted"
            camera = "Camera on" if self.video_options and self.video_options.include_camera else "Camera off"
            command_rows = (
                ("RECORDING", "V Stop & save  ·  D Discard"),
                ("STATUS", f"Screen  ·  {mic}  ·  {camera}"),
            )
        legend_height = 238 if video_is_recording else 378
        legend_bottom = min(height - 18, legend_top + legend_height)
        legend_box = (legend_left, legend_top, legend_right, legend_bottom)
        glass_fade = 64
        legend_glass_box = (
            max(0, legend_left - glass_fade),
            max(0, legend_top - glass_fade),
            min(width, legend_right + glass_fade),
            min(height, legend_bottom + glass_fade),
        )
        legend_glass = ImageTk.PhotoImage(
            liquid_glass_image(
                getattr(self, "_command_backdrop", None),
                legend_glass_box,
                radius=1,
                tint_alpha=96,
                feather=28,
                boundary=False,
                core_box=legend_box,
            )
        )
        self._command_glass_photos.append(legend_glass)
        canvas.create_image(
            legend_glass_box[0],
            legend_glass_box[1],
            image=legend_glass,
            anchor="nw",
            tags=("command_glass",),
        )

        self._command_logo_photo = ImageTk.PhotoImage(load_logo(42))
        canvas.create_image(
            legend_left + 42,
            legend_top + 32,
            image=self._command_logo_photo,
            anchor="nw",
        )
        canvas.create_text(
            legend_left + 94,
            legend_top + 35,
            text="CURSORPOCKET",
            fill=PAPER,
            anchor="nw",
            font=(FONT_DISPLAY, 11, "bold"),
        )
        canvas.create_text(
            legend_left + 94,
            legend_top + 55,
            text="RECORDING  •  ACTIVE" if video_is_recording else "COMMAND MODE  •  ACTIVE",
            fill=RED if video_is_recording else GREEN,
            anchor="nw",
            font=(FONT_MONO, 8, "bold"),
        )
        canvas.create_text(
            legend_left + 42,
            legend_top + 92,
            text="Walkthrough recording" if video_is_recording else "Tap one key",
            fill=PAPER,
            anchor="nw",
            font=(FONT_BODY, 18, "bold"),
        )
        row_y = legend_top + 136
        for label, shortcuts in command_rows:
            canvas.create_text(
                legend_left + 42,
                row_y,
                text=label,
                fill="#82AFA9",
                anchor="nw",
                font=(FONT_MONO, 8, "bold"),
            )
            canvas.create_text(
                legend_left + 142,
                row_y,
                text=shortcuts,
                fill=PAPER,
                anchor="nw",
                font=(FONT_MONO, 9),
            )
            row_y += 38
        canvas.create_text(
            legend_left + 42,
            legend_bottom - 41,
            text=(
                "ESC  CLOSE     •     RECORDING CONTINUES"
                if video_is_recording
                else "ESC  CLOSE     •     AUTO-CLOSES IN 30 SECONDS"
            ),
            fill="#8CA0AA",
            anchor="nw",
            font=(FONT_MONO, 8, "bold"),
        )

        button_x = max(60, width - 72)
        button_y = max(60, height - 72)
        self._command_button_center = (button_x, button_y)
        self._command_button_bounds = (
            button_x - 56,
            button_y - 56,
            button_x + 56,
            button_y + 56,
        )
        self._command_launcher_frames = [
            ImageTk.PhotoImage(frame)
            for frame in launcher_logo_frames(load_logo(76))
        ]
        canvas.create_image(
            button_x,
            button_y,
            image=self._command_launcher_frames[0],
            anchor="center",
            tags=("command_launcher_logo",),
        )

    def _animate_command_mode(self, session: int) -> None:
        if not self.command_mode_open or session != self._command_session:
            return
        if self._command_launcher_frames:
            self._command_pulse_phase = (
                self._command_pulse_phase + 1
            ) % len(self._command_launcher_frames)
            self.command_canvas.itemconfigure(
                "command_launcher_logo",
                image=self._command_launcher_frames[self._command_pulse_phase],
            )
        self.command_mode.after(
            50,
            lambda: self._animate_command_mode(session),
        )

    def _command_mode_click(self, event: tk.Event) -> None:
        left, top, right, bottom = self._command_button_bounds
        if left <= int(event.x) <= right and top <= int(event.y) <= bottom:
            self.hide_command_mode()
            self.show_panel(snapshot_context=False)
            return
        self.hide_command_mode()

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
        exclude_window_from_capture(self.panel)
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
            text=PANEL_SHORTCUT_HELP,
            bg=PANEL,
            fg=MUTED,
            justify="left",
            wraplength=470,
            font=(FONT_BODY, 10),
        ).pack(anchor="w", pady=(8, 12))

        self._section_label(content, "VIDEO WALKTHROUGH  ·  V / C").pack(
            anchor="w",
            pady=(2, 3),
        )
        (
            _video_glyph,
            self.video_title,
            self.video_description,
            self.video_shortcut,
        ) = self._action_row(
            content,
            "V",
            "Record this display",
            "Preparing the local video recorder…",
            "V",
            self.toggle_video_recording,
            accent=GREEN,
            priority=True,
        )
        video_options = tk.Frame(content, bg=PANEL)
        video_options.pack(fill="x", pady=(2, 7))
        self.video_mic_button = self._small_button(
            video_options,
            "Mic on",
            self.toggle_video_microphone,
        )
        self.video_mic_button.pack(side="left")
        self.video_camera_button = self._small_button(
            video_options,
            "Camera off",
            self.toggle_video_camera,
        )
        self.video_camera_button.pack(side="left", padx=(6, 0))
        self.video_region_button = self._header_button(
            video_options,
            "Region",
            lambda: self.start_video_recording(VideoSourceKind.REGION),
        )
        self.video_region_button.pack(side="left", padx=(14, 0))
        self.video_window_button = self._header_button(
            video_options,
            "Window",
            lambda: self.start_video_recording(VideoSourceKind.WINDOW),
        )
        self.video_window_button.pack(side="left", padx=(6, 0))

        video_devices = tk.Frame(content, bg=PANEL)
        video_devices.pack(fill="x", pady=(0, 7))
        self.video_mic_device_button = self._header_button(
            video_devices,
            "Mic device",
            lambda: self.cycle_video_device("audio"),
        )
        self.video_mic_device_button.pack(side="left")
        self.video_camera_device_button = self._header_button(
            video_devices,
            "Camera device",
            lambda: self.cycle_video_device("video"),
        )
        self.video_camera_device_button.pack(side="left", padx=(6, 0))
        video_camera_layout = tk.Frame(content, bg=PANEL)
        video_camera_layout.pack(fill="x", pady=(0, 7))
        self.video_camera_position_button = self._header_button(
            video_camera_layout,
            "Bottom right",
            self.cycle_video_camera_position,
        )
        self.video_camera_position_button.pack(side="left")
        self.video_camera_size_button = self._header_button(
            video_camera_layout,
            "Medium",
            self.cycle_video_camera_size,
        )
        self.video_camera_size_button.pack(side="left", padx=(6, 0))

        self.screenshot_buttons = self._keyboard_group(
            content,
            "SCREENSHOTS  ·  TAP ONE: Q  W  E  R",
            (
                ("Q", "Region", self.capture_screenshot, True),
                ("W", "Window", self.capture_active_window, True),
                ("E", "All screens", self.capture_all_screens, True),
                ("R", "Repeat", self.repeat_last_region, True),
            ),
        )
        self.audio_buttons = self._keyboard_group(
            content,
            "AUDIO  ·  TAP ONE: A  S  D  F",
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
            "FULL DISPLAY  ·  TAP ONE: 1  2  3  4",
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
            text=f"Open command mode anywhere: {self.registered_shortcuts['panel']}",
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
        if not self.panel_open and not getattr(self, "command_mode_open", False):
            return None
        if int(getattr(event, "state", 0)) & PANEL_BLOCKED_MODIFIER_MASK:
            return None
        action = panel_key_action(str(getattr(event, "keysym", "")))
        if action is None:
            return None
        if getattr(self, "video_recording", False):
            if action == "toggle_video":
                self.stop_video_recording()
            elif action == "discard_audio":
                self.discard_video_recording()
            else:
                self.show_toast(
                    "Walkthrough is recording",
                    "Press V to stop and save, or D to discard.",
                )
            if getattr(self, "command_mode_open", False):
                self.hide_command_mode()
            return "break"
        commands: dict[str, Callable[[], None]] = {
            "toggle_video": self.toggle_video_recording,
            "toggle_video_camera": self.toggle_video_camera,
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
        if getattr(self, "command_mode_open", False):
            self.hide_command_mode()
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
        now = time.monotonic()
        gesture_ready = (
            self.settings.mouse_gesture_enabled
            and not self.capture_active
            and not self.panel_open
            and not getattr(self, "command_mode_open", False)
            and not self.recording
            and not getattr(self, "video_recording", False)
            and self.settings_window is None
        )
        follow_ready = (
            not self.hidden_mode
            and (not self.capture_active or getattr(self, "video_recording", False))
            and not self.panel_open
            and not getattr(self, "command_mode_open", False)
            and self.settings.follow_cursor
        )
        if gesture_ready or follow_ready:
            cursor_x, cursor_y = cursor_position()
            if gesture_ready and self.gesture_detector.feed(cursor_x, cursor_y, now):
                self.show_command_mode()
                self.root.after(16, self._follow_tick)
                return
            if not gesture_ready:
                self.gesture_detector.reset()
        else:
            self.gesture_detector.reset()

        if follow_ready:
            if (cursor_x, cursor_y) != self._last_cursor:
                self._last_cursor = (cursor_x, cursor_y)
                self._last_cursor_move = now
            idle_seconds = now - self._last_cursor_move
            if self._companion_pinned and not self._companion_hover:
                center_x = self._companion_x + self.COMPANION_SIZE / 2
                center_y = self._companion_y + self.COMPANION_SIZE / 2
                distance = math.hypot(cursor_x - center_x, cursor_y - center_y)
                if distance > 132:
                    self._companion_pinned = False
            if not self._companion_pinned and idle_seconds >= COMPANION_PIN_SECONDS:
                self._companion_pinned = True
        show_companion = companion_should_show(
            follow_ready=follow_ready,
            hovered=getattr(self, "_companion_hover", False),
            recording=self.recording or getattr(self, "video_recording", False),
            idle_seconds=now - getattr(self, "_last_cursor_move", now),
        )
        if show_companion:
            if getattr(self, "_companion_idle_hidden", False):
                self._show_companion_window()
            if not self._companion_hover and not self._companion_pinned:
                self._companion_x, self._companion_y = self._companion_target(cursor_x, cursor_y)
            position_window(
                self.companion,
                round(self._companion_x),
                round(self._companion_y),
                self.COMPANION_SIZE,
                self.COMPANION_SIZE,
            )
        elif hasattr(self, "companion") and not getattr(
            self,
            "_companion_idle_hidden",
            False,
        ):
            self._companion_pinned = False
            self._hide_companion_window()
        self.root.after(16, self._follow_tick)

    def _companion_enter(self, _event: tk.Event) -> None:
        self._companion_hover = True
        self._companion_pinned = True
        self._draw_companion(True)

    def _companion_leave(self, _event: tk.Event) -> None:
        self._companion_hover = False
        self._draw_companion(False)

    def toggle_command_mode(self) -> None:
        if self.command_mode_open:
            self.hide_command_mode()
        else:
            self.show_command_mode()

    def toggle_panel(self) -> None:
        if self.panel_open:
            self.hide_panel()
        else:
            self.show_panel()

    def _remember_source_context(self) -> None:
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

    def show_command_mode(self) -> None:
        if self.capture_active and not getattr(self, "video_recording", False):
            return
        if self.panel_open:
            self.hide_panel()
        self._remember_source_context()
        self.display_bounds = monitor_bounds()
        cursor_x, cursor_y = cursor_position()
        left, top, right, bottom = monitor_for_point(
            self.display_bounds,
            cursor_x,
            cursor_y,
        )
        width = max(1, right - left)
        height = max(1, bottom - top)
        self.command_mode_open = True
        self._command_session += 1
        session = self._command_session
        self._command_pulse_phase = 0
        self._hide_companion_window()
        self.root.update_idletasks()
        try:
            backdrop = ImageGrab.grab(
                bbox=(left, top, right, bottom),
                all_screens=True,
            ).convert("RGB")
            if backdrop.size != (width, height):
                backdrop = backdrop.resize((width, height), Image.Resampling.BILINEAR)
            self._command_backdrop = backdrop
        except (OSError, ValueError):
            self._command_backdrop = None
        self._render_command_mode(width, height)
        position_window(
            self.command_mode,
            left,
            top,
            width,
            height,
            activate=True,
        )
        self.command_mode.lift()
        self.command_mode.focus_force()
        self._animate_command_mode(session)
        self.command_mode.after(
            COMMAND_MODE_TIMEOUT_MS,
            lambda: self._expire_command_mode(session),
        )

    def _expire_command_mode(self, session: int) -> None:
        if self.command_mode_open and session == self._command_session:
            self.hide_command_mode()

    def hide_command_mode(self) -> None:
        if not self.command_mode_open:
            return
        self.command_mode_open = False
        self._command_session += 1
        self.command_mode.withdraw()
        self._command_backdrop = None
        self._command_glass_photos = []
        self._command_launcher_frames = []
        self._companion_pinned = False
        self._last_cursor_move = time.monotonic()
        if (
            not self.hidden_mode
            and (not self.capture_active or getattr(self, "video_recording", False))
            and self.settings.follow_cursor
        ):
            self._show_companion_window()

    def _show_first_run(self) -> None:
        if self.closing:
            return
        self.settings.onboarding_seen = True
        self.settings_store.save(self.settings)
        self.show_command_mode()
        self.status.configure(
            text="Ready. Use command mode, then tap one capture key.",
            fg=BLUE,
        )

    def show_panel(self, snapshot_context: bool = True) -> None:
        if self.capture_active:
            return
        self.hide_command_mode()
        if snapshot_context:
            self._remember_source_context()
        if self.hidden_mode:
            self.hidden_mode = False
            self._show_companion_window()
            self.tray.set_state(
                recording=self.recording,
                hidden=False,
                video=getattr(self, "video_recording", False),
            )
        self.panel_open = True
        self._hide_companion_window()
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
        self.hide_command_mode()

    def capture_screenshot(self) -> None:
        if self.capture_active:
            return
        self.capture_active = True
        self.hide_panel()
        self._hide_companion_window()
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
        self._hide_companion_window()
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
        cached_selection = (
            self.source_selected_text
            if self.panel_open or getattr(self, "command_mode_open", False)
            else ""
        )
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
        cached_url = (
            self.source_page_url
            if self.panel_open or getattr(self, "command_mode_open", False)
            else ""
        )
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
            self._show_companion_window()

    def _probe_video_backend(self) -> None:
        try:
            if not self.ffmpeg_path.exists():
                raise FileNotFoundError("CursorPocket's video component is not installed.")
            capabilities = probe_video_capabilities(self.ffmpeg_path)
            if not capabilities.ready:
                raise RuntimeError("The installed video component is missing required Windows capture features.")
            devices = list_directshow_devices(self.ffmpeg_path)
            self.events.put(("video_backend_ready", capabilities, devices))
        except Exception as error:
            self.events.put(("video_backend_error", str(error)))

    def _device_name(self, kind: str, saved_name: str) -> str:
        devices = [device for device in self.video_devices if device.kind == kind]
        if saved_name and any(device.name == saved_name for device in devices):
            return saved_name
        return devices[0].name if devices else ""

    @staticmethod
    def _compact_device_name(name: str, limit: int = 18) -> str:
        value = name.strip()
        return value if len(value) <= limit else value[: limit - 1].rstrip() + "…"

    def _update_video_ui(self) -> None:
        mic_name = self._device_name("audio", self.settings.video_microphone_name)
        camera_name = self._device_name("video", self.settings.video_camera_name)
        if hasattr(self, "video_mic_button"):
            self.video_mic_button.configure(
                text="Mic on" if self.settings.video_microphone_enabled else "Mic off"
            )
            self.video_camera_button.configure(
                text="Camera on" if self.settings.video_camera_enabled else "Camera off"
            )
            self.video_mic_device_button.configure(
                text="Mic · " + self._compact_device_name(mic_name or "None")
            )
            self.video_camera_device_button.configure(
                text="Camera · " + self._compact_device_name(camera_name or "None")
            )
            position_label = self.settings.video_camera_position.replace("-", " ").title()
            size_label = {240: "Small", 360: "Medium", 480: "Large"}.get(
                self.settings.video_camera_width,
                "Medium",
            )
            self.video_camera_position_button.configure(text=position_label)
            self.video_camera_size_button.configure(text=size_label)
            source_label = self.settings.video_source_kind.title()
            self.video_title.configure(text=f"Record {source_label.lower()}")
        if hasattr(self, "video_description"):
            if not self.video_available:
                description = "Video recorder unavailable · screenshots and audio still work"
            elif self.video_recording:
                description = "Recording now · press V or click the red dot to stop and save"
            else:
                mic = mic_name if self.settings.video_microphone_enabled else "Off"
                camera = camera_name if self.settings.video_camera_enabled else "Off"
                source = self.settings.video_source_kind.title()
                camera_layout = ""
                if self.settings.video_camera_enabled:
                    camera_layout = (
                        f" · {self.settings.video_camera_position.replace('-', ' ')}"
                        f" · {self.settings.video_camera_width}px"
                    )
                description = f"{source} · {self.settings.video_fps} fps · Mic: {mic} · Camera: {camera}{camera_layout}"
            self.video_description.configure(text=description)

    def cycle_video_device(self, kind: str) -> None:
        if self.video_recording:
            self.show_toast("Device choice is locked", "Stop this walkthrough before changing it.")
            return
        devices = [device for device in self.video_devices if device.kind == kind]
        if not devices:
            self.show_toast(
                "No device is available",
                "Connect a camera or microphone, then reopen CursorPocket.",
                error=True,
            )
            return
        attribute = "video_microphone_name" if kind == "audio" else "video_camera_name"
        current = getattr(self.settings, attribute)
        names = [device.name for device in devices]
        next_index = (names.index(current) + 1) % len(names) if current in names else 0
        setattr(self.settings, attribute, names[next_index])
        self.settings_store.save(self.settings)
        self._update_video_ui()
        label = "Microphone" if kind == "audio" else "Camera"
        self.show_toast(f"{label} selected", names[next_index])

    def cycle_video_camera_position(self) -> None:
        if self.video_recording:
            self.show_toast("Camera layout is locked", "Stop this walkthrough before changing it.")
            return
        positions = ("bottom-right", "bottom-left", "top-right", "top-left")
        current = self.settings.video_camera_position
        next_index = (positions.index(current) + 1) % len(positions) if current in positions else 0
        self.settings.video_camera_position = positions[next_index]
        self.settings_store.save(self.settings)
        self._update_video_ui()

    def cycle_video_camera_size(self) -> None:
        if self.video_recording:
            self.show_toast("Camera layout is locked", "Stop this walkthrough before changing it.")
            return
        sizes = (240, 360, 480)
        current = self.settings.video_camera_width
        next_index = (sizes.index(current) + 1) % len(sizes) if current in sizes else 0
        self.settings.video_camera_width = sizes[next_index]
        self.settings_store.save(self.settings)
        self._update_video_ui()

    def toggle_video_microphone(self) -> None:
        if self.video_recording:
            self.show_toast("Microphone choice is locked", "Stop this walkthrough before changing it.")
            return
        self.settings.video_microphone_enabled = not self.settings.video_microphone_enabled
        self.settings_store.save(self.settings)
        self._update_video_ui()

    def toggle_video_camera(self) -> None:
        if self.video_recording:
            self.show_toast("Camera choice is locked", "Stop this walkthrough before changing it.")
            return
        enabling = not self.settings.video_camera_enabled
        if enabling and not self._device_name("video", self.settings.video_camera_name):
            self.show_toast(
                "No camera is available",
                "Connect a webcam or allow desktop camera access in Windows Settings.",
                error=True,
                action=lambda: os.startfile("ms-settings:privacy-webcam"),  # type: ignore[attr-defined]
            )
            return
        self.settings.video_camera_enabled = enabling
        self.settings_store.save(self.settings)
        self._update_video_ui()
        state = "included" if enabling else "off"
        self.show_toast("Webcam " + state, "This choice applies to the next walkthrough.")

    def toggle_video_recording(self) -> None:
        if self.video_recording:
            self.stop_video_recording()
        else:
            self.start_video_recording()

    def start_video_recording(
        self,
        source_kind: VideoSourceKind | None = None,
        *,
        bounds: tuple[int, int, int, int] | None = None,
        window_handle: int | None = None,
    ) -> None:
        if self.recording:
            self.show_toast("Audio note is already recording", "Save or discard it before recording the screen.", error=True)
            return
        if self.capture_active:
            self.show_toast("Finish the current capture first", "Screen recording did not start.", error=True)
            return
        if self.settings_window is not None:
            self.show_toast(
                "Close Settings first",
                "Recording choices are locked when a walkthrough starts.",
            )
            return
        if not self.video_available:
            self.show_toast(
                "Video recorder isn’t ready",
                "Repair or reinstall CursorPocket's local video component.",
                error=True,
            )
            return
        if source_kind is None:
            try:
                source_kind = VideoSourceKind(self.settings.video_source_kind)
            except ValueError:
                source_kind = VideoSourceKind.DISPLAY
        if source_kind == VideoSourceKind.REGION and bounds is None:
            self._choose_video_region()
            return
        if source_kind == VideoSourceKind.WINDOW:
            window_handle = window_handle or self.last_foreground_handle
            bounds = window_bounds(window_handle)
            if not window_handle or not bounds:
                self.show_toast(
                    "That window can’t be recorded",
                    "Return to a visible window, reopen CursorPocket, then choose Window.",
                    error=True,
                )
                return
        try:
            free_bytes = shutil.disk_usage(self.store.base_dir).free
        except OSError:
            free_bytes = 0
        if free_bytes and free_bytes < 1024 * 1024 * 1024:
            self.show_toast(
                "Not enough free space",
                "Free at least 1 GB in the capture folder before recording.",
                error=True,
                action=self.open_folder,
            )
            return
        microphone_name = self._device_name("audio", self.settings.video_microphone_name)
        camera_name = self._device_name("video", self.settings.video_camera_name)
        if self.settings.video_microphone_enabled and not microphone_name:
            self.show_toast(
                "Microphone couldn’t start",
                "No microphone is available. Turn Mic off to record a muted walkthrough.",
                error=True,
                action=lambda: os.startfile("ms-settings:privacy-microphone"),  # type: ignore[attr-defined]
            )
            return
        if self.settings.video_camera_enabled and not camera_name:
            self.show_toast(
                "Camera couldn’t start",
                "Turn Camera off or allow desktop camera access in Windows Settings.",
                error=True,
                action=lambda: os.startfile("ms-settings:privacy-webcam"),  # type: ignore[attr-defined]
            )
            return
        self.display_bounds = monitor_bounds()
        if source_kind == VideoSourceKind.DISPLAY:
            cursor_x, cursor_y = cursor_position()
            selected_bounds = monitor_for_point(self.display_bounds, cursor_x, cursor_y)
            bounds = selected_bounds
        else:
            selected_bounds = bounds
        if selected_bounds is None:
            self.show_toast("Recording source is missing", "Choose a display, region, or window.", error=True)
            return
        center_x = (selected_bounds[0] + selected_bounds[2]) // 2
        center_y = (selected_bounds[1] + selected_bounds[3]) // 2
        selected_display = monitor_for_point(self.display_bounds, center_x, center_y)
        display_index = self.display_bounds.index(selected_display) if selected_display in self.display_bounds else 0
        options = VideoOptions(
            source_kind=source_kind,
            display_index=display_index,
            bounds=selected_bounds,
            window_handle=window_handle,
            fps=self.settings.video_fps,
            draw_cursor=self.settings.video_draw_cursor,
            include_microphone=self.settings.video_microphone_enabled,
            microphone_name=microphone_name,
            include_camera=self.settings.video_camera_enabled,
            camera_name=camera_name,
            camera_position=self.settings.video_camera_position,
            camera_width=self.settings.video_camera_width,
        )
        metadata = {
            "source_kind": options.source_kind.value,
            "display_index": display_index,
            "source_bounds": list(selected_bounds),
            "window_handle": window_handle,
            "draw_cursor": options.draw_cursor,
            "fps": options.fps,
            "include_microphone": options.include_microphone,
            "microphone_name": microphone_name,
            "include_camera": options.include_camera,
            "camera_name": camera_name,
            "camera_position": options.camera_position,
            "camera_width": options.camera_width,
        }
        try:
            reservation = self.store.reserve_video(metadata)
        except OSError as error:
            self.show_toast("Recording couldn’t start", str(error), error=True)
            return
        self.settings.video_microphone_name = microphone_name
        self.settings.video_camera_name = camera_name
        self.settings.video_source_kind = source_kind.value
        self.settings_store.save(self.settings)
        self.video_options = options
        self.video_reservation = reservation
        self.video_recording = True
        self.capture_active = True
        self._video_started_once = False
        self._video_last_disk_check = 0.0
        self._video_disk_stop = False
        self.video_countdown_session += 1
        session = self.video_countdown_session
        self.hide_panel()
        self.hide_command_mode()
        self._hide_companion_window()
        self._update_video_ui()
        countdown = self.settings.video_countdown_seconds
        if countdown:
            self._video_countdown_tick(session, countdown)
        else:
            self._begin_video_process(session)

    def _choose_video_region(self) -> None:
        self.capture_active = True
        self.hide_panel()
        self.hide_command_mode()
        self._hide_companion_window()
        self.root.after(180, self._begin_video_region_selection)

    def _begin_video_region_selection(self) -> None:
        try:
            screenshot = ImageGrab.grab(all_screens=True)
        except Exception as error:
            self.capture_active = False
            self._restore_companion()
            self.show_toast("Couldn’t select a video region", str(error), error=True)
            return
        vx, vy, _vw, _vh = virtual_screen_bounds()
        RegionSelector(
            self.root,
            screenshot,
            (vx, vy),
            self._video_region_selected,
            self._cancel_video_region,
            title_text="Drag the region to record",
            enter_text="Enter records every screen  ·  Esc cancels",
        )

    def _video_region_selected(
        self,
        _image: Image.Image,
        bounds: tuple[int, int, int, int],
    ) -> None:
        self.capture_active = False
        self.start_video_recording(VideoSourceKind.REGION, bounds=bounds)

    def _cancel_video_region(self) -> None:
        self.capture_active = False
        self._restore_companion()
        self.show_toast("Walkthrough cancelled", "No video was recorded.")

    def _video_countdown_tick(self, session: int, remaining: int) -> None:
        if session != self.video_countdown_session or not self.video_recording:
            return
        if remaining <= 0:
            self._begin_video_process(session)
            return
        camera = " · Camera" if self.video_options and self.video_options.include_camera else ""
        self.recording_indicator.show_message(f"Screen{camera} starts in {remaining}")
        self.root.after(1000, lambda: self._video_countdown_tick(session, remaining - 1))

    def _begin_video_process(self, session: int) -> None:
        if session != self.video_countdown_session or not self.video_recording:
            return
        if not self.video_options or not self.video_reservation:
            return
        if (
            self.video_options.source_kind == VideoSourceKind.WINDOW
            and not window_bounds(self.video_options.window_handle)
        ):
            self.store.discard_video(self.video_reservation)
            self._reset_video_state()
            self.show_toast(
                "That window is no longer recordable",
                "It may have closed or been minimized during the countdown.",
                error=True,
            )
            return
        self.recording_indicator.show_message("Starting screen recorder…")
        try:
            self.video_recorder.start(self.video_options, self.video_reservation.partial_path)
            self.root.after(12_000, lambda: self._video_start_timeout(session))
        except Exception as error:
            self.store.discard_video(self.video_reservation)
            self._reset_video_state()
            self.show_toast("Screen recording didn’t start", str(error), error=True)

    def _video_start_timeout(self, session: int) -> None:
        if (
            session == self.video_countdown_session
            and self.video_recording
            and self.video_recorder.state == RecordingState.STARTING
        ):
            self.show_toast(
                "Recording hardware didn’t respond",
                "CursorPocket stopped safely. The camera or microphone may be busy.",
                error=True,
            )
            self.video_recorder.stop(discard=True)

    def stop_active_recording(self) -> None:
        if self.video_recording:
            self.stop_video_recording()
        else:
            self.stop_audio_recording()

    def stop_video_recording(self, *, discard: bool = False) -> None:
        if not self.video_recording:
            return
        if self.video_finalizing:
            return
        self.video_countdown_session += 1
        if not self.video_recorder.is_recording:
            if self.video_reservation:
                self.store.discard_video(self.video_reservation)
            self._reset_video_state()
            self.show_toast("Recording cancelled", "No video file was created.")
            return
        self.recording_indicator.show_message("Discarding video…" if discard else "Saving video…")
        self.video_recorder.stop(discard=discard)

    def discard_video_recording(self) -> None:
        if not self.video_recording:
            return
        if messagebox.askyesno(
            "Discard walkthrough?",
            "Discard the current screen recording without saving it?",
            icon="warning",
            parent=self.recording_indicator.window,
        ):
            self.stop_video_recording(discard=True)

    def _finalize_video_worker(
        self,
        result: VideoProcessResult,
        reservation: VideoReservation,
        options: VideoOptions,
    ) -> None:
        try:
            if result.discard_requested:
                self.store.discard_video(reservation)
                self.events.put(("video_discarded",))
                return
            inspection = inspect_video_file(self.ffmpeg_path, result.output_path)
            record = self.store.finalize_video(
                reservation,
                inspection.duration_seconds,
                inspection.width,
                inspection.height,
                inspection.fps or options.fps,
                metadata={
                    "has_audio": inspection.has_audio,
                    "source_kind": options.source_kind.value,
                    "display_index": options.display_index,
                    "source_bounds": list(options.bounds) if options.bounds else None,
                    "draw_cursor": options.draw_cursor,
                    "include_microphone": options.include_microphone,
                    "microphone_name": options.microphone_name,
                    "include_camera": options.include_camera,
                    "camera_name": options.camera_name,
                    "camera_position": options.camera_position,
                    "camera_width": options.camera_width,
                    "video_codec": "h264",
                    "audio_codec": "aac" if inspection.has_audio else None,
                    "recovered": result.forced or result.return_code != 0,
                },
            )
            self.events.put(("video_saved", record))
        except Exception as error:
            self.events.put(("video_save_error", str(error), result.error_detail))

    def _reset_video_state(self) -> None:
        self.video_recording = False
        self.video_finalizing = False
        self._video_started_once = False
        self.capture_active = False
        self.video_options = None
        self.video_reservation = None
        self._companion_pinned = False
        self.recording_indicator.hide()
        self.tray.set_state(recording=self.recording, hidden=self.hidden_mode, video=False)
        self._update_video_ui()
        if hasattr(self, "companion_canvas"):
            self._draw_companion(False)
        self._restore_companion()

    def _recover_pending_videos(self) -> None:
        for reservation in self.store.pending_videos():
            if not reservation.partial_path.exists():
                continue
            try:
                inspection = inspect_video_file(self.ffmpeg_path, reservation.partial_path)
                record = self.store.finalize_video(
                    reservation,
                    inspection.duration_seconds,
                    inspection.width,
                    inspection.height,
                    inspection.fps or 30.0,
                    metadata={"has_audio": inspection.has_audio, "recovered": True},
                )
                self.events.put(("video_recovered", record))
            except Exception as error:
                self.events.put(("video_recovery_error", str(error)))

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
            self._show_companion_window()
            self.tray.set_state(
                recording=self.recording,
                hidden=False,
                video=getattr(self, "video_recording", False),
            )
            shortcut = self.registered_shortcuts.get("hidden", "Ctrl + Shift + H")
            self.show_toast("Dot visible", f"{shortcut} toggles the dot")
            return
        self.hide_panel()
        self.hidden_mode = True
        self._hide_companion_window()
        self.tray.set_state(
            recording=self.recording,
            hidden=True,
            video=getattr(self, "video_recording", False),
        )
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
        glyphs = {
            "screenshot": "▣",
            "text": "¶",
            "link": "↗",
            "audio": "●",
            "video": "▶",
        }
        for record in records:
            row = tk.Frame(self.history_frame, bg=INK, padx=10, pady=7, cursor="hand2")
            row.pack(fill="x", pady=2)
            tk.Label(
                row,
                text=glyphs.get(record.kind, "•"),
                bg=INK,
                fg=GREEN if record.kind in {"audio", "video"} else BLUE,
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
        if self.recording or self.video_recording:
            self.show_toast(
                "Settings are locked while recording",
                "Stop and save or discard the recording first.",
            )
            return
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
                self._show_companion_window()
            else:
                self.hidden_mode = True
                self._hide_companion_window()
            self.tray.set_state(
                recording=self.recording,
                hidden=self.hidden_mode,
                video=getattr(self, "video_recording", False),
            )
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
        exclude_window_from_capture(toast)
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

    def _handle_video_event(self, event: tuple[object, ...]) -> bool:
        kind = str(event[0])
        if kind == "video_backend_ready":
            self.video_capabilities = event[1]  # type: ignore[assignment]
            self.video_devices = list(event[2])  # type: ignore[arg-type]
            self.video_available = True
            self._update_video_ui()
            self.status.configure(text="Ready · video walkthroughs available", fg=BLUE)
            threading.Thread(
                target=self._recover_pending_videos,
                name="CursorPocketVideoRecovery",
                daemon=True,
            ).start()
            return True
        if kind == "video_backend_error":
            self.video_available = False
            self._update_video_ui()
            self.status.configure(text="Video recorder unavailable · other captures still work", fg=ORANGE)
            return True
        if kind == "video_started":
            if self.video_recording:
                self._video_started_once = True
                self.video_recording_started_at = time.monotonic()
                status_parts = ["Screen"]
                status_parts.append(
                    "Mic on" if self.video_options and self.video_options.include_microphone else "Muted"
                )
                status_parts.append(
                    "Camera on" if self.video_options and self.video_options.include_camera else "Camera off"
                )
                self.recording_indicator.show(
                    self.video_recording_started_at,
                    " · ".join(status_parts),
                )
                self.tray.set_state(recording=False, hidden=self.hidden_mode, video=True)
                self._draw_companion(False)
                self._update_video_ui()
                camera = " with webcam" if self.video_options and self.video_options.include_camera else ""
                self.show_toast(
                    "Recording screen" + camera,
                    "Press V, use the red Stop bar, or click the red cursor dot to save.",
                )
            return True
        if kind == "video_progress":
            elapsed = float(event[1]) if len(event) > 1 else 0.0
            if elapsed >= self._video_last_disk_check + 5.0:
                self._video_last_disk_check = elapsed
                try:
                    free_bytes = shutil.disk_usage(self.store.base_dir).free
                except OSError:
                    free_bytes = 0
                if free_bytes and free_bytes < 128 * 1024 * 1024 and not self._video_disk_stop:
                    self._video_disk_stop = True
                    self.show_toast(
                        "Recording stopped · drive nearly full",
                        "CursorPocket is preserving the video recorded so far.",
                        error=True,
                        action=self.open_folder,
                    )
                    self.stop_video_recording()
            return True
        if kind == "video_finished":
            result = event[1]
            if not isinstance(result, VideoProcessResult):
                return True
            reservation = self.video_reservation
            options = self.video_options
            if reservation is None or options is None:
                self._reset_video_state()
                return True
            if (
                not self._video_started_once
                and result.return_code != 0
                and (
                    not result.output_path.exists()
                    or result.output_path.stat().st_size < 1024
                )
            ):
                self.store.discard_video(reservation)
                self._reset_video_state()
                self._show_video_start_error(options, result.error_detail)
                return True
            self.video_finalizing = True
            self.recording_indicator.show_message(
                "Discarding video…" if result.discard_requested else "Saving video…"
            )
            threading.Thread(
                target=self._finalize_video_worker,
                args=(result, reservation, options),
                name="CursorPocketVideoFinalize",
                daemon=True,
            ).start()
            return True
        if kind == "video_saved":
            record = event[1]
            self._reset_video_state()
            self._refresh_history()
            if isinstance(record, CaptureRecord):
                self.show_toast(
                    "Walkthrough saved",
                    f"{record.preview} · Click to open",
                    action=lambda target=record: self.open_capture(target),
                )
            if self._quit_after_video:
                self._quit_after_video = False
                self.root.after(0, self.quit)
            return True
        if kind == "video_discarded":
            self._reset_video_state()
            self.show_toast("Walkthrough discarded", "No video was saved.")
            if self._quit_after_video:
                self._quit_after_video = False
                self.root.after(0, self.quit)
            return True
        if kind == "video_save_error":
            message = str(event[1])
            self._reset_video_state()
            self.show_toast(
                "Video needs recovery",
                f"{message} · The partial recording remains in .in-progress.",
                error=True,
                action=self.open_folder,
            )
            if self._quit_after_video:
                self._quit_after_video = False
                self.root.after(0, self.quit)
            return True
        if kind == "video_recovered":
            record = event[1]
            self._refresh_history()
            if isinstance(record, CaptureRecord):
                self.show_toast(
                    "Recovered an interrupted video",
                    f"{record.preview} · Click to open",
                    action=lambda target=record: self.open_capture(target),
                )
            return True
        if kind == "video_recovery_error":
            self.status.configure(text="An interrupted video is waiting in .in-progress", fg=ORANGE)
            return True
        return False

    def _show_video_start_error(self, options: VideoOptions, detail: str) -> None:
        summary = detail.splitlines()[-1] if detail.strip() else "The recording device did not respond."
        if options.include_camera:
            self.show_toast(
                "Camera couldn’t start",
                "It may be busy in another app. Turn Camera off to record without it, or check Windows camera access.",
                error=True,
                action=lambda: os.startfile("ms-settings:privacy-webcam"),  # type: ignore[attr-defined]
            )
        elif options.include_microphone:
            self.show_toast(
                "Microphone couldn’t start",
                "It may be busy in another app. Turn Mic off for a muted walkthrough, or check Windows microphone access.",
                error=True,
                action=lambda: os.startfile("ms-settings:privacy-microphone"),  # type: ignore[attr-defined]
            )
        else:
            self.show_toast("Screen recording couldn’t start", summary, error=True)

    def _poll_events(self) -> None:
        if self.closing:
            return
        if self.activation_check is not None and self.activation_check():
            self.show_command_mode()
        while True:
            try:
                event = self.events.get_nowait()
            except queue.Empty:
                break
            if isinstance(event, tuple) and event and self._handle_video_event(event):
                continue
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
                        text=f"Open command mode anywhere: {label}"
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
                self.toggle_command_mode()
            elif event == "audio":
                self.toggle_audio_recording()
            elif event == "video":
                self.toggle_video_recording()
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
        if self.video_recording:
            if self.video_finalizing:
                self._quit_after_video = True
                self.recording_indicator.show_message("Saving video before closing…")
                return
            decision = messagebox.askyesnocancel(
                "Close CursorPocket?",
                "Save the screen recording before closing?\n\nYes saves it. No discards it.",
                icon="warning",
                parent=self.recording_indicator.window,
            )
            if decision is None:
                return
            self._quit_after_video = True
            was_running = self.video_recorder.is_recording
            self.stop_video_recording(discard=not decision)
            if not was_running:
                self._quit_after_video = False
                self.root.after(0, self.quit)
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
