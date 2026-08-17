from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image, ImageDraw


LOGO_ASSET = Path("assets") / "cursorpocket-logo.png"


def resource_path(relative: Path) -> Path:
    root = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parents[1]))
    return root / relative


def load_logo(size: int) -> Image.Image:
    """Load the branded mark and fit it onto a transparent square canvas."""
    try:
        source = Image.open(resource_path(LOGO_ASSET)).convert("RGBA")
        bounds = source.getbbox()
        if bounds:
            source = source.crop(bounds)
    except (OSError, ValueError):
        source = _fallback_logo(256)

    inset = max(1, round(size * 0.05))
    available = max(1, size - inset * 2)
    source.thumbnail((available, available), Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    canvas.alpha_composite(
        source,
        ((size - source.width) // 2, (size - source.height) // 2),
    )
    return canvas


def tray_icon(recording: bool, size: int = 64) -> Image.Image:
    image = load_logo(size)
    if recording:
        draw = ImageDraw.Draw(image)
        diameter = max(12, round(size * 0.28))
        margin = max(1, round(size * 0.03))
        left = size - diameter - margin
        top = size - diameter - margin
        draw.ellipse(
            (left - 2, top - 2, size - margin + 2, size - margin + 2),
            fill="#10151D",
        )
        draw.ellipse(
            (left, top, size - margin, size - margin),
            fill="#FF5D68",
        )
    return image


def _fallback_logo(size: int) -> Image.Image:
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    edge = round(size * 0.12)
    draw.ellipse((edge, edge, size - edge, size - edge), fill="#101820")
    draw.arc(
        (edge * 2, edge * 2, size - edge, size - edge),
        205,
        520,
        fill="#42D392",
        width=max(2, round(size * 0.08)),
    )
    bead = round(size * 0.12)
    draw.ellipse(
        (size - edge * 2 - bead, edge * 2, size - edge * 2 + bead, edge * 2 + bead * 2),
        fill="#8FFFD0",
    )
    return image
