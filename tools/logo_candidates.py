"""Five brand-mark candidates for CursorPocket, defined once as path data.

Each candidate is a single XAML `Path.Data` string with a single fill -- the
constraint that makes a mark work everywhere the app draws one, from the 16 px
tray frame to the 11-dip settings chip, with no small-size fallback form and no
keyline. The same string is rasterised by tools/pathdata.py and pasted into
App.xaml on adoption, so the icon and the vector can never disagree.

Brief: say capture *and* consolidation in one shape; no cursor motif; confident
but quiet. This module is candidate exploration only -- it writes nothing and
is imported by tools/logo_contact_sheet.py.
"""

from __future__ import annotations

import math
from dataclasses import dataclass

from make_logo import GREEN, GROUND  # palette stays the single copy in make_logo

RED = (255, 95, 107, 255)  # App.xaml PocketRedColor / branding.RED

K = 0.5522847498307936  # cubic quarter-circle constant


def _f(value: float) -> str:
    text = f"{value:.2f}".rstrip("0").rstrip(".")
    return text if text else "0"


def rrect(x: float, y: float, w: float, h: float, r: float, clockwise: bool = True) -> str:
    """A rounded rectangle as one closed cubic subpath.

    `clockwise=False` reverses the winding, which is how a hole is cut under the
    nonzero fill rule -- safer than relying on `F0` surviving a XAML Style setter.
    """
    r = min(r, w / 2, h / 2)
    k = K * r
    points = [
        f"M{_f(x + r)},{_f(y)}",
        f"L{_f(x + w - r)},{_f(y)}",
        f"C{_f(x + w - r + k)},{_f(y)} {_f(x + w)},{_f(y + r - k)} {_f(x + w)},{_f(y + r)}",
        f"L{_f(x + w)},{_f(y + h - r)}",
        f"C{_f(x + w)},{_f(y + h - r + k)} {_f(x + w - r + k)},{_f(y + h)} {_f(x + w - r)},{_f(y + h)}",
        f"L{_f(x + r)},{_f(y + h)}",
        f"C{_f(x + r - k)},{_f(y + h)} {_f(x)},{_f(y + h - r + k)} {_f(x)},{_f(y + h - r)}",
        f"L{_f(x)},{_f(y + r)}",
        f"C{_f(x)},{_f(y + r - k)} {_f(x + r - k)},{_f(y)} {_f(x + r)},{_f(y)}",
        "Z",
    ]
    if not clockwise:
        return _reverse_subpath(" ".join(points))
    return " ".join(points)


def _reverse_subpath(subpath: str) -> str:
    """Reverse one M...Z subpath, flipping its winding."""
    import re

    tokens = re.findall(r"[MLCZ]|-?\d*\.?\d+", subpath)
    commands: list[tuple[str, list[float]]] = []
    i = 0
    while i < len(tokens):
        op = tokens[i]
        i += 1
        count = {"M": 2, "L": 2, "C": 6, "Z": 0}[op]
        args = [float(tokens[i + j]) for j in range(count)]
        i += count
        commands.append((op, args))

    # Collect the on-curve points and the control pairs between them.
    anchors: list[tuple[float, float]] = []
    controls: dict[int, tuple[float, float, float, float]] = {}
    for op, args in commands:
        if op in ("M", "L"):
            anchors.append((args[0], args[1]))
        elif op == "C":
            controls[len(anchors) - 1] = (args[0], args[1], args[2], args[3])
            anchors.append((args[4], args[5]))

    out = [f"M{_f(anchors[-1][0])},{_f(anchors[-1][1])}"]
    for index in range(len(anchors) - 2, -1, -1):
        if index in controls:
            c1x, c1y, c2x, c2y = controls[index]
            out.append(
                f"C{_f(c2x)},{_f(c2y)} {_f(c1x)},{_f(c1y)} {_f(anchors[index][0])},{_f(anchors[index][1])}"
            )
        else:
            out.append(f"L{_f(anchors[index][0])},{_f(anchors[index][1])}")
    out.append("Z")
    return " ".join(out)


def capsule(x: float, y: float, w: float, h: float, clockwise: bool = True) -> str:
    return rrect(x, y, w, h, h / 2, clockwise=clockwise)


@dataclass(frozen=True)
class Candidate:
    key: str
    title: str
    note: str
    data: str  # complete path data; holes are opposite-wound, no F0 needed


def build_bracket(weight: float = 16.0, arm: float = 42.0) -> Candidate:
    """Two opposed crop-mark corners. The implied rectangle is the library."""
    w, a = weight, arm

    def corner_tl() -> str:
        return (
            f"M0,0 L{_f(a)},0 L{_f(a)},{_f(w)} L{_f(w)},{_f(w)} "
            f"L{_f(w)},{_f(a)} L0,{_f(a)} Z"
        )

    def corner_br() -> str:
        return (
            f"M100,100 L{_f(100 - a)},100 L{_f(100 - a)},{_f(100 - w)} L{_f(100 - w)},{_f(100 - w)} "
            f"L{_f(100 - w)},{_f(100 - a)} L100,{_f(100 - a)} Z"
        )

    return Candidate(
        key="bracket",
        title="Bracket",
        note="Crop marks; the space between them is the collected library",
        data=f"{corner_tl()} {corner_br()}",
    )


