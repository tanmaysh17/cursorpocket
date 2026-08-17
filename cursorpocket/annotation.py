from __future__ import annotations

import math
import tkinter as tk
from tkinter import simpledialog
from typing import Callable, Iterable

from PIL import Image, ImageDraw, ImageFont, ImageTk

from .windows import position_window, virtual_screen_bounds


INK = "#10151D"
PANEL = "#18212D"
PANEL_RAISED = "#202B39"
LINE = "#2D3A4B"
PAPER = "#F4F7FB"
MUTED = "#8D9AAD"
BLUE = "#63B3FF"
GREEN = "#42D392"
RED = "#FF5D68"
YELLOW = "#FFE66D"
FONT_BODY = "Segoe UI"
FONT_MONO = "Cascadia Mono"


def _rgba(image: Image.Image) -> Image.Image:
    return image.convert("RGBA") if image.mode != "RGBA" else image.copy()


def draw_stroke(
    image: Image.Image,
    points: Iterable[tuple[int, int]],
    color: str,
    width: int,
    opacity: int = 255,
) -> Image.Image:
    result = _rgba(image)
    path = list(points)
    if not path:
        return result
    overlay = Image.new("RGBA", result.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)
    fill = (*_hex_rgb(color), max(0, min(255, opacity)))
    if len(path) == 1:
        x, y = path[0]
        radius = max(1, width // 2)
        draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=fill)
    else:
        draw.line(path, fill=fill, width=max(1, width), joint="curve")
    return Image.alpha_composite(result, overlay)


def draw_rectangle(
    image: Image.Image,
    start: tuple[int, int],
    end: tuple[int, int],
    color: str,
    width: int,
) -> Image.Image:
    result = _rgba(image)
    draw = ImageDraw.Draw(result)
    x1, x2 = sorted((start[0], end[0]))
    y1, y2 = sorted((start[1], end[1]))
    draw.rectangle((x1, y1, x2, y2), outline=color, width=max(1, width))
    return result


def draw_arrow(
    image: Image.Image,
    start: tuple[int, int],
    end: tuple[int, int],
    color: str,
    width: int,
) -> Image.Image:
    result = _rgba(image)
    draw = ImageDraw.Draw(result)
    draw.line((start, end), fill=color, width=max(1, width))
    angle = math.atan2(end[1] - start[1], end[0] - start[0])
    head = max(12, width * 4)
    spread = math.pi / 7
    left = (
        end[0] - head * math.cos(angle - spread),
        end[1] - head * math.sin(angle - spread),
    )
    right = (
        end[0] - head * math.cos(angle + spread),
        end[1] - head * math.sin(angle + spread),
    )
    draw.polygon((end, left, right), fill=color)
    return result


