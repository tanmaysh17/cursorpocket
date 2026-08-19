# CursorPocket design system

CursorPocket is an instrumental, local-first Windows utility: it should feel present only when the user asks for it, make recording state unmistakable, and then get out of the way. The interface is designed for daily keyboard-first use, not as a dashboard that competes with the work being captured.

## Product principles

1. **Source first.** Command mode preserves a frozen view of the source desktop so the user never loses visual context.
2. **One deliberate next action.** Each surface has one dominant decision: choose a command, confirm a recording, stop and save, or inspect a capture.
3. **State is visible.** Green means ready/saved. Red is reserved for recording, discard, and destructive action. Every successful or failed capture produces a receipt.
4. **Local is legible.** Device names, the capture folder, final file type, and reveal/open actions are explicit. There are no cloud, account, sharing, or analytics affordances.
5. **Transient versus persistent.** The edge overlay, cursor companion, HUD, and receipts are compact transient tools. Library and Settings are normal movable, resizable Windows surfaces.

## Visual direction

The visual direction is **cursor-field Fluent**: quiet graphite Mica for persistent surfaces, a compact command panel over the frozen desktop with a restrained green edge, and the product logo used sparingly as the brand mark—at command-panel header scale, never as page decoration. Avoid floating glass cards with arbitrary borders, excessive gradients, decorative spheres, or ornamental copy.

### Color roles

| Role | Token | Usage |
| --- | --- | --- |
| Ink | `#F4FAF7` | Primary text on dark surfaces |
| Muted | `#98A9A2` | Supporting text; never critical recording state |
| Surface | `#D9151E24` | Mica-supported panels and cards |
| Raised | `#EE1C2830` | Inputs and secondary controls |
| Divider | `#2EFFFFFF` | Restrained structural separation |
| Ready | `#43E08D` | Ready, saved, audio activity, primary action |
| Recording | `#FF5A67` | Live recording, discard, destructive state |
| Informational | `#7AB8FF` | Text/link capture only; use sparingly |

Green is not used as generic decoration inside dense surfaces. Red is never used for ordinary navigation.

### Type and spacing

- Segoe UI Variable Display for display and section headings.
- Segoe UI Variable Text/Small for controls and supporting copy.
- Cascadia Mono only for key chips, timers, dimensions, and compact status labels.
- Base spacing unit: 8 px. Normal sequences use 8, 16, 24, or 32 px.
- Corner radii: 10 px controls, 12–14 px cards, 16–20 px transient receipts/HUD.
- Minimum critical text: 12 px with high contrast; recording state and timer are 17–22 px.

## Surface contracts

### Cursor companion

- Rendered by a per-pixel-alpha native window: a 4 px borderless status mark with a 28 px invisible target.
- Offset close to the pointer without covering its tip.
- Green when ready, red throughout starting/recording/finalizing, absent in Off/Hidden mode.
- It never becomes the active window and never leaves a black fallback rectangle.

### Command mode

- A small panel (296 × 340 dips) on the pointer's display, not a full-monitor overlay. It covers as little of the user's work as possible.
- **The user places it; nothing else moves it.** Drag anywhere on the panel except a button to move it, double-click to reset to the top right. An earlier version stepped away from an approaching pointer on its own; predictability proved worth more than the clearance, so the pointer, a mode change, and a reopen never relocate it.
- The position is remembered as a fraction of the display's free space (`CommandPanelPlacement`), not as screen coordinates, so it means the same thing on another display, resolution, or DPI and can never be restored off screen.
- Dragging uses Windows' own move loop rather than per-frame pointer tracking, so it feels exactly like dragging a title bar on a surface that has none. The panel takes its rounded corners from DWM rather than a window region, because a region clip drops the window off DWM's fast path and makes dragging lag.
- **Liquid glass.** Command mode is the one transient surface that uses a system backdrop: `DesktopAcrylicBackdrop` blurs the live desktop behind it, with only a thin tint over the top for text contrast. The system paints the whole window, so this is not the transparent-root gutter that the opacity rule guards against. The frozen desktop snapshot it replaced now belongs to region selection alone.
- Rows are single-line: keycap, label, kind icon. No per-row captions—the panel is meant to be read at a glance, not studied.
- A hairline green border communicates that command mode is active. No four-edge glow, no hard-edged glass slab.
- The header carries the product logo as the brand mark and the Library affordance, with a soft green pulse behind it. This supersedes the earlier vector-cursor-only rule and the general ban on raster marks, for this surface only.
- The command list scrolls rather than clipping, so nothing critical is lost at high display scale.
- Commands are mnemonic single keys. Screenshot is explicitly sequential (`S`, then `R/W/D/A/P`).
- Mnemonic keys are temporarily registered only while command mode is visible so focus cannot make them intermittent. Because the panel no longer covers the screen it can lose activation while still shown; the global bare-key service is what keeps the keys working, and clicking elsewhere deliberately does **not** dismiss it.

### Recording preflight

