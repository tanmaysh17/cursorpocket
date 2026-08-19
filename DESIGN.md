# CursorPocket design system

CursorPocket is an instrumental, local-first Windows utility: it should feel present only when the user asks for it, make recording state unmistakable, and then get out of the way. The interface is designed for daily keyboard-first use, not as a dashboard that competes with the work being captured.

## Product principles

1. **Source first.** Command mode preserves a frozen view of the source desktop so the user never loses visual context.
2. **One deliberate next action.** Each surface has one dominant decision: choose a command, confirm a recording, stop and save, or inspect a capture.
3. **State is visible.** Green means ready/saved. Red is reserved for recording, discard, and destructive action. Every successful or failed capture produces a receipt.
4. **Local is legible.** Device names, the capture folder, final file type, and reveal/open actions are explicit. There are no cloud, account, sharing, or analytics affordances.
5. **Transient versus persistent.** The edge overlay, cursor companion, HUD, and receipts are compact transient tools. Library and Settings are normal movable, resizable Windows surfaces.

## Visual direction

The visual direction is **instrument, not app**. Quiet graphite Mica for persistent surfaces, a compact command panel over the frozen desktop with a restrained green edge, and the cursor mark used sparingly as the brand: at command-panel header scale, never as page decoration. Structure comes from a strict grid, hairline rules, and a real type scale — never from tinted blobs. Avoid floating glass cards with arbitrary borders, excessive gradients, decorative spheres, glossy or three-dimensional marks, and ornamental copy.

### Colour roles

| Role | Token | Usage |
| --- | --- | --- |
| Ink | `#F2F7F4` | Primary text |
| Ink dim | `#C2CFC9` | Descriptions and supporting copy |
| Muted | `#8FA09A` | Tertiary only: timestamps, counts, paths; never critical recording state |
| Base | `#0B100F` | Opaque transient surfaces (HUD, receipt) |
| Sunken | `#66070C0B` | Wells and inset groups inside a card |
| Surface | `#CC101815` | Mica-supported panels and cards |
| Raised | `#E0161F1C` | Inputs, key chips, and secondary controls |
| Line | `#24FFFFFF` | Restrained structural separation |
| Line strong | `#40FFFFFF` | Key chip edges and framing rectangles |
| Ready | `#45E08C` | Ready, saved, audio activity, the one primary action |
| Recording | `#FF5F6B` | Live recording, discard, destructive state |
| Informational | `#7FBBFF` | Text/link capture only; use sparingly |

**Green is load-bearing, not decorative.** It is allowed on exactly four things: live or ready state, the single primary action on a surface, the current selection, and the command-mode edge field. It is never a background wash behind a list of items, and never a tint on a per-kind icon. Red is never used for ordinary navigation.

Capture kinds are distinguished by their glyph and a mono file-type tag, never by hue. That keeps a coloured surface meaning "something is live" everywhere in the app.

### Type and spacing

- Segoe UI Variable Display for `PocketDisplayText` (32) and `PocketTitleText` (22), both with negative tracking.
- Segoe UI Variable Text for `PocketSubtitleText` (16), `PocketBodyText` (14), and `PocketCaptionText` (13).
- Segoe UI Variable Small for `PocketMetaText` (13, muted).
- Cascadia Mono for anything the eye has to compare: `PocketNumericText` (12) for sizes, counts, timers, and paths; `PocketLabelText` (11, tracked, uppercase) for section labels; `PocketTagText` (11) for file-type tags; and every key chip.
- Base spacing unit: 4 px. Normal sequences use 8, 12, 16, 20, 24, or 32 px.
- Corner radii: 8 px controls, 12 px cards, 16 px wells and transient receipts, 20 px HUD.
- Minimum critical text: 13 px with high contrast. Recording state and timer are 17–22 px.

### Controls

All buttons share one template, so height (36 px), radius (8 px), hover, pressed, and disabled response are identical everywhere. Four roles: `PocketPrimaryButton` (green, one per surface), `PocketQuietButton` (raised with a hairline), `PocketGhostButton` (transparent), and `PocketDangerButton` (red text). A destructive action is never adjacent to a benign one; it sits at the opposite end of its row.

Key chips are real keys: `KeyCapButton` (32 px) and `KeyCapLargeButton` (40 px), mono, with a heavier bottom edge. The same chip renders a shortcut wherever it appears — capture tiles, command mode, settings, region selector — so the app teaches its own keys.

## Surface contracts

### Cursor companion

- Rendered by a per-pixel-alpha native window: a 4 px borderless status mark with a 28 px invisible target.
- Offset close to the pointer without covering its tip.
- Green when ready, red throughout starting/recording/finalizing, absent in Off/Hidden mode.
- It never becomes the active window and never leaves a black fallback rectangle.

### Command mode

