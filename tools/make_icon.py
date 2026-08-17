from pathlib import Path

from PIL import Image, ImageDraw


def main() -> None:
    target = Path(__file__).resolve().parents[1] / "assets" / "cursorpocket.ico"
    target.parent.mkdir(parents=True, exist_ok=True)
    image = Image.new("RGBA", (256, 256), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle((18, 18, 238, 238), radius=64, fill="#18212D")
    draw.rounded_rectangle((25, 25, 231, 231), radius=58, outline="#2D3A4B", width=5)
    draw.ellipse((63, 63, 193, 193), fill="#0B1119", outline="#2D3A4B", width=10)
    draw.ellipse((96, 96, 160, 160), fill="#42D392")
    draw.ellipse((174, 43, 220, 89), fill="#FF5D68", outline="#10151D", width=7)
    image.save(target, format="ICO", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print(f"Created {target}")


if __name__ == "__main__":
    main()
