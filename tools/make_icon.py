from pathlib import Path

from PIL import Image


def main() -> None:
    assets = Path(__file__).resolve().parents[1] / "assets"
    target = assets / "cursorpocket.ico"
    target.parent.mkdir(parents=True, exist_ok=True)
    source = Image.open(assets / "cursorpocket-logo.png").convert("RGBA")
    bounds = source.getbbox()
    if bounds:
        source = source.crop(bounds)
    source.thumbnail((232, 232), Image.Resampling.LANCZOS)
    image = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
    image.alpha_composite(source, ((256 - source.width) // 2, (256 - source.height) // 2))
    image.save(target, format="ICO", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print(f"Created {target}")


if __name__ == "__main__":
    main()
