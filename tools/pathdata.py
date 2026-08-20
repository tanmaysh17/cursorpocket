"""A small XAML/SVG path-data reader, so a mark can be authored once.

The brand mark has to exist twice: as a `Path.Data` string in App.xaml and as a
raster in the .ico. Today those are two hand-kept copies of the same geometry
with nothing checking they agree. This module makes the string the single source
and rasterises *that*, so the icon is provably a rendering of the literal text
the app draws.

Supports the subset the mark needs: `F0`/`F1`, `M m L l H h V v C c Q q Z z`, and
implicit command repetition. Elliptical arcs are deliberately absent -- every
rounded corner here is a cubic, which is closer to a squircle anyway.

Not a general SVG implementation; it exists to keep two files honest.
"""

from __future__ import annotations

import re

from PIL import Image, ImageChops, ImageDraw

# Cubic control-point offset that approximates a quarter circle of radius 1.
KAPPA = 0.5522847498307936

CURVE_STEPS = 24  # flattening resolution; well past visible at any icon size

_TOKEN = re.compile(r"[A-Za-z]|-?\d*\.?\d+(?:[eE][-+]?\d+)?")

Point = tuple[float, float]
Subpath = list[Point]


def _cubic(p0: Point, p1: Point, p2: Point, p3: Point) -> list[Point]:
    points = []
    for step in range(1, CURVE_STEPS + 1):
        t = step / CURVE_STEPS
        u = 1.0 - t
        points.append(
            (
                u * u * u * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t * t * t * p3[0],
                u * u * u * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t * t * t * p3[1],
            )
        )
    return points


def parse(data: str) -> tuple[bool, list[Subpath]]:
    """Return (even_odd, subpaths) with every curve flattened to a polyline."""
    tokens = _TOKEN.findall(data)
    even_odd = False
    index = 0
    # A leading fill rule tokenises as two tokens, e.g. ['F', '0'].
    if tokens and tokens[0] in ("F", "f") and len(tokens) > 1 and tokens[1] in ("0", "1"):
        even_odd = tokens[1] == "0"
        index = 2

    subpaths: list[Subpath] = []
    current: Subpath = []
    cursor: Point = (0.0, 0.0)
    start: Point = (0.0, 0.0)
    command = ""

    def number() -> float:
        nonlocal index
        value = float(tokens[index])
        index += 1
        return value

    while index < len(tokens):
        token = tokens[index]
        if token.isalpha():
            command = token
            index += 1
            if command in ("Z", "z"):
                if current:
                    subpaths.append(current)
                    current = []
                cursor = start
                continue
        elif not command:
            raise ValueError(f"path data starts with a number: {data[:32]!r}")

        relative = command.islower()
        op = command.upper()

        if op == "M":
            x, y = number(), number()
            cursor = (cursor[0] + x, cursor[1] + y) if relative else (x, y)
            if current:
                subpaths.append(current)
            current = [cursor]
            start = cursor
            # A repeated coordinate pair after M is an implicit L.
            command = "l" if relative else "L"
        elif op == "L":
            x, y = number(), number()
            cursor = (cursor[0] + x, cursor[1] + y) if relative else (x, y)
            current.append(cursor)
        elif op == "H":
            x = number()
            cursor = (cursor[0] + x, cursor[1]) if relative else (x, cursor[1])
            current.append(cursor)
        elif op == "V":
            y = number()
            cursor = (cursor[0], cursor[1] + y) if relative else (cursor[0], y)
            current.append(cursor)
        elif op == "C":
            raw = [(number(), number()) for _ in range(3)]
            if relative:
                raw = [(cursor[0] + x, cursor[1] + y) for x, y in raw]
            current.extend(_cubic(cursor, raw[0], raw[1], raw[2]))
            cursor = raw[2]
        elif op == "Q":
            raw = [(number(), number()) for _ in range(2)]
            if relative:
                raw = [(cursor[0] + x, cursor[1] + y) for x, y in raw]
            control, end = raw
            # Raise the quadratic to an equivalent cubic.
            c1 = (cursor[0] + 2 / 3 * (control[0] - cursor[0]), cursor[1] + 2 / 3 * (control[1] - cursor[1]))
            c2 = (end[0] + 2 / 3 * (control[0] - end[0]), end[1] + 2 / 3 * (control[1] - end[1]))
            current.extend(_cubic(cursor, c1, c2, end))
            cursor = end
        else:
            raise ValueError(f"unsupported path command {command!r}")

    if current:
        subpaths.append(current)
    return even_odd, [sub for sub in subpaths if len(sub) >= 3]


def bbox(subpaths: list[Subpath]) -> tuple[float, float, float, float]:
    xs = [x for sub in subpaths for x, _ in sub]
    ys = [y for sub in subpaths for _, y in sub]
    return min(xs), min(ys), max(xs), max(ys)