def build_notch(radius: float = 24.0, mouth_w: float = 34.0, mouth_d: float = 26.0) -> Candidate:
    """A solid squircle with a mouth cut into the top edge: pocket and inbox."""
    r, mw, md = radius, mouth_w, mouth_d
    k = K * r
    left = (100 - mw) / 2
    right = left + mw
    inner_r = 7.0
    ik = K * inner_r
    points = [
        f"M{_f(r)},0",
        f"L{_f(left - inner_r)},0",
        # ease into the mouth
        f"C{_f(left - inner_r + ik)},0 {_f(left)},{_f(inner_r - ik)} {_f(left)},{_f(inner_r)}",
        f"L{_f(left)},{_f(md - inner_r)}",
        f"C{_f(left)},{_f(md - inner_r + ik)} {_f(left + inner_r - ik)},{_f(md)} {_f(left + inner_r)},{_f(md)}",
        f"L{_f(right - inner_r)},{_f(md)}",
        f"C{_f(right - inner_r + ik)},{_f(md)} {_f(right)},{_f(md - inner_r + ik)} {_f(right)},{_f(md - inner_r)}",
        f"L{_f(right)},{_f(inner_r)}",
        f"C{_f(right)},{_f(inner_r - ik)} {_f(right + inner_r - ik)},0 {_f(right + inner_r)},0",
        f"L{_f(100 - r)},0",
        f"C{_f(100 - r + k)},0 100,{_f(r - k)} 100,{_f(r)}",
        f"L100,{_f(100 - r)}",
        f"C100,{_f(100 - r + k)} {_f(100 - r + k)},100 {_f(100 - r)},100",
        f"L{_f(r)},100",
        f"C{_f(r - k)},100 0,{_f(100 - r + k)} 0,{_f(100 - r)}",
        f"L0,{_f(r)}",
        f"C0,{_f(r - k)} {_f(r - k)},0 {_f(r)},0",
        "Z",
    ]
    return Candidate(
        key="notch",
        title="Notch",
        note="A pocket with its mouth open; things drop in from the screen",
        data=" ".join(points),
    )


def build_slot(radius: float = 24.0, slot_w: float = 52.0, slot_h: float = 16.0, slot_y: float = 30.0) -> Candidate:
    """A solid squircle with one capsule aperture. The slot is the identity."""
    body = rrect(0, 0, 100, 100, radius)
    hole = capsule((100 - slot_w) / 2, slot_y, slot_w, slot_h, clockwise=False)
    return Candidate(
        key="slot",
        title="Slot",
        note="A mail slot; every capture goes through the same opening",
        data=f"{body} {hole}",
    )


def build_converge(
    bar_y: float = 78.0,
    bar_h: float = 20.0,
    stroke: float = 19.0,
    tops: tuple[tuple[float, float], ...] = ((16.0, 10.0), (50.0, 0.0), (84.0, 10.0)),
    meet_y: float = 68.0,
) -> Candidate:
    """Streams of different origins landing in one bar. The consolidation story."""
    bar = rrect(6, bar_y, 88, bar_h, bar_h / 2)
    strokes = []
    for top_x, top_y in tops:
        # each stream leans toward the centre as it falls
        bottom_x = top_x + (50.0 - top_x) * 0.42
        half = stroke / 2
        strokes.append(
            f"M{_f(top_x - half)},{_f(top_y)} L{_f(top_x + half)},{_f(top_y)} "
            f"L{_f(bottom_x + half)},{_f(meet_y)} L{_f(bottom_x - half)},{_f(meet_y)} Z"
        )
    return Candidate(
        key="converge",
        title="Converge",
        note="Three kinds of capture becoming one reviewable place",
        data=" ".join(strokes) + " " + bar,
    )


def build_framefill(weight: float = 18.0, radius: float = 24.0, block: float = 38.0) -> Candidate:
    """A selection frame whose corner has already filled in with the result."""
    outer = rrect(0, 0, 100, 100, radius)
    inner = rrect(weight, weight, 100 - 2 * weight, 100 - 2 * weight, max(6.0, radius - weight), clockwise=False)
    solid = rrect(100 - weight - block, 100 - weight - block, block, block, 9.0)
    return Candidate(
        key="framefill",
        title="Frame + fill",
        note="The act of selecting and the captured result in one shape",
        data=f"{outer} {inner} {solid}",  # order matters: outer, its hole, then the island
    )


def candidates() -> tuple[Candidate, ...]:
    return (
        build_bracket(),
        build_notch(),
        build_slot(),
        build_converge(),
        build_framefill(),
    )


def xaml_data(candidate: Candidate) -> str:
    """The exact string for App.xaml. Holes are opposite-wound, so no F0 token."""
    return candidate.data


def svg_d(candidate: Candidate) -> str:
    """The same data for an SVG harness; winding holes work under nonzero there too."""
    return candidate.data
