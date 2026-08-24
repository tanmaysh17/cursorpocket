from __future__ import annotations

import json
import struct
from io import BytesIO
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
BRAND = ROOT / "assets" / "brand"
IMAGERY = BRAND / "imagery"
EXPORT = BRAND / "export"
NATIVE_ASSETS = ROOT / "native" / "CursorPocket.App" / "Assets"

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
    buffer = BytesIO()
    image.save(buffer, "PNG", optimize=True)
    write_bytes_if_changed(path, buffer.getvalue())


def write_bytes_if_changed(path: Path, payload: bytes) -> None:
    if path.exists() and path.read_bytes() == payload:
        return
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_bytes(payload)
    temporary.replace(path)


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
    # Installed identity surfaces should use the full Windows icon canvas. Keep
    # only a sub-pixel antialiasing guard instead of the old 6% optical inset.
    padding = max(1, round(side * 0.02))
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


def font(size: int, bold: bool = False) -> ImageFont.FreeTypeFont:
    del bold  # The approved board uses the same Segoe UI Variable source at every weight.
    source = Path(r"C:\Windows\Fonts\SegUIVar.ttf")
    if not source.exists():
        raise FileNotFoundError(
            "CursorPocket brand exports require C:\\Windows\\Fonts\\SegUIVar.ttf; "
            "refusing a platform-dependent fallback font."
        )
    return ImageFont.truetype(str(source), size=size)


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


def make_splash(ready_mark: Image.Image, wordmark: Image.Image) -> Image.Image:
    canvas = Image.new("RGBA", (1240, 600), PINE)
    paste_center(canvas, ready_mark, (90, 90, 510, 510))
    paste_center(canvas, wordmark, (505, 140, 1160, 460))
    return canvas


def make_wide_tile(ready_mark: Image.Image, wordmark: Image.Image) -> Image.Image:
    canvas = Image.new("RGBA", (620, 300), PINE)
    paste_center(canvas, ready_mark, (30, 30, 270, 270))
    paste_center(canvas, wordmark, (260, 72, 590, 228))
    return canvas


def write_ico(frame_paths: dict[int, Path], path: Path) -> None:
    """Write a PNG-backed ICO without resampling any reviewed per-size frame."""
    path.parent.mkdir(parents=True, exist_ok=True)
    frames: list[tuple[int, bytes]] = []
    for size, frame_path in sorted(frame_paths.items()):
        with Image.open(frame_path) as image:
            if image.size != (size, size):
                raise ValueError(f"ICO frame has the wrong dimensions: {frame_path}")
        frames.append((size, frame_path.read_bytes()))

    header = struct.pack("<HHH", 0, 1, len(frames))
    offset = len(header) + len(frames) * 16
    entries: list[bytes] = []
    payloads: list[bytes] = []
    for size, payload in frames:
        dimension = 0 if size == 256 else size
        entries.append(
            struct.pack(
                "<BBBBHHII",
                dimension,
                dimension,
                0,
                0,
                1,
                32,
                len(payload),
                offset,
            )
        )
        payloads.append(payload)
        offset += len(payload)
    write_bytes_if_changed(path, header + b"".join(entries) + b"".join(payloads))


