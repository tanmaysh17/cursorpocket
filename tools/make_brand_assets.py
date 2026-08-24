from __future__ import annotations

import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
BRAND = ROOT / "assets" / "brand"
IMAGERY = BRAND / "imagery"
EXPORT = BRAND / "export"

PRIMARY_SOURCE = BRAND / "main-logo.png"
READY_SOURCE = BRAND / "brand-logo-02-pocket-v3-transparent.png"
WORDMARK_SOURCE = BRAND / "brand-logo-03-wordmark-transparent.png"
HERO_SOURCE = IMAGERY / "cursorpocket-catch-field-v2-transparent.png"

PINE = (7, 19, 15, 255)
PAPER = (246, 244, 236, 255)
READY = (54, 229, 140, 255)
FOLD = (43, 111, 99, 255)
RECORDING = (255, 89, 100, 255)
LINK = (122, 167, 255, 255)
MIST = (142, 160, 153, 255)

APP_SIZES = (16, 20, 24, 32, 44, 48, 64, 128, 150, 256, 310, 512, 1024)
TRAY_SIZES = (16, 20, 24, 32, 48, 64)


def load_master(path: Path) -> Image.Image:
    if not path.exists():
        raise FileNotFoundError(f"Approved brand master is missing: {path}")
    image = Image.open(path).convert("RGBA")
    if image.getchannel("A").getbbox() is None:
        raise ValueError(f"Approved brand master has no visible pixels: {path}")
    return image


def save_png(image: Image.Image, path: Path) -> None:
    image.save(path, "PNG", optimize=True)


def content_crop(image: Image.Image, padding: int = 0) -> Image.Image:
    box = image.getchannel("A").getbbox()
    if box is None:
        return image.copy()
    left = max(0, box[0] - padding)
    top = max(0, box[1] - padding)
    right = min(image.width, box[2] + padding)
    bottom = min(image.height, box[3] + padding)
    return image.crop((left, top, right, bottom))


def contain(image: Image.Image, width: int, height: int) -> Image.Image:
    scale = min(width / image.width, height / image.height)
    size = (max(1, round(image.width * scale)), max(1, round(image.height * scale)))
    return image.resize(size, Image.Resampling.LANCZOS)


def paste_center(canvas: Image.Image, image: Image.Image, box: tuple[int, int, int, int]) -> None:
    left, top, right, bottom = box
    fitted = contain(image, right - left, bottom - top)
    x = left + (right - left - fitted.width) // 2
    y = top + (bottom - top - fitted.height) // 2
    canvas.alpha_composite(fitted, (x, y))


def recolor_wordmark(image: Image.Image, text_color: tuple[int, int, int, int]) -> Image.Image:
    """Recolor only the Paper letterforms; preserve exact alpha and Ready ribbon."""
    output = Image.new("RGBA", image.size)
    source = image.load()
    target = output.load()
    for y in range(image.height):
        for x in range(image.width):
            red, green, blue, alpha = source[x, y]
            if alpha == 0:
                continue
            color = READY if green > red * 1.3 else text_color
            target[x, y] = color[:3] + (alpha,)
    return output


def make_tile(mark: Image.Image, size: int) -> Image.Image:
    supersample = 4
    side = size * supersample
    canvas = Image.new("RGBA", (side, side))
    draw = ImageDraw.Draw(canvas)
    inset = round(side * 0.03)
    radius = round(side * 0.22)
    draw.rounded_rectangle((inset, inset, side - inset, side - inset), radius=radius, fill=PINE)
    padding = round(side * 0.105)
    paste_center(canvas, mark, (padding, padding, side - padding, side - padding))
    return canvas.resize((size, size), Image.Resampling.LANCZOS)


def make_unplated(mark: Image.Image, size: int) -> Image.Image:
    supersample = 4
    side = size * supersample
    canvas = Image.new("RGBA", (side, side))
    padding = round(side * 0.06)
    paste_center(canvas, mark, (padding, padding, side - padding, side - padding))
    return canvas.resize((size, size), Image.Resampling.LANCZOS)


def make_wordmark_panel(wordmark: Image.Image, background: tuple[int, int, int, int]) -> Image.Image:
    canvas = Image.new("RGBA", (2400, 800), background)
    paste_center(canvas, wordmark, (100, 100, 2300, 700))
    return canvas