- A compact panel (372 × 468 dips) anchored in one corner of the work area under the pointer, not a full-monitor overlay. It covers as little of the user's work as possible.
- Backed by the slice of frozen desktop it happens to cover, re-sliced whenever it moves, so the source stays legible through it rather than behind an opaque fallback surface.
- One restrained green edge treatment—a top wash and a hairline border—communicates that command mode is active. No four-edge glow, no hard-edged glass slab.
- **Keep-away is a contract.** When the pointer enters the keep-away band the panel steps to the corner farthest from it, then holds until the pointer leaves the band again. It never moves while the pointer is inside it, so every row stays clickable. Placement decisions live in `PalettePlacementPolicy` and are unit-tested.
- The header carries the product logo as the brand mark and the Library affordance, with a soft green pulse behind it. This supersedes the earlier vector-cursor-only rule and the general ban on raster marks, for this surface only. The asset is the cursor-only form of the mark, rendered at header scale rather than downscaled from the full lockup.
- Because the panel can land in any corner of any display, it names itself with a `COMMAND MODE` label instead of relying on the user recognising the surface.
- Screenshot, video, and audio carry a description; text, link, repeat-video, and Library are one line each below a hairline. There is no trailing icon column: a column that mixes a submenu chevron with category glyphs states two different things in one place.
- Key chips share one right-aligned 60 px column, so a two-key shortcut cannot shift a label out of line with the rest.
- The command list scrolls rather than clipping, so nothing critical is lost at high display scale.
- Commands are mnemonic single keys. Screenshot is explicitly sequential (`S`, then `R/W/D/A/P`).
- Mnemonic keys are temporarily registered only while command mode is visible so focus cannot make them intermittent. Because the panel no longer covers the screen it can lose activation while still shown; the global bare-key service is what keeps the keys working, and clicking elsewhere deliberately does **not** dismiss it.

### Recording preflight

- Normal inspectable setup surface; never excluded from screen readers or visual QA before recording.
- Three numbered decisions read straight down the left column in the order they are numbered: screen, microphone, camera.
- The right column answers what the numbers cannot: a framing preview with the camera picture-in-picture shown in the slot it will actually occupy, plus mono tags stating container, frame rate, microphone, and countdown.
- Microphone and camera show named Windows devices, availability, live signal/preview, and remembered selection.
- Start remains disabled until discovery completes and FFmpeg is available. Readiness is stated by a dot colour as well as words.
- Camera preview is released before FFmpeg starts.

### Recording HUD

- Excluded from the captured media and placed at top center using DPI-aware sizing.
- Opaque near-black surface (`#09110F`) with white primary text, pale supporting text, red live mark, green level meter.
- No outline or hard bounding stroke; separation comes from the opaque surface, radius, and restrained shadow.
- Status and actions are separated by a flexible gap, so stopping is never a near-miss for reading the timer.
- Timer, device state, **Stop & save**, and **Discard** are simultaneously visible at 100–250% display scale. The timer is mono with tabular figures so it does not jitter.
- The primary stop action is text-labelled; discard has both an accessible name and tooltip.

### Receipt and Library

- Receipt appears without stealing focus, remains for 12 seconds, and pauses on hover. Because hovering pauses the countdown indefinitely, it also carries an explicit dismiss control.
- Receipts use the correct media preview and explicit Open/Reveal/Library actions.
- Library is a standard resizable Mica window with a top navigation bar, so the wordmark and section names stay visible at every width.
- Library rows are 52 px: neutral kind chip, title, mono file-type tag, mono size, mono time. Selection is carried by fill, not by a green wash.
- Filters are one segmented control with a live count per filter.
- The detail pane states kind, size, saved time, and file name as facts rather than leaving an empty well.
- Media-specific previews and playback are first-class; recoverable deletion always goes to Recycle Bin.

## Brand mark

The mark is one idea: a cursor crossing into a pocket. Above the pocket mouth the arrow is solid; where it crosses, a keyline cuts the pocket back so the arrow reads as passing in front of it. Nothing is shaded, glossed, or beveled.

- The canonical cursor geometry lives in two places that must agree: `PocketCursorPath` in `App.xaml` and `CURSOR` in `tools/make_logo.py`.
- Below 40 px the pocket and its keyline collapse, so the icon falls back to the cursor alone. `tools/make_icon.py` renders every `.ico` frame at its own size rather than downscaling one raster.
- The tray icon turns red end to end while recording, matching the cursor companion. It never gains a corner badge, which would swallow a 16 px icon.
- The command panel header is the one place a raster of the mark is used, at 26 dips over a green pulse. `tools/make_logo.py` writes that asset in the cursor-only form so it stays crisp from 100% to 250% scale.
- Regenerate with `python tools/make_logo.py && python tools/make_icon.py`.

## Design acceptance gate

The design-consultation gate requires all of the following before release:

- no opaque gray capture surface or black companion rectangle;
- the command panel stays compact, preserves readable source context, and has no hard-edged glass slab;
- the command panel steps away from an approaching pointer, holds still once the pointer is on it, and scrolls instead of clipping at 100–250% scale;
- every displayed shortcut works while command mode is visible;
- `Enter` saves a screenshot from the annotation surface whether or not anything was drawn;
- named microphone and camera are visible before video begins;
- recording HUD is readable over both light and dark source content at the active Windows scale;
- save, failure, and discard states are unmistakable;
- Library remains readable and scrollable at its minimum size;
- green appears only on live state, the primary action, the current selection, and the command-mode field;
- typography matches the scale above and every button shares one height and hover response;
- the brand mark is legible at 16 px and carries no gloss, bevel, or sphere;
- screenshots, video, audio, text, and links each produce the correct receipt and Library item.

Approval is based on an end-to-end visual and interaction pass of the installed build, not on XAML inspection alone.