def make_tray_primary(size: int) -> Image.Image:
    """Rasterize brand logo #1 at one exact tray size with a transparent cursor."""
    supersample = 8
    render = size * supersample
    scale = render / 24
    mark = Image.new("RGBA", (render, render))
    draw = ImageDraw.Draw(mark)

    def point(x: float, y: float) -> tuple[float, float]:
        return x * scale, y * scale

    draw.ellipse((*point(4, 5), *point(18, 19)), outline=FOLD, width=round(3 * scale))
    draw.arc((*point(4, 5), *point(18, 19)), start=135, end=315, fill=READY, width=round(3 * scale))
    draw.ellipse((*point(7.9, 8.9), *point(14.1, 15.1)), fill=RECORDING)

    alpha = mark.getchannel("A")
    cutout = ImageDraw.Draw(alpha)
    cutout.polygon(
        [point(9.5, 7.5), point(9.5, 15.5), point(11.5, 13.5), point(14.5, 18.5), point(16.2, 17.5), point(13.2, 12.5), point(16, 12.5)],
        fill=0,
    )
    mark.putalpha(alpha)

    draw = ImageDraw.Draw(mark)
    tick_width = round(1.6 * scale)
    for start, end in [((17, 3), (17, 5)), ((20, 4), (18.6, 5.4)), ((21, 7), (19, 7))]:
        draw.line([point(*start), point(*end)], fill=RECORDING, width=tick_width)
    # The geometry above is authored on a 24 px reference canvas. Crop its
    # optical bounds and refit them so Windows' 16 px tray slot is fully used.
    crop = content_crop(mark, max(1, round(scale * 0.2)))
    fitted = contain(crop, render, render)
    canvas = Image.new("RGBA", (render, render))
    canvas.alpha_composite(fitted, ((render - fitted.width) // 2, (render - fitted.height) // 2))
    return canvas.resize((size, size), Image.Resampling.LANCZOS)


def sync_native_runtime_assets(
    primary_mark: Image.Image,
    wordmark: Image.Image,
    app_icon: Path,
    recording_app_icon: Path,
    tray_ready_icon: Path,
    tray_recording_icon: Path,
) -> list[Path]:
    """Stage every image the WinUI build, installer, Start, and tray consume."""
    NATIVE_ASSETS.mkdir(parents=True, exist_ok=True)
    runtime: dict[str, Image.Image] = {
        "CursorPocketLogo.png": make_unplated(primary_mark, 256),
        "LockScreenLogo.scale-200.png": make_unplated(primary_mark, 48),
        "SplashScreen.scale-200.png": make_splash(primary_mark, wordmark),
        "Square150x150Logo.scale-200.png": make_unplated(primary_mark, 300),
        "Square44x44Logo.scale-200.png": make_unplated(primary_mark, 88),
        "Square44x44Logo.targetsize-24_altform-unplated.png": make_unplated(primary_mark, 24),
        "Square44x44Logo.targetsize-48_altform-lightunplated.png": make_unplated(primary_mark, 48),
        "StoreLogo.png": make_unplated(primary_mark, 50),
        "Wide310x150Logo.scale-200.png": make_wide_tile(primary_mark, wordmark),
    }
    written: list[Path] = []
    for filename, image in runtime.items():
        path = NATIVE_ASSETS / filename
        save_png(image, path)
        written.append(path)

    icon_sources = {
        "AppIcon.ico": app_icon,
        "AppIconRecording.ico": recording_app_icon,
        "TrayReady.ico": tray_ready_icon,
        "TrayRecording.ico": tray_recording_icon,
    }
    for filename, source in icon_sources.items():
        target = NATIVE_ASSETS / filename
        write_bytes_if_changed(target, source.read_bytes())
        written.append(target)
    return written


def export_manifest(
    source_paths: dict[str, Path],
    export_paths: list[Path],
    runtime_paths: list[Path],
) -> None:
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
        "runtime": [image_info(path) for path in sorted(runtime_paths)],
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

    def png(image: Image.Image, filename: str) -> Path:
        path = EXPORT / filename
        save_png(image, path)
        exported.append(path)
        return path

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

    app_frames: dict[int, Path] = {}
    recording_frames: dict[int, Path] = {}
    for size in APP_SIZES:
        # Brand logo #1 is the installed application identity in every state.
        # Preserve its outer field and negative-space cursor as transparency.
        app = make_unplated(primary, size)
        recording_app = make_unplated(primary, size)
        app_frames[size] = png(app, f"app-icon-{size}.png")
        recording_frames[size] = png(recording_app, f"app-icon-recording-{size}.png")

    ico_sizes = (16, 20, 24, 32, 48, 64, 128, 256)
    cursorpocket_ico = EXPORT / "CursorPocket.ico"
    recording_ico = EXPORT / "CursorPocket-recording.ico"
    write_ico({size: app_frames[size] for size in ico_sizes}, cursorpocket_ico)
    write_ico({size: recording_frames[size] for size in ico_sizes}, recording_ico)
    exported.extend((cursorpocket_ico, recording_ico))

    tray_ready_frames: dict[int, Path] = {}
    tray_recording_frames: dict[int, Path] = {}
    for size in TRAY_SIZES:
        # Tooltips carry state; both resources retain logo #1 as the tray identity.
        tray_ready_frames[size] = png(make_tray_primary(size), f"tray-ready-{size}.png")
        tray_recording_frames[size] = png(make_tray_primary(size), f"tray-recording-{size}.png")

    tray_ready_ico = EXPORT / "CursorPocket-tray-ready.ico"
    tray_recording_ico = EXPORT / "CursorPocket-tray-recording.ico"
    write_ico(tray_ready_frames, tray_ready_ico)
    write_ico(tray_recording_frames, tray_recording_ico)
    exported.extend((tray_ready_ico, tray_recording_ico))

    png(make_brand_board(primary, ready, wordmark_light, hero), "brand-board.png")

    runtime = sync_native_runtime_assets(
        primary,
        wordmark_light,
        cursorpocket_ico,
        recording_ico,
        tray_ready_ico,
        tray_recording_ico,
    )

    export_manifest(
        {
            "primary_mark": PRIMARY_SOURCE,
            "ready_mark": READY_SOURCE,
            "motion_wordmark": WORDMARK_SOURCE,
            "catch_field": HERO_SOURCE,
        },
        exported,
        runtime,
    )


if __name__ == "__main__":
    main()
