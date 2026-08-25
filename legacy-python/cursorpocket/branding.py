from __future__ import annotations

import sys
from pathlib import Path

from PIL import Image, ImageDraw


LOGO_ASSET = Path("assets") / "brand" / "main-logo.png"
ICON_ASSET = Path("assets") / "brand" / "export" / "CursorPocket.ico"


def resource_path(relative: Path) -> Path:
    root = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parents[2]))
    return root / relative


def load_logo(size: int) -> Image.Image:
    """Load the branded mark and fit it onto a transparent square canvas.

    The .ico carries a frame drawn for each size, including the cursor-only form
    used below 40 px, so the tray icon stays legible instead of shrinking the
    full lockup into a smudge.
    """
    source = _load_icon_frame(size)
    if source is None:
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


def _load_icon_frame(size: int) -> Image.Image | None:
    """Return the .ico frame drawn closest to `size`, or None if it is missing."""
    try:
        with Image.open(resource_path(ICON_ASSET)) as icon:
            available = sorted(icon.info.get("sizes", []), key=lambda pair: pair[0])
            if not available:
                return None
            best = min(available, key=lambda pair: (pair[0] < size, abs(pair[0] - size)))
            icon.size = best
            return icon.convert("RGBA").copy()
    except (OSError, ValueError, KeyError):
        return None


def tray_icon(recording: bool, size: int = 64) -> Image.Image:
    """Return the stable application identity for every tray state.

    The tooltip, cursor companion, and recording HUD communicate live state. The
    installed brand mark itself no longer swaps or recolours between states.
    """
    del recording
    return load_logo(size)


def _fallback_logo(size: int) -> Image.Image:
    """The canonical cursor on the dark ground, used only if the assets are missing."""
    image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((0, 0, size - 1, size - 1), radius=round(size * 0.22), fill="#0D1412")
    cursor = [
        (0.0, 0.0),
        (0.0, 26.5),
        (7.2, 20.1),
        (11.6, 30.6),
        (16.4, 28.6),
        (12.1, 18.3),
        (21.4, 17.6),
    ]
    scale = size * 0.72 / 30.6
    left = (size - 21.4 * scale) / 2
    top = (size - 30.6 * scale) / 2
    draw.polygon([(left + x * scale, top + y * scale) for x, y in cursor], fill="#45E08C")
    return image
