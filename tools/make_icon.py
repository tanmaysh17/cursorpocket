"""Write the app icons from the generated brand mark.

Both icons come straight from tools/make_logo.py rather than from a downscaled
raster, so each frame is rendered at its own size and the small ones fall back to
the cursor alone instead of turning into a green smudge.

Usage:  python tools/make_icon.py
Writes: assets/cursorpocket.ico, native/CursorPocket.App/Assets/AppIcon.ico
"""

from pathlib import Path

from make_logo import render_mark

SIZES = (256, 128, 64, 48, 40, 32, 24, 20, 16)


def write_ico(target: Path) -> None:
    target.parent.mkdir(parents=True, exist_ok=True)
    frames = [render_mark(size) for size in SIZES]
    frames[0].save(
        target,
        format="ICO",
        sizes=[(size, size) for size in SIZES],
        append_images=frames[1:],
    )
    print(f"Created {target}")


def main() -> None:
    root = Path(__file__).resolve().parents[1]
    write_ico(root / "assets" / "cursorpocket.ico")
    write_ico(root / "native" / "CursorPocket.App" / "Assets" / "AppIcon.ico")


if __name__ == "__main__":
    main()
