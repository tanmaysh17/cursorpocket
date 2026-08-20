"""Contact sheet and vector harness for the brand-mark candidates.

Writes only into artifacts/brandmark/ (gitignored). The PIL sheet is the
decision document -- the .ico ships from this same rasteriser, so the small
sizes here are the truth. The HTML harness exists to prove each emitted path
string parses and fills correctly as a vector, which nothing else on this
machine can check while the WinUI app cannot build.

Usage:  python tools/logo_contact_sheet.py
"""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from PIL import Image, ImageDraw, ImageFilter

import pathdata
from logo_candidates import GREEN, GROUND, RED, candidates, svg_d, xaml_data
from make_logo import GROUND_RADIUS, UNIT

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "artifacts" / "brandmark"

DARK_TASKBAR = (26, 32, 34, 255)
LIGHT_TASKBAR = (238, 240, 238, 255)
SHEET_BG = (16, 21, 22, 255)
INK = (233, 240, 236, 255)
MUTED = (140, 156, 150, 255)

MARK_FRACTION = 0.62  # mark box as a share of the ground tile
SIZES = (16, 20, 24, 32, 48, 128)


def render_tile(candidate, size: int, accent=GREEN, ground=True) -> Image.Image:
    """The candidate on the dark rounded ground tile, like the shipping icon."""
    supersample = max(1, min(4, 8192 // max(size, 1)))
    render = size * supersample
    canvas = Image.new("RGBA", (render, render), (0, 0, 0, 0))
    draw = ImageDraw.Draw(canvas)
    if ground:
        draw.rounded_rectangle(
            (0, 0, render - 1, render - 1),
            radius=GROUND_RADIUS / UNIT * render,
            fill=GROUND,
        )
    inset = render * (1 - MARK_FRACTION) / 2
    even_odd, subpaths = pathdata.parse(candidate.data)
    placed = pathdata.fit(subpaths, (inset, inset, render - inset, render - inset))
    mask = pathdata.fill_mask(placed, (render, render), even_odd)
    canvas.paste(accent, (0, 0), mask)
    return canvas.resize((size, size), Image.Resampling.LANCZOS)


def contact_sheet() -> Image.Image:
    marks = candidates()
    col_w = 320
    pad = 24
    width = pad + len(marks) * (col_w + pad)

    bands = []  # list of (label, height, draw_fn(sheet, x, y, candidate))

    def size_band(taskbar, accent):
        def draw(sheet, x, y, c):
            cx = x
            for size in SIZES:
                tile = render_tile(c, size, accent=accent)
                cell = Image.new("RGBA", (size + 12, size + 12), taskbar)
                cell.alpha_composite(tile, (6, 6))
                sheet.alpha_composite(cell, (cx, y + (140 - 12 - size) // 2))
                cx += size + 18
        return draw, 152

    def squint(sheet, x, y, c):
        tile = render_tile(c, 128).filter(ImageFilter.GaussianBlur(4))
        cell = Image.new("RGBA", (140, 140), DARK_TASKBAR)
        cell.alpha_composite(tile, (6, 6))
        sheet.alpha_composite(cell, (x, y))

    def pixel_grid(sheet, x, y, c):
        tile = render_tile(c, 16).resize((128, 128), Image.Resampling.NEAREST)
        cell = Image.new("RGBA", (140, 140), DARK_TASKBAR)
        cell.alpha_composite(tile, (6, 6))
        sheet.alpha_composite(cell, (x + 160, y))

    def vector_boxes(sheet, x, y, c):
        # The two vector usage sites at 3x, with the real box outlined so the
        # letterboxing of a square mark in a 11x16 box is visible.
        even_odd, subpaths = pathdata.parse(c.data)
        for index, (bw, bh) in enumerate(((11, 16), (26, 37))):
            scale = 3
            cw, ch = bw * scale + 24, bh * scale + 24
            cell = Image.new("RGBA", (cw, ch), SHEET_BG)
            draw = ImageDraw.Draw(cell)
            box = (12, 12, 12 + bw * scale, 12 + bh * scale)
            draw.rectangle(box, outline=(70, 84, 78, 255))
            placed = pathdata.fit(subpaths, box)
            mask = pathdata.fill_mask(placed, (cw, ch), even_odd)
            cell.paste(GREEN, (0, 0), mask)
            sheet.alpha_composite(cell, (x + index * 90, y))

    band_defs = [
        ("DARK TASKBAR · GREEN", *size_band(DARK_TASKBAR, GREEN)),
        ("LIGHT TASKBAR · GREEN", *size_band(LIGHT_TASKBAR, GREEN)),
        ("RECORDING RED", *size_band(DARK_TASKBAR, RED)),
        ("SQUINT (BLUR 4)  ·  16PX PIXEL GRID", lambda s, x, y, c: (squint(s, x, y, c), pixel_grid(s, x, y, c)), 152),
        ("VECTOR FORM - 11x16 AND 26x37 DIP BOXES AT 3X", vector_boxes, 160),
    ]

    header = 92
    band_label_h = 30
    height = header + sum(h + band_label_h for _, _, h in band_defs) + pad

    sheet = Image.new("RGBA", (width, height), SHEET_BG)
    draw = ImageDraw.Draw(sheet)

    # Column headers.
    for column, c in enumerate(marks):
        x = pad + column * (col_w + pad)
        stroke = pathdata.min_stroke(c.data)
        at16 = stroke / 100 * MARK_FRACTION * 16
        draw.text((x, 18), f"{column + 1} · {c.title.upper()}", fill=INK)
        draw.text((x, 40), c.note, fill=MUTED)
        draw.text((x, 62), f"thinnest feature at 16px: {at16:.1f}px", fill=MUTED)

    y = header
    for label, band_draw, band_h in band_defs:
        draw.text((pad, y), label, fill=MUTED)
        for column, c in enumerate(marks):
            band_draw(sheet, pad + column * (col_w + pad), y + band_label_h, c)
        y += band_h + band_label_h

    return sheet


def write_html() -> Path:
    """Each candidate's emitted path as inline SVG; a malformed path renders as nothing."""
    rows = []
    for c in candidates():
        rows.append(f"""
  <section>
    <h2>{c.title}</h2>
    <p>{c.note}</p>
    <div class="row">
      <svg viewBox="0 0 100 100" width="128" height="128"><path d="{svg_d(c)}" fill="#45E08C" fill-rule="nonzero"/></svg>
      <svg viewBox="0 0 100 100" width="48" height="48"><path d="{svg_d(c)}" fill="#45E08C" fill-rule="nonzero"/></svg>
      <svg viewBox="0 0 100 100" width="24" height="24"><path d="{svg_d(c)}" fill="#45E08C" fill-rule="nonzero"/></svg>
      <svg viewBox="0 0 100 100" width="128" height="128"><path d="{svg_d(c)}" fill="#FF5F6B" fill-rule="nonzero"/></svg>
    </div>
    <pre>{xaml_data(c)}</pre>
  </section>""")
    html = f"""<!doctype html><html><head><meta charset="utf-8"><title>brandmark candidates</title>
<style>
body{{background:#101516;color:#E9F0EC;font-family:Segoe UI,sans-serif;padding:32px}}
section{{margin-bottom:36px;border-bottom:1px solid #2a3432;padding-bottom:24px}}
.row{{display:flex;gap:24px;align-items:flex-end;background:#1A2022;padding:16px;border-radius:8px;width:fit-content}}
pre{{font-size:10px;color:#8C9C96;white-space:pre-wrap;max-width:900px}}
h2{{margin:0 0 4px}} p{{color:#8C9C96;margin:0 0 12px}}
</style></head><body><h1>CursorPocket brand-mark candidates — vector harness</h1>{''.join(rows)}</body></html>"""
    OUT.mkdir(parents=True, exist_ok=True)
    target = OUT / "candidates.html"
    target.write_text(html, encoding="utf-8")
    return target


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    sheet = contact_sheet()
    sheet.save(OUT / "contact-sheet.png")
    write_html()
    paths = OUT / "paths.txt"
    paths.write_text(
        "\n\n".join(f"{c.key}:\n{xaml_data(c)}" for c in candidates()),
        encoding="utf-8",
    )
    print(f"wrote contact-sheet.png ({sheet.size[0]}x{sheet.size[1]}), candidates.html, paths.txt to {OUT}")


if __name__ == "__main__":
    main()
