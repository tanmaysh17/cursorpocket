# CursorPocket brand system

Status: approved visual direction; canonical raster masters and production export kit rebuilt for application migration.

## Brand idea

CursorPocket is a quiet local capture reflex: catch something without leaving the work, know when recording is live, and always know where the result went. The identity is built around **Pocket + Orbit**.

- The **pocket** is the default mark. A green capture orbit drops into a teal pocket while the cursor stays inside the captured field. It means ready, saved, local, and recoverable.
- The **orbit** is the recording mark. The structure opens into motion around a coral live disc, with three signal ticks as a non-colour state cue.
- The cursor in the recording mark is **negative space**. It is never teal, green, white, or coral; the background must show through the whole cursor silhouette.
- The motion line under the wordmark echoes the app's two-circle mouse gesture and ends in a square capture token.

The working brand line is **Catch now. Find it later.** Supporting copy is **Quiet capture, kept on this computer.** These are campaign and onboarding lines, not persistent UI decoration.

## Logo system

### Primary signature

The canonical main app logo is `assets/brand/main-logo.png`: the green-and-teal capture orbit around a coral focal disc and a fully transparent negative-space cursor, with three coral signal ticks. Use this mark for the primary application identity. When the `CursorPocket` name is required beside it, pair it with the existing motion wordmark; prefer the reversed lockup on deep-pine or black surfaces.

### Secondary pocket mark

`assets/brand/brand-logo-02-pocket-v3-transparent.png` is brand logo #2: a ready-green capture loop tucked behind a simple single-panel teal pocket, with the cursor held inside the captured field. Its pocket and return loop form one uninterrupted flowing upper boundary—never a sharp peak, separate raised lip, rim, or edge stripe. The transparent master is the reusable asset; `brand-logo-02-pocket-v3.png` retains the deep-pine presentation ground. Use it when the local, kept, and recoverable side of CursorPocket should lead.

### Motion wordmark

`assets/brand/brand-logo-03-wordmark-transparent.png` is the high-resolution transparent reversed wordmark for dark surfaces. The lower terminal of the `C` becomes the ready-green gesture ribbon as one continuous curve, passes through two mouse-gesture loops, and ends in the square capture token. Do not detach the underline from the `C` or introduce a kink at the colour handoff.

### State marks

| State | Mark | Required reading |
| --- | --- | --- |
| Ready, idle, saved | Pocket mark | Green orbit entering a teal pocket |
| Starting, recording, finalizing | Recording orbit | Coral live disc, three coral ticks, cursor cut out as background |

Do not put a red badge on the ready mark. The recording mark is a distinct silhouette so state survives grayscale and common colour-vision differences.

### Clear space and minimum size

- Clear space around a standalone mark: at least one quarter of its rendered width.
- Clear space around a lockup: at least the height of the square endpoint on the motion line.
- Full-colour mark minimum: 24 px.
- At 16–20 px, use the dedicated tray exports rather than downscaling the 1024 px application tile.
- The full wordmark should not be rendered below 150 px wide.

### Never

- Do not fill the recording cursor; it is negative space.
- Do not darken the teal fold until it disappears against the ground.
- Do not recolour capture kinds. Kind is communicated by glyph, not hue.
- Do not add glass, bevel, gloss, drop shadow, lens flare, or a badge to either state mark.
- Do not place the marks over busy photography without an opaque pine or paper holding field.

## Colour

| Name | Hex | Role |
| --- | --- | --- |
| Pine | `#07130F` | Primary dark ground, dark wordmark, negative-space reading |
| Paper | `#F6F4EC` | Warm light ground and primary text on dark |
| Ready | `#36E58C` | Brand motion, ready/saved, one primary action, active capture field |
| Fold | `#2B6F63` | Ribbon backside and pocket; deliberately lighter than the old near-black face |
| Recording | `#FF5964` | Live recording, discard, destructive action, signal ticks |
| Link | `#7AA7FF` | Informational and link emphasis only |
| Ink dim | `#CBD7D1` | Secondary text on dark |
| Mist | `#8EA099` | Tertiary metadata |

Green and coral are semantic, not decorative. A full interface should never become a green or coral wash.

## Typography

- Brand and product display: Segoe UI Variable Display, 650–700 weight, tight optical spacing.
- Interface: Segoe UI Variable Text.
- Timers, paths, sizes, and shortcuts: Cascadia Mono.
- The generated motion wordmark may be used as artwork. Never rebuild it by typing ordinary text and adding a random underline.

## Product glyphs

The glyph family in `assets/brand/icons/` covers the product-specific set: screenshot, video, audio, text, link, Library, receipt, annotate, OCR, pin, region, window, display, and all displays.

