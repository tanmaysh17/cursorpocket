"""Verify the pinned segmentation model against what SelfieSegmenter assumes.

Three assumptions in `CursorPocket.App/Services/SelfieSegmenter.cs` and
`CursorPocket.Core/Media/SegmentationPreprocessor.cs` cannot be covered by the
unit tests, because those run against a fake `IPersonMaskModel`:

  1. the tensor layout is NCHW, not NHWC,
  2. the input is RGB rescaled to 0..1, with no mean/std normalization,
  3. the output is a probability where **high means person**.

Get the polarity backwards and background blur blurs the person instead, which
looks like a working feature right up until someone watches the recording. This
checks all three against the real file, so a model swap cannot silently invert
the effect.

A synthetic head-and-shoulders figure stands in for a camera frame. A flat
ellipse is not a convincing person, so the absolute mask values stay low; what
matters is that the response concentrates on the figure by a wide margin rather
than on the background.

    py -m tools.verify_segmentation_model

Requires `onnxruntime` and `numpy`, which are not CursorPocket runtime
dependencies -- this is a development gate, not part of the app.
"""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
import onnxruntime as ort

INPUT_SIZE = 256
#: The figure has to out-respond the background by at least this ratio. Set well
#: below what a correct model produces (~140x) and far above the ~1x an inverted
#: or mis-fed model would give.
MINIMUM_RATIO = 10.0


def default_model_path() -> Path:
    return Path(__file__).resolve().parent.parent / "third_party" / "models" / "selfie_segmenter.onnx"


def _synthetic_frame() -> tuple[np.ndarray, np.ndarray]:
    """A skin-toned head and shoulders on a cool background, plus its own mask."""
    frame = np.zeros((INPUT_SIZE, INPUT_SIZE, 3), dtype=np.uint8)
    frame[:, :] = (40, 55, 75)
    rows, columns = np.mgrid[0:INPUT_SIZE, 0:INPUT_SIZE]
    head = ((columns - 128) ** 2) / 46**2 + ((rows - 96) ** 2) / 58**2 <= 1
    shoulders = ((columns - 128) ** 2) / 104**2 + ((rows - 250) ** 2) / 92**2 <= 1
    figure = head | shoulders
    frame[figure] = (196, 152, 128)
    return frame, figure


def main() -> int:
    parser = argparse.ArgumentParser(description="Verify the CursorPocket segmentation model")
    parser.add_argument("--model", type=Path, default=default_model_path())
    arguments = parser.parse_args()
    model = arguments.model.resolve()
    if not model.exists():
        raise RuntimeError(f"Model not found: {model}. Run tools/fetch_models.ps1 first.")

    session = ort.InferenceSession(str(model), providers=["CPUExecutionProvider"])
    model_input = session.get_inputs()[0]
    model_output = session.get_outputs()[0]

    # 1. Layout. SegmentationPreprocessor writes channels-first when the second
    #    dimension is 3, so that is what the model has to report.
    if list(model_input.shape[1:]) != [3, INPUT_SIZE, INPUT_SIZE]:
        raise RuntimeError(f"Expected NCHW [_, 3, {INPUT_SIZE}, {INPUT_SIZE}], got {model_input.shape}")
    print(f"PASS layout: {model_input.name} {model_input.shape} is NCHW")

    frame, figure = _synthetic_frame()
    rgb = frame.astype(np.float32) / 255.0
    tensor = np.transpose(rgb, (2, 0, 1))[None, ...].copy()
    mask = np.squeeze(session.run([model_output.name], {model_input.name: tensor})[0]).astype(np.float32)
    if mask.shape != (INPUT_SIZE, INPUT_SIZE):
        raise RuntimeError(f"Expected a single {INPUT_SIZE}x{INPUT_SIZE} plane, got {mask.shape}")

    # 2. Range. SelfieSegmenter applies a sigmoid only when values fall outside
    #    0..1; this model emits probabilities, so it must not need one.
    low, high = float(mask.min()), float(mask.max())
    if low < -0.01 or high > 1.01:
        raise RuntimeError(f"Expected probabilities in 0..1, got [{low:+.3f}, {high:+.3f}] (logits?)")
    print(f"PASS range: output in [{low:.3f}, {high:.3f}], no sigmoid needed")

    # 3. Polarity. High has to mean person.
    on_figure = float(mask[figure].mean())
    on_background = float(mask[~figure].mean())
    ratio = on_figure / on_background if on_background > 0 else float("inf")
    peak_row, peak_column = np.unravel_index(int(mask.argmax()), mask.shape)
    if not bool(figure[peak_row, peak_column]):
        raise RuntimeError(f"Strongest response at ({peak_row}, {peak_column}) is outside the figure")
    if ratio < MINIMUM_RATIO:
        raise RuntimeError(
            f"Figure/background ratio {ratio:.1f} is below {MINIMUM_RATIO}; "
            "the mask may be inverted or the input mis-fed"
        )
    print(
        f"PASS polarity: figure {on_figure:.6f} vs background {on_background:.6f} "
        f"({ratio:.0f}x), peak inside the figure -- high means person"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