def draw_text(
    image: Image.Image,
    point: tuple[int, int],
    value: str,
    color: str,
    size: int,
) -> Image.Image:
    result = _rgba(image)
    try:
        font = ImageFont.truetype("arial.ttf", max(10, size))
    except OSError:
        font = ImageFont.load_default()
    ImageDraw.Draw(result).multiline_text(
        point,
        value,
        fill=color,
        font=font,
        spacing=max(3, size // 5),
        stroke_width=max(1, size // 18),
        stroke_fill="#10151D",
    )
    return result


def _hex_rgb(value: str) -> tuple[int, int, int]:
    clean = value.lstrip("#")
    return tuple(int(clean[index : index + 2], 16) for index in (0, 2, 4))  # type: ignore[return-value]


class ScreenshotAnnotator:
    """A focused, native screenshot markup step with full-resolution output."""

    def __init__(
        self,
        root: tk.Tk,
        image: Image.Image,
        bounds: tuple[int, int, int, int],
        on_save: Callable[[Image.Image, tuple[int, int, int, int]], None],
        on_cancel: Callable[[], None],
    ) -> None:
        self.image = _rgba(image)
        self.bounds = bounds
        self.on_save = on_save
        self.on_cancel = on_cancel
        self.undo_stack: list[Image.Image] = []
        self.tool = "pen"
        self.color = RED
        self.start: tuple[int, int] | None = None
        self.points: list[tuple[int, int]] = []
        self.preview_item: int | None = None
        self.closed = False

        vx, vy, vw, vh = virtual_screen_bounds()
        available_width = max(520, min(1400, vw - 100))
        available_height = max(320, vh - 190)
        self.scale = min(
            1.0,
            available_width / max(1, self.image.width),
            available_height / max(1, self.image.height),
        )
        self.view_width = max(1, round(self.image.width * self.scale))
        self.view_height = max(1, round(self.image.height * self.scale))
        window_width = max(720, self.view_width + 32)
        window_height = self.view_height + 132

        self.window = tk.Toplevel(root)
        self.window.title("CursorPocket — Annotate screenshot")
        self.window.attributes("-topmost", True)
        self.window.configure(bg=LINE)
        self.window.resizable(True, True)
        self.window.minsize(min(720, window_width), min(460, window_height))
        self.window.protocol("WM_DELETE_WINDOW", self.cancel)
        self.window.bind("<Escape>", lambda _event: self.cancel())
        self.window.bind("<Return>", lambda _event: self.save())
        self.window.bind("<Control-z>", lambda _event: self.undo())

        shell = tk.Frame(self.window, bg=PANEL, padx=15, pady=13)
        shell.pack(fill="both", expand=True, padx=1, pady=1)
        toolbar = tk.Frame(shell, bg=PANEL)
        toolbar.pack(fill="x", pady=(0, 10))
        tk.Label(
            toolbar,
            text="ANNOTATE",
            bg=PANEL,
            fg=BLUE,
            font=(FONT_MONO, 9, "bold"),
        ).pack(side="left", padx=(0, 12))
        self.tool_buttons: dict[str, tk.Button] = {}
        for key, label in (
            ("pen", "Pen"),
            ("highlight", "Highlight"),
            ("arrow", "Arrow"),
            ("rectangle", "Rectangle"),
            ("text", "Text"),
        ):
            button = self._button(toolbar, label, lambda target=key: self.select_tool(target))
            button.pack(side="left", padx=(0, 4))
            self.tool_buttons[key] = button
        self._button(toolbar, "Undo", self.undo).pack(side="left", padx=(8, 0))
        self._button(toolbar, "Cancel", self.cancel).pack(side="right")
        self._button(toolbar, "Save  ↵", self.save, primary=True).pack(side="right", padx=(0, 6))

        color_bar = tk.Frame(shell, bg=PANEL)
        color_bar.pack(fill="x", pady=(0, 9))
        tk.Label(
            color_bar,
            text="COLOR",
            bg=PANEL,
            fg=MUTED,
            font=(FONT_MONO, 8, "bold"),
        ).pack(side="left", padx=(0, 8))
        for color in (RED, YELLOW, GREEN, BLUE, PAPER):
            tk.Button(
                color_bar,
                text="",
                command=lambda selected=color: self.select_color(selected),
                bg=color,
                activebackground=color,
                relief="flat",
                bd=0,
                width=2,
                height=1,
                cursor="hand2",
            ).pack(side="left", padx=(0, 5))
        self.hint = tk.Label(
            color_bar,
            text="Draw on the image · Enter saves · Ctrl+Z undoes",
            bg=PANEL,
            fg=MUTED,
            font=(FONT_BODY, 9),
        )
        self.hint.pack(side="right")

        canvas_shell = tk.Frame(shell, bg=INK)
        canvas_shell.pack(fill="both", expand=True)
        self.canvas = tk.Canvas(
            canvas_shell,
            width=self.view_width,
            height=self.view_height,
            bg=INK,
            highlightthickness=0,
            cursor="crosshair",
        )
        self.canvas.pack(anchor="center", expand=True)
        self.canvas.bind("<ButtonPress-1>", self._press)
        self.canvas.bind("<B1-Motion>", self._drag)
        self.canvas.bind("<ButtonRelease-1>", self._release)
        self._render()
        self.select_tool("pen")

        px = vx + max(12, (vw - window_width) // 2)
        py = vy + max(12, (vh - window_height) // 2)
        position_window(self.window, px, py, window_width, window_height, activate=True)
        self.window.lift()
        self.window.focus_force()
        self.window.grab_set()

    def _button(
        self,
        parent: tk.Misc,
        text: str,
        command: Callable[[], None],
        primary: bool = False,
    ) -> tk.Button:
        return tk.Button(
            parent,
            text=text,
            command=command,
            bg=BLUE if primary else PANEL_RAISED,
            fg=INK if primary else PAPER,
            activebackground="#88C6FF" if primary else LINE,
            activeforeground=INK if primary else PAPER,
            relief="flat",
            bd=0,
            padx=11,
            pady=6,
            cursor="hand2",
            font=(FONT_BODY, 9, "bold"),
        )

    def select_tool(self, tool: str) -> None:
        self.tool = tool
        for key, button in self.tool_buttons.items():
            button.configure(bg=BLUE if key == tool else PANEL_RAISED, fg=INK if key == tool else PAPER)
        instructions = {
            "pen": "Drag to draw",
            "highlight": "Drag to highlight",
            "arrow": "Drag from the label to its target",
            "rectangle": "Drag around an area",
            "text": "Click where text should begin",
        }
        self.hint.configure(text=f"{instructions[tool]} · Enter saves · Ctrl+Z undoes")

    def select_color(self, color: str) -> None:
        self.color = color

    def _image_point(self, event: tk.Event) -> tuple[int, int]:
        x = max(0, min(self.view_width - 1, int(event.x)))
        y = max(0, min(self.view_height - 1, int(event.y)))
        return round(x / self.scale), round(y / self.scale)

    def _view_point(self, point: tuple[int, int]) -> tuple[int, int]:
        return round(point[0] * self.scale), round(point[1] * self.scale)

    def _press(self, event: tk.Event) -> None:
        point = self._image_point(event)
        if self.tool == "text":
            value = simpledialog.askstring(
                "Add text",
                "Text to place on the screenshot:",
                parent=self.window,
            )
            if value and value.strip():
                self._checkpoint()
                self.image = draw_text(
                    self.image,
                    point,
                    value.strip(),
                    self.color,
                    max(18, round(24 / self.scale)),
                )
                self._render()
            return
        self.start = point
        self.points = [point]
        vx, vy = self._view_point(point)
        if self.tool in {"pen", "highlight"}:
            self.preview_item = self.canvas.create_line(
                vx,
                vy,
                vx,
                vy,
                fill=YELLOW if self.tool == "highlight" else self.color,
                width=14 if self.tool == "highlight" else 4,
                capstyle="round",
                smooth=True,
            )
        elif self.tool == "rectangle":
            self.preview_item = self.canvas.create_rectangle(vx, vy, vx, vy, outline=self.color, width=3)
        else:
            self.preview_item = self.canvas.create_line(vx, vy, vx, vy, fill=self.color, width=4, arrow=tk.LAST)

    def _drag(self, event: tk.Event) -> None:
        if self.start is None or self.preview_item is None:
            return
        point = self._image_point(event)
        if self.tool in {"pen", "highlight"}:
            self.points.append(point)
            coordinates = [value for item in self.points for value in self._view_point(item)]
            self.canvas.coords(self.preview_item, *coordinates)
        else:
            self.canvas.coords(self.preview_item, *self._view_point(self.start), *self._view_point(point))

    def _release(self, event: tk.Event) -> None:
        if self.start is None:
            return
        end = self._image_point(event)
        self._checkpoint()
        width = max(2, round(4 / self.scale))
        if self.tool == "pen":
            self.points.append(end)
            self.image = draw_stroke(self.image, self.points, self.color, width)
        elif self.tool == "highlight":
            self.points.append(end)
            self.image = draw_stroke(
                self.image,
                self.points,
                YELLOW,
                max(8, round(16 / self.scale)),
                opacity=96,
            )
        elif self.tool == "rectangle":
            self.image = draw_rectangle(self.image, self.start, end, self.color, width)
        else:
            self.image = draw_arrow(self.image, self.start, end, self.color, width)
        self.start = None
        self.points = []
        self.preview_item = None
        self._render()

    def _checkpoint(self) -> None:
        self.undo_stack.append(self.image.copy())
        if len(self.undo_stack) > 30:
            self.undo_stack.pop(0)

    def undo(self) -> None:
        if not self.undo_stack:
            return
        self.image = self.undo_stack.pop()
        self._render()

    def _render(self) -> None:
        preview = self.image.convert("RGB")
        if preview.size != (self.view_width, self.view_height):
            preview = preview.resize((self.view_width, self.view_height), Image.Resampling.LANCZOS)
        self.photo = ImageTk.PhotoImage(preview)
        self.canvas.delete("all")
        self.canvas.create_image(0, 0, anchor="nw", image=self.photo)

    def save(self) -> None:
        if self.closed:
            return
        image = self.image.convert("RGB")
        bounds = self.bounds
        self._destroy()
        self.on_save(image, bounds)

    def cancel(self) -> None:
        if self.closed:
            return
        self._destroy()
        self.on_cancel()

    def _destroy(self) -> None:
        self.closed = True
        try:
            self.window.grab_release()
        except tk.TclError:
            pass
        self.window.destroy()