def fit(subpaths: list[Subpath], box: tuple[float, float, float, float]) -> list[Subpath]:
    """Scale uniformly and centre into `box`. The Python model of Stretch="Uniform"."""
    left, top, right, bottom = box
    x0, y0, x1, y1 = bbox(subpaths)
    width = max(x1 - x0, 1e-9)
    height = max(y1 - y0, 1e-9)
    scale = min((right - left) / width, (bottom - top) / height)
    offset_x = left + ((right - left) - width * scale) / 2
    offset_y = top + ((bottom - top) - height * scale) / 2
    return [
        [((x - x0) * scale + offset_x, (y - y0) * scale + offset_y) for x, y in sub]
        for sub in subpaths
    ]


def signed_area(subpath: Subpath) -> float:
    """Shoelace area; the sign carries the winding direction."""
    total = 0.0
    for index, (x, y) in enumerate(subpath):
        next_x, next_y = subpath[(index + 1) % len(subpath)]
        total += x * next_y - next_x * y
    return total / 2.0


def fill_mask(subpaths: list[Subpath], size: tuple[int, int], even_odd: bool) -> Image.Image:
    """Coverage mask for the filled path.

    PIL has neither fill rules nor multi-subpath polygons, so each subpath is
    filled into its own mask and the masks are folded. On 0/255 values
    `difference` is exactly XOR, which is what even-odd means. For nonzero the
    winding direction decides: subpaths wound with the majority add, subpaths
    wound against it cut holes -- which is how the mark's apertures are encoded
    so that App.xaml needs no F0 token.
    """
    mask = Image.new("L", size, 0)
    if even_odd:
        for sub in subpaths:
            layer = Image.new("L", size, 0)
            ImageDraw.Draw(layer).polygon(sub, fill=255)
            mask = ImageChops.difference(mask, layer)
        return mask

    areas = [signed_area(sub) for sub in subpaths]
    dominant = max(areas, key=abs) if areas else 0.0
    # Fold in document order so an island inside a hole (outer, hole, island)
    # survives, as it does under true nonzero winding.
    for sub, area in zip(subpaths, areas):
        layer = Image.new("L", size, 0)
        ImageDraw.Draw(layer).polygon(sub, fill=255)
        if area * dominant >= 0:
            mask = ImageChops.lighter(mask, layer)
        else:
            mask = ImageChops.subtract(mask, layer)
    return mask


def render(
    data: str,
    size: int,
    fill: tuple[int, int, int, int],
    box: tuple[float, float, float, float] | None = None,
    supersample: int = 4,
) -> Image.Image:
    """Rasterise `data` as a flat fill on a transparent square of `size` px."""
    supersample = max(1, min(supersample, 8192 // max(size, 1)))
    render_size = size * supersample
    even_odd, subpaths = parse(data)
    target = box or (0.0, 0.0, float(render_size), float(render_size))
    if box is not None:
        target = tuple(value * supersample for value in box)
    placed = fit(subpaths, target)
    mask = fill_mask(placed, (render_size, render_size), even_odd)
    canvas = Image.new("RGBA", (render_size, render_size), (0, 0, 0, 0))
    canvas.paste(fill, (0, 0), mask)
    return canvas.resize((size, size), Image.Resampling.LANCZOS)


def min_stroke(data: str) -> float:
    """Rough thinnest-feature probe, in the path's own units.

    Erodes the filled mask one pixel per pass until *any* part of it vanishes,
    which catches thin strokes wherever they are rather than trusting the
    author's declared weight. A feature survives N erosions when it is roughly
    2N+1 px across, so the first pass that loses area names the thinnest one.
    """
    from PIL import ImageFilter

    even_odd, subpaths = parse(data)
    x0, y0, x1, y1 = bbox(subpaths)
    span = max(x1 - x0, y1 - y0)
    probe = 200
    # MinFilter replicates edge pixels, so anything touching the canvas border
    # would never erode; keep the shape well inside it.
    margin = 60
    placed = fit(subpaths, (float(margin), float(margin), float(probe - margin), float(probe - margin)))
    scale = (probe - 2 * margin) / probe
    mask = fill_mask(placed, (probe, probe), even_odd).point(lambda p: 255 if p >= 128 else 0)
    total = sum(1 for value in mask.tobytes() if value)
    eroded = mask
    for passes in range(1, 60):
        eroded = eroded.filter(ImageFilter.MinFilter(3))
        # Opening: whatever dilation cannot restore was thinner than the kernel.
        reopened = eroded
        for _ in range(passes):
            reopened = reopened.filter(ImageFilter.MaxFilter(3))
        survived = sum(1 for value in reopened.tobytes() if value)
        if total - survived > total * 0.02:
            return (passes * 2) * span / (probe - 2 * margin)
    return span