- Grid: 24 × 24.
- Stroke: 1.8 px, round caps and joins.
- Default: monochrome using the surface's current text colour.
- State colour belongs to the container or status mark, never to the capture-kind glyph.
- Generic controls such as close, search, settings, volume, and delete should continue to use the Fluent system set; duplicating them would make CursorPocket feel less native.

## Imagery

The image language is a **catch field**: one oversized gesture line moves abstract capture tokens into a local orbit or pocket. Tokens may imply a screen crop, waveform, text strip, or linked pair, but should never reproduce application UI.

The current completed transparent hero master is `assets/brand/imagery/cursorpocket-catch-field-v2-transparent.png`. It retains the full sweeping gesture, both large loops, four capture cards, and the resolved lower orbit inside the canvas.

- Use deep-pine negative space, oversized cropped gesture paths, warm-paper tokens, and restrained coral/blue accents.
- Keep imagery editorial and kinetic. It may use subtle screen-print grain, but never glass cards or glossy 3D objects.
- No people are required. No cloud, account, upload, sharing, analytics, or AI imagery.
- The hero image belongs to campaigns, onboarding, documentation, and splash surfaces—not the command panel, HUD, receipts, or capture output.

## Asset map

| Path | Purpose |
| --- | --- |
| `assets/brand/main-logo.png` | Canonical transparent main app logo |
| `assets/brand/brand-logo-02-pocket-v3-transparent.png` | Canonical transparent secondary pocket logo |
| `assets/brand/brand-logo-02-pocket-v3.png` | Secondary pocket logo on deep-pine ground |
| `assets/brand/brand-logo-03-wordmark-transparent.png` | Transparent reversed motion wordmark for dark surfaces |
| `assets/brand/logo-mark.svg` | Default ready mark |
| `assets/brand/logo-mark-recording.svg` | Recording mark with negative-space cursor |
| `assets/brand/logo-lockup.svg` | Flat lockup for light surfaces |
| `assets/brand/logo-lockup-reversed.svg` | Flat lockup for dark surfaces |
| `assets/brand/app-icon*.svg` | Vector application state icons |
| `assets/brand/tray-*.svg` | Hand-simplified 24 px tray masters |
| `assets/brand/icons/` | Fourteen product-specific SVG glyphs |
| `assets/brand/icon-set.svg` | Glyph contact sheet |
| `assets/brand/imagery/cursorpocket-catch-field-v2-transparent.png` | Completed high-resolution transparent catch-field hero |
| `assets/brand/imagery/cursorpocket-catch-field.png` | Brand hero artwork |
| `assets/brand/export/` | Generated PNG and ICO deliverables |
| `assets/brand/export/brand-board.png` | Rebuilt 2400 × 1800 brand board using all approved masters |
| `assets/brand/export/logo-mark*.png` | Pixel-identical primary, ready, and recording master exports |
| `assets/brand/export/wordmark*.png` | Transparent wordmark and dark/light presentation exports |
| `assets/brand/export/logo-lockup*.png` | Transparent, dark, light, ready, and stacked signature lockups |
| `assets/brand/export/catch-field*.png` | Transparent and deep-pine hero deliverables |
| `assets/brand/export/app-icon-*.png` | Ready and recording application tiles from 16–1024 px |
| `assets/brand/export/tray-*.png` | Ready and recording transparent tray marks from 16–64 px |
| `assets/brand/export/CursorPocket*.ico` | Eight-frame ready and recording Windows icon files |
| `assets/brand/export/brand-assets-manifest.json` | Source and deliverable dimensions/modes for automated consumers |
| `assets/brand/brand-tokens.json` | Machine-readable colour and shape tokens |
| `tools/make_brand_assets.py` | Deterministic raster/ICO export pipeline |
| `output/imagegen/` | Image-generation record and concept outputs |

The export generator treats `main-logo.png`, `brand-logo-02-pocket-v3-transparent.png`, `brand-logo-03-wordmark-transparent.png`, and `imagery/cursorpocket-catch-field-v2-transparent.png` as read-only approved masters. Their direct exports are pixel-identical; only presentation backgrounds, lockup composition, and requested size reductions are derived.

Regenerate production exports with:

```powershell
python .\tools\make_brand_assets.py
```

## Migration into the app

This kit does not replace installed application assets or UI code by itself. When the brand is promoted into the product, update the WinUI package images, tray frames, command-panel header, and design tokens together. Verify the ready and recording marks at 16, 20, 24, 32, 48, and 256 px on the active Windows display scale before release.