- Normal inspectable setup surface; never excluded from screen readers or visual QA before recording.
- Three numbered decisions: source, microphone, camera.
- Microphone and camera show named Windows devices, availability, live signal/preview, and remembered selection.
- Start remains disabled until discovery completes and FFmpeg is available.
- The preflight camera preview is released before the recording self-view opens the same device.

### Camera self-view

- When a recording includes the camera, a live self-view sits inside the area being recorded at the chosen corner and size, so the user can see their own feed while they record.
- **It is the one CursorPocket surface deliberately visible in captured media.** CursorPocket holds the camera for the whole recording, and the webcam reaches the file by being on screen inside the captured rectangle. FFmpeg must never be given a `dshow` camera input at the same time—DirectShow grants a single consumer exclusive use, and that is exactly what made a live self-view impossible before.
- Placement is computed by `CameraSelfViewPlacement` and must always land inside the recorded rectangle. Anything outside it is missing from the file.
- **Draggable, and clamped to the recorded rectangle.** The user drags it to reposition their camera mid recording, so it accepts pointer input rather than being click-through. The clamp is not cosmetic: the webcam reaches the file by being on screen inside that rectangle, so a self-view dragged outside it would silently vanish from the recording. It never takes activation from the work being demonstrated.
- Window-source recordings capture a single window, so the self-view stays visible on screen but cannot appear in the file. Preflight says so before recording rather than letting the user discover it afterwards.
- A camera that cannot be opened—privacy settings, unplugged, held by another app—never blocks the recording. The screen still records, without a webcam inset.
- The camera is released as soon as the recording stops, so the next preflight preview does not find the device busy.

### Recording HUD

- Excluded from the captured media and tucked against the top edge, centred, using DPI-aware sizing.
- **Small by default, complete on approach.** Collapsed it is a 178 × 30 dip pill: live mark, timer, and level meter, nothing else. It opens to the full surface—mode, device, **Stop & save**, **Discard**—as the pointer *approaches*, not when it lands, and draws back up when the pointer leaves. Recording is not a mode the user is asked to look at; it is a mode they occasionally reach for.
- It moves like a drawer: an eased travel of roughly 190 ms in both directions, with the contents cross-fading and sliding down as it opens. Snapping between two sizes read as abrupt. Window geometry cannot be animated by the composition engine, so `DrawerAnimation` steps it per frame; keyboard focus holds it open, tracked explicitly because `FocusManager` reports stale focus on an inactive window.
- `Escape` stops and saves at any time, which is what makes a collapsed HUD safe. The collapsed pill carries its state as a live mark plus a tooltip, with the running timer as the non-colour cue.
- Opaque near-black surface (`#09110F`) with white primary text, pale supporting text, red live mark, green level meter.
- No outline or hard bounding stroke; separation comes from the opaque surface, radius, and restrained shadow.
- Timer, device state, **Stop & save**, and **Discard** are all visible together once expanded, at 100–250% display scale.
- The primary stop action is text-labelled; discard has both an accessible name and tooltip.
- The level meter is a rolling waveform across a short history of samples (`AudioLevelHistory`), not a single bar tracking the current level. Silence still draws a visible baseline so a quiet room does not look like a broken meter, and levels are square-rooted so ordinary speech registers.

### Receipt and Library

- Receipt appears without stealing focus, remains for 12 seconds, and pauses on hover. Because hovering pauses the countdown indefinitely, it also carries an explicit dismiss control.
- Receipts use the correct media preview and explicit Open/Reveal/Library actions. A screenshot is also copied to the clipboard the moment it is taken, and again after annotation so the clipboard holds the marked-up image; the receipt says so.
- Library is a standard resizable Mica window with date grouping and All/Screenshots/Video/Audio/Text/Links filters.
- Media-specific previews and playback are first-class; recoverable deletion always goes to Recycle Bin.

## Design acceptance gate

The design-consultation gate requires all of the following before release:

- no opaque gray capture surface or black companion rectangle;
- the command panel stays small, keeps the blurred desktop readable behind it, drags from anywhere except a button, reopens where it was left, and never moves on its own;
- the command list scrolls instead of clipping at 100–250% scale;
- every displayed shortcut works while command mode is visible;
- a screenshot opens its annotation surface in the foreground, never behind the source app or minimized;
- `Enter` saves a screenshot from the annotation surface whether or not anything was drawn, and the shot is on the clipboard without asking;
- a dragged region is captured in full, with nothing missing from its right or bottom edge at any display scale;
- two circles drawn with the pointer open command mode whether they are small or large, fast or slow, clockwise or not — and ordinary mouse work over a working session never opens it;
- named microphone and camera are visible before video begins;
- the camera self-view is visible on screen while recording, lands inside the recorded area, passes clicks through, and appears in the saved display or region recording;
- recording HUD is readable over both light and dark source content at the active Windows scale;
- save, failure, and discard states are unmistakable;
- Library remains readable and scrollable at its minimum size;
- green/red semantics and typography match this document;
- screenshots, video, audio, text, and links each produce the correct receipt and Library item.

Approval is based on an end-to-end visual and interaction pass of the installed build, not on XAML inspection alone.