def make_horizontal_lockup(
    mark: Image.Image,
    wordmark: Image.Image,
    background: tuple[int, int, int, int] | None,
) -> Image.Image:
    canvas = Image.new("RGBA", (2800, 800), background or (0, 0, 0, 0))
    paste_center(canvas, mark, (70, 70, 690, 730))
    word = contain(wordmark, 1980, 610)
    canvas.alpha_composite(word, (740, (canvas.height - word.height) // 2))
    return canvas


def make_stacked_lockup(
    mark: Image.Image,
    wordmark: Image.Image,
    background: tuple[int, int, int, int] | None,
) -> Image.Image:
    canvas = Image.new("RGBA", (1800, 1600), background or (0, 0, 0, 0))
    paste_center(canvas, mark, (470, 80, 1330, 930))
    word = contain(wordmark, 1580, 450)
    canvas.alpha_composite(word, ((canvas.width - word.width) // 2, 1040))
    return canvas


def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    candidates = [
        Path(r"C:\Windows\Fonts\SegUIVar.ttf"),
        Path(r"C:\Windows\Fonts\segoeuib.ttf" if bold else r"C:\Windows\Fonts\segoeui.ttf"),
    ]
    for candidate in candidates:
        if candidate.exists():
            return ImageFont.truetype(str(candidate), size=size)
    return ImageFont.load_default()


def make_brand_board(
    primary_mark: Image.Image,
    ready_mark: Image.Image,
    wordmark: Image.Image,
    hero: Image.Image,
) -> Image.Image:
    canvas = Image.new("RGBA", (2400, 1800), PINE)
    draw = ImageDraw.Draw(canvas)
    display_font = font(64, bold=True)
    title_font = font(48)
    body_font = font(30)
    label_font = font(22, bold=True)
    meta_font = font(24)

    primary = content_crop(primary_mark, 10)
    ready = content_crop(ready_mark, 10)
    signature = content_crop(wordmark, 10)

    draw.text((105, 62), "PRIMARY SIGNATURE", font=label_font, fill=MIST)
    paste_center(canvas, primary, (105, 105, 620, 575))
    word = contain(signature, 1600, 410)
    canvas.alpha_composite(word, (675, 105))
    draw.text((690, 475), "Catch now. Find it later.", font=display_font, fill=PAPER)
    draw.text((694, 555), "Quiet capture, kept on this computer.", font=body_font, fill=MIST)

    draw.text((105, 665), "STATE MARKS", font=label_font, fill=MIST)
    paste_center(canvas, ready, (105, 705, 475, 1080))
    paste_center(canvas, primary, (505, 705, 875, 1080))
    draw.text((144, 1093), "READY / SAVED", font=label_font, fill=READY)
    draw.text((540, 1093), "RECORDING", font=label_font, fill=RECORDING)

    draw.text((1020, 735), "One identity, two explicit states.", font=title_font, fill=PAPER)
    draw.text((1020, 810), "The pocket holds the capture. The orbit signals live recording.", font=body_font, fill=MIST)
    draw.text((1020, 862), "Colour changes with meaning; silhouette reinforces it without colour.", font=body_font, fill=MIST)
    draw.text((1020, 952), "Pocket + Orbit", font=display_font, fill=PAPER)
    draw.text((1024, 1035), "gesture  /  cursor  /  catch  /  local receipt  /  live state", font=meta_font, fill=MIST)

    swatches = [
        ("PINE", "#07130F", PINE),
        ("PAPER", "#F6F4EC", PAPER),
        ("READY", "#36E58C", READY),
        ("FOLD", "#2B6F63", FOLD),
        ("RECORD", "#FF5964", RECORDING),
        ("LINK", "#7AA7FF", LINK),
    ]
    swatch_left = 105
    swatch_top = 1190
    swatch_width = 335
    swatch_gap = 45
    for index, (name, value, color) in enumerate(swatches):
        x = swatch_left + index * (swatch_width + swatch_gap)
        draw.rounded_rectangle((x, swatch_top, x + swatch_width, swatch_top + 180), radius=28, fill=color)
        text_color = PAPER if name == "PINE" else PINE
        draw.text((x + 24, swatch_top + 25), name, font=label_font, fill=text_color)
        draw.text((x + 24, swatch_top + 108), value, font=body_font, fill=text_color)

    draw.text((105, 1450), "IMAGERY", font=label_font, fill=MIST)
    draw.text((105, 1495), "The catch field", font=title_font, fill=PAPER)
    draw.text((105, 1565), "A single gesture gathers screens, sound, text, and links", font=body_font, fill=MIST)
    draw.text((105, 1615), "into a local, recoverable orbit.", font=body_font, fill=MIST)
    paste_center(canvas, hero, (1510, 1385, 2295, 1755))
    return canvas


def export_manifest(source_paths: dict[str, Path], export_paths: list[Path]) -> None:
    def image_info(path: Path) -> dict[str, object]:
        with Image.open(path) as image:
            return {
                "path": path.relative_to(ROOT).as_posix(),
                "width": image.width,
                "height": image.height,
                "mode": image.mode,
            }

    manifest = {
        "sources": {role: image_info(path) for role, path in source_paths.items()},
        "exports": [image_info(path) for path in sorted(export_paths)],
    }
    (EXPORT / "brand-assets-manifest.json").write_text(
        json.dumps(manifest, indent=2) + "\n",
        encoding="utf-8",
    )


def main() -> None:
    EXPORT.mkdir(parents=True, exist_ok=True)

    primary_master = load_master(PRIMARY_SOURCE)
    ready_master = load_master(READY_SOURCE)
    wordmark_master = load_master(WORDMARK_SOURCE)
    hero_master = load_master(HERO_SOURCE)

    primary = content_crop(primary_master, 12)
    ready = content_crop(ready_master, 12)
    wordmark_light = content_crop(wordmark_master, 12)
    wordmark_dark = recolor_wordmark(wordmark_light, PINE)
    hero = content_crop(hero_master, 0)

    exported: list[Path] = []

    def png(image: Image.Image, filename: str) -> None:
        path = EXPORT / filename
        save_png(image, path)
        exported.append(path)

    # Exact approved masters, copied into the deliverables directory without
    # recoloring, redrawing, or geometry changes.
    png(primary_master, "logo-mark.png")
    png(ready_master, "logo-mark-ready.png")
    png(primary_master, "logo-mark-recording.png")
    png(wordmark_master, "wordmark-transparent.png")
    png(hero_master, "catch-field-transparent.png")

    png(make_wordmark_panel(wordmark_light, PINE), "wordmark-on-dark.png")
    png(make_wordmark_panel(wordmark_dark, PAPER), "wordmark-on-light.png")
    png(make_horizontal_lockup(primary, wordmark_light, None), "logo-lockup-transparent.png")
    png(make_horizontal_lockup(primary, wordmark_light, PINE), "logo-lockup-on-dark.png")
    png(make_horizontal_lockup(primary, wordmark_dark, PAPER), "logo-lockup-on-light.png")
    png(make_horizontal_lockup(ready, wordmark_light, PINE), "logo-lockup-ready-on-dark.png")
    png(make_stacked_lockup(primary, wordmark_light, None), "logo-stacked-transparent.png")
    png(make_stacked_lockup(primary, wordmark_light, PINE), "logo-stacked-on-dark.png")

    hero_on_dark = Image.new("RGBA", hero_master.size, PINE)
    hero_on_dark.alpha_composite(hero_master)
    png(hero_on_dark, "catch-field-on-dark.png")

    app_images: dict[int, Image.Image] = {}
    recording_images: dict[int, Image.Image] = {}
    for size in APP_SIZES:
        app = make_tile(ready, size)
        recording_app = make_tile(primary, size)
        png(app, f"app-icon-{size}.png")
        png(recording_app, f"app-icon-recording-{size}.png")
        app_images[size] = app
        recording_images[size] = recording_app

    ico_sizes = [(size, size) for size in (16, 20, 24, 32, 48, 64, 128, 256)]
    cursorpocket_ico = EXPORT / "CursorPocket.ico"
    recording_ico = EXPORT / "CursorPocket-recording.ico"
    app_images[1024].save(cursorpocket_ico, format="ICO", sizes=ico_sizes)
    recording_images[1024].save(recording_ico, format="ICO", sizes=ico_sizes)
    exported.extend((cursorpocket_ico, recording_ico))

    for size in TRAY_SIZES:
        png(make_unplated(ready, size), f"tray-ready-{size}.png")
        png(make_unplated(primary, size), f"tray-recording-{size}.png")

    png(make_brand_board(primary, ready, wordmark_light, hero), "brand-board.png")

    export_manifest(
        {
            "primary_mark": PRIMARY_SOURCE,
            "ready_mark": READY_SOURCE,
            "motion_wordmark": WORDMARK_SOURCE,
            "catch_field": HERO_SOURCE,
        },
        exported,
    )


if __name__ == "__main__":
    main()
