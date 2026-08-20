"""Generate the CursorPocket brand mark.

The mark is one idea: a cursor crossing into a pocket. Above the pocket mouth the
arrow is solid; below it the same arrow becomes a hole punched through the
pocket. Nothing is shaded, glossed, or beveled, so the silhouette survives all
the way down to a 16 px tray icon, and the geometry here is the same geometry the
app draws as a XAML `Path` in App.xaml.

Usage:  python tools/make_logo.py
Writes: assets/cursorpocket-logo.png, assets/cursorpocket-logo-4k.png,
        assets/cursorpocket-mark.png, assets/cursorpocket.ico
"""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "assets"

GROUND = (13, 20, 18, 255)
GREEN = (69, 224, 140, 255)

UNIT = 1024  # design grid
SS = 4  # supersample factor
GROUND_RADIUS = 228

# Pocket: left, top, right, bottom on the design grid.
POCKET = (208.0, 524.0, 816.0, 892.0)
POCKET_RADIUS_TOP = 28.0
POCKET_RADIUS_BOTTOM = 112.0

# Cursor arrow on a 21.4 x 30.6 box with the tip at the origin.
CURSOR = [
    (0.0, 0.0),
    (0.0, 26.5),
    (7.2, 20.1),
    (11.6, 30.6),
    (16.4, 28.6),
    (12.1, 18.3),
    (21.4, 17.6),
]
CURSOR_SCALE = 16.5
CURSOR_TIP = (352.0, 120.0)
KEYLINE = 22.0  # dark gap between the arrow and the pocket it crosses


# Below this pixel size the pocket and its keyline collapse into a blob, so the
# icon falls back to the cursor alone -- the same simplification the tray, the
# taskbar, and small Explorer views need to stay legible.
SMALL_SIZE = 40
SMALL_CURSOR_SCALE = 24.1
SMALL_CURSOR_TIP = (254.0, 143.0)


def cursor_points(scale: float = 1.0, small: bool = False):
    """The arrow polygon placed on the design grid, scaled to the render size."""
    tip = SMALL_CURSOR_TIP if small else CURSOR_TIP
    size = SMALL_CURSOR_SCALE if small else CURSOR_SCALE
    return [
        ((tip[0] + x * size) * scale, (tip[1] + y * size) * scale)
        for x, y in CURSOR
    ]


def _draw_pocket(draw: ImageDraw.ImageDraw, scale: float, fill) -> None:
    left, top, right, bottom = (value * scale for value in POCKET)
    radius_bottom = POCKET_RADIUS_BOTTOM * scale
    radius_top = POCKET_RADIUS_TOP * scale
    draw.rounded_rectangle(
        (left, top, right, bottom),
        radius=radius_bottom,
        fill=fill,
        corners=(False, False, True, True),
    )
    draw.rounded_rectangle(
        (left, top, right, top + radius_bottom * 2),
        radius=radius_top,
        fill=fill,
        corners=(True, True, False, False),
    )


def render_mark(size: int, ground: bool = True, simplify: bool | None = None) -> Image.Image:
    """Render the mark at `size` px, with or without the dark rounded ground.

    `simplify` forces the cursor-only form on or off. Leave it None to pick by
    size, or set it True for an asset that will be displayed small however large
    the file is, such as the command panel header.
    """
    small = size <= SMALL_SIZE if simplify is None else simplify
    # Keep the supersampled canvas bounded so a 4K master stays renderable.
    supersample = max(1, min(SS, 8192 // max(size, 1)))
    render = size * supersample
    scale = render / UNIT
    canvas = Image.new("RGBA", (render, render), (0, 0, 0, 0))
    draw = ImageDraw.Draw(canvas)
    if ground:
        draw.rounded_rectangle(
            (0, 0, render - 1, render - 1),
            radius=GROUND_RADIUS * scale,
            fill=GROUND,
        )
    if not small:
        _draw_pocket(draw, scale, GREEN)

    # A keyline is stroked around the arrow before the arrow itself is filled,
    # so the pocket is cut back by an even gap wherever the two overlap and the
    # arrow reads as passing in front of the pocket mouth.
    arrow = cursor_points(scale, small=small)
    draw.line(
        arrow + [arrow[0]],
        fill=GROUND if ground else (0, 0, 0, 0),
        width=max(1, round(KEYLINE * 2 * scale)),
        joint="curve",
    )
    draw.polygon(arrow, fill=GREEN)

    return canvas.resize((size, size), Image.Resampling.LANCZOS)


def main() -> None:
    ASSETS.mkdir(exist_ok=True)
    render_mark(1024).save(ASSETS / "cursorpocket-logo.png")
    render_mark(4096).save(ASSETS / "cursorpocket-logo-4k.png")
    render_mark(1024, ground=False).save(ASSETS / "cursorpocket-mark.png")
    render_mark(256, simplify=True).save(
        ROOT / "native" / "CursorPocket.App" / "Assets" / "CursorPocketLogo.png"
    )

    sizes = (256, 128, 64, 48, 40, 32, 24, 20, 16)
    frames = [render_mark(size) for size in sizes]
    frames[0].save(
        ASSETS / "cursorpocket.ico",
        format="ICO",
        sizes=[(size, size) for size in sizes],
        append_images=frames[1:],
    )
    print(f"wrote {len(sizes)} icon frames + 3 png masters to {ASSETS}")


if __name__ == "__main__":
    main()
