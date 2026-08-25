# CursorPocket design system

CursorPocket is an instrumental, local-first Windows utility: it should feel present only when the user asks for it, make recording state unmistakable, and then get out of the way. The interface is designed for daily keyboard-first use, not as a dashboard that competes with the work being captured.

## Product principles

1. **Source first.** Command mode uses desktop acrylic to preserve the live source context without obscuring it.
2. **One deliberate next action.** Each surface has one dominant decision: choose a command, confirm a recording, stop and save, or inspect a capture.
3. **State is visible.** Green means ready/saved. Red is reserved for recording, discard, and destructive action. Every successful or failed capture produces a receipt.
4. **Local is legible.** Device names, the capture folder, final file type, and reveal/open actions are explicit. There are no cloud, account, sharing, or analytics affordances.
5. **Transient versus persistent.** The edge overlay, cursor companion, HUD, and receipts are compact transient tools. Library and Settings are normal movable, resizable Windows surfaces.

## Visual direction

The visual direction is **instrument, not app**. CursorPocket uses layered liquid glass throughout: Mica or Mica Alt for long-lived windows, Desktop Acrylic for short-lived decision surfaces, and restrained translucent planes for navigation, inspectors, and controls. Each window owns one compositor backdrop; individual panels do not stack independent blur effects. Structure comes from a strict grid, hairline highlights, and a real type scale — never from tinted blobs. Avoid decorative glass-card mosaics, excessive gradients, decorative spheres, glossy or three-dimensional marks, and ornamental copy.

### Theme and material

- `System`, `Light`, and `Dark` are first-class app modes. The choice applies live to every app-owned window, title bar, flyout, tooltip, dialog, overlay, and tray/context menu.
- Light uses a pale mineral glass with dark graphite ink. Dark uses graphite glass with chalk ink. Both retain the same green-ready and red-recording meanings.
- High contrast always wins over the app preference. Disabled transparency, battery saver, unsupported backdrop hardware, and inactive windows use the matching opaque semantic fallback.
- Windows-owned File/Folder pickers and Explorer surfaces follow Windows. CursorPocket never replaces system UI merely to force its app theme.

### Colour roles

| Role | Token | Usage |
| --- | --- | --- |
| Ink | `#F6F4EC` | Primary text; brand name Paper |
| Ink dim | `#CBD7D1` | Descriptions and supporting copy |
| Muted | `#8EA099` | Tertiary only: timestamps, counts, paths; never critical recording state |
| Base | `#07130F` | Opaque transient surfaces (HUD, receipt); brand name Pine |
| Sunken | `#66070C0B` | Wells and inset groups inside a card |
| Surface | `#CC101815` | Mica-supported panels and cards |
| Raised | `#E0161F1C` | Inputs, key chips, and secondary controls |
| Line | `#24FFFFFF` | Restrained structural separation |
| Line strong | `#40FFFFFF` | Key chip edges and framing rectangles |
| Ready | `#36E58C` | Ready, saved, audio activity, the one primary action, brand motion |
| Fold | `#2B6F63` | Brand ribbon backside and pocket only; never critical state by itself |
| Recording | `#FF5964` | Live recording, discard, destructive state |
| Informational | `#7AA7FF` | Text/link capture only; use sparingly |

**Green is load-bearing, not decorative.** It is allowed on exactly four things: live or ready state, the single primary action on a surface, the current selection, and the command-mode edge field. It is never a background wash behind a list of items, and never a tint on a per-kind icon. Red is never used for ordinary navigation.

Capture kinds are distinguished by their glyph and a mono file-type tag, never by hue. That keeps a coloured surface meaning "something is live" everywhere in the app.

### Type and spacing

- Segoe UI Variable Display for `PocketDisplayText` (32) and `PocketTitleText` (22), both with negative tracking.
- Segoe UI Variable Text for `PocketSubtitleText` (16), `PocketBodyText` (14), and `PocketCaptionText` (13).
- Segoe UI Variable Small for `PocketMetaText` (13, muted).
- Cascadia Mono only for values the eye compares: timers, coordinates, sizes, paths, file-type tags, and compact key hints. Interface headings and section labels use Segoe UI Variable in sentence case.
- Base spacing unit: 4 px. Normal sequences use 8, 12, 16, 20, 24, or 32 px.
- Corner radii: 8 px controls, 12 px cards, 16 px wells and transient receipts, 20 px HUD.
- Minimum critical text: 13 px with high contrast. Recording state and timer are 17–22 px.

### Controls

All buttons share one template, so height (36 px), radius (8 px), hover, pressed, and disabled response are identical everywhere. Four roles: `PocketPrimaryButton` (green, one per surface), `PocketQuietButton` (raised with a hairline), `PocketGhostButton` (transparent), and `PocketDangerButton` (red text). A destructive action is never adjacent to a benign one; it sits at the opposite end of its row.

Key chips are real keys: `KeyCapButton` (32 px) and `KeyCapLargeButton` (40 px), mono, with a heavier bottom edge. The same chip renders a shortcut wherever it appears — capture tiles, command mode, settings, region selector — so the app teaches its own keys.

## Surface contracts

### First-run onboarding

- A first visible launch opens a three-stage field guide before the persistent Library: meet CursorPocket, learn command-mode activation, and rehearse with the real command surface. A `--background` launch never raises or flashes onboarding.
- The flow has one job: move a new user from install to a confident first capture in under a minute. It explains local storage and recording colour semantics, shows all seven commands from the shared action catalogue, confirms the registered activation shortcut, and makes the real command mode directly launchable.
- The signature is one changing cursor-field instrument over the persistent Mica backdrop. It is not a marketing carousel and does not introduce a decorative card mosaic, gradients, screenshots of obsolete UI, or permission requests before a relevant action.
- The rail is a real three-step sequence and may be selected directly. At narrow widths it collapses while Back and Continue preserve the same sequence. The surface may use one document-level vertical scroll under text scaling; it never scrolls horizontally or nests scroll regions.
- The final step offers two relevant, reversible choices: start at sign-in and show the cursor companion. The installer does not maintain a competing startup preference. Finish and Skip both persist the current onboarding version atomically; Skip preserves the loaded choices. Settings exposes `Run tour` without clearing completion, so leaving a revisited tour cannot make it appear again at startup. A future materially changed tour may increment the version and run once.
- Onboarding inherits live System, Light, Dark, high-contrast, transparency, text-scale, and reduced-motion policies. Every action is at least 36 px, named, keyboard-operable, and visibly focused.

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
- The row icon column says one thing on every row: which kind of capture it is. It never mixes a submenu chevron with kind glyphs, and no row is left without one.
- Every key chip is the same width and a combination rides in a single chip, so a two-key shortcut cannot shift its label out of line with the rest.
- The root presents seven content-sized actions: Screenshot `S`, Video `V`, Repeat video `Shift+V`, Audio `A`, Highlighted text `T`, Current link `L`, and Library `O`.
- Screenshot drills into Region `R`, Window `W`, Display `D`, All displays `A`, and Previous region `P`; `Esc` returns to the root. Numeric screenshot shortcuts are forbidden.
- Visible command rows are always clickable. Bare keys are accelerators scoped to the visible panel and cannot depend on which control currently owns focus.
- At normal height the root is one compact list. A short work area uses a two-column command matrix, and the screenshot page uses a 3 × 2 matrix. Neither page scrolls or clips.
- Commands are mnemonic single keys. Screenshot is explicitly sequential (`S`, then `R/W/D/A/P`).
- Mnemonic keys are temporarily registered only while command mode is visible so focus cannot make them intermittent. Because the panel no longer covers the screen it can lose activation while still shown; the global bare-key service is what keeps the keys working, and clicking elsewhere deliberately does **not** dismiss it.

### Recording preflight

- Normal inspectable setup surface; never excluded from screen readers or visual QA before recording.
- Three numbered decisions read straight down the left column in the order they are numbered: screen, microphone, camera.
- The right column answers what the numbers cannot: a framing preview with the camera picture-in-picture shown in the slot it will actually occupy, plus mono tags stating container, frame rate, microphone, and countdown.
- Microphone and camera show named Windows devices, availability, live signal/preview, and remembered selection.
- Start remains disabled until discovery completes and FFmpeg is available. Readiness is stated by a dot colour as well as words.
- The preflight camera preview is released before the recording self-view opens the same device.

### Camera self-view

- When a recording includes the camera, a live self-view sits inside the area being recorded at the chosen corner and size, so the user can see their own feed while they record.
- **It is the one CursorPocket surface deliberately visible in captured media.** CursorPocket holds the camera for the whole recording, and the webcam reaches the file by being on screen inside the captured rectangle. FFmpeg must never be given a `dshow` camera input at the same time—DirectShow grants a single consumer exclusive use, and that is exactly what made a live self-view impossible before.
- Placement is computed by `CameraSelfViewPlacement` and must always land inside the recorded rectangle. Anything outside it is missing from the file.
- **Draggable, and clamped to the recorded rectangle.** The user drags it to reposition their camera mid recording, so it deliberately accepts pointer input while keeping the source application active. The clamp is not cosmetic: the webcam reaches the file by being on screen inside that rectangle, so a self-view dragged outside it would silently vanish from the recording. It never takes activation from the work being demonstrated.
- Window-source recordings capture a single window, so the self-view stays visible on screen but cannot appear in the file. Preflight says so before recording rather than letting the user discover it afterwards.
- A camera that cannot be opened—privacy settings, unplugged, held by another app—never blocks the recording. The screen still records, without a webcam inset.
- The camera is released as soon as the recording stops, so the next preflight preview does not find the device busy.
- Shape is the user's choice: `rounded` (16:9, the default) or `squircle` (1:1 superellipse). Both are cut by a GDI window region. Regions are 1-bit, so their edges are aliased; the existing hairline border rides that edge and is what keeps it from reading as stairsteps. This surface uses application pointer tracking rather than Windows' modal move loop, so its region does not introduce the command-panel drag regression.
- Camera effects render into the self-view, so whatever the user sees is exactly what the file gets. With every effect off the plain `MediaPlayerElement` path still runs; effects swap in an `Image` fed by the frame-reader pipeline. Neither path is ever capture-excluded.

### Camera effects

- Effects are opt-in and all default to off. A user who never opens the controls records the camera untouched, on the pre-effects code path.
- Everything runs on-device: colour adjustment and blur are plain pixel math, and the person mask comes from a hash-pinned local ONNX model. No frame, mask, or derived data leaves the machine, and there is no network call in the path.
- Preflight shows the effects live in the framing slot, so the shape and look are settled before recording rather than discovered afterwards.
- Degrade, never fail. A missing model disables background blur and replacement and says so in words; colour and touch-up keep working. A machine that cannot keep up stretches inference across frames before anything visible drops. Without a mask the background is left alone—blurring everything would erase the user.
- Brightness, warmth, and contrast are the one place a slider is allowed, because the live preview sits beside them and the value is continuous. Its row is label / slider / mono numeric readout. Every other setting stays an enumerated `ComboBox`.

### Microphone cleanup

- Noise suppression and auto-levelling are applied when the recording is finalized, never live. The raw capture is always written first and only replaced when the cleanup pass succeeds, so a filter problem can cost the cleanup but never the take.
- Both apply to narration and to standalone audio notes. Audio notes still record and save with no FFmpeg present—cleanup is simply skipped.

### Recording HUD

- Excluded from the captured media and tucked against the top edge, centred, using DPI-aware sizing.
- **Small by default, complete on approach.** Collapsed it is a 178 × 30 dip pill: live mark, timer, and level meter, nothing else. It opens to the full surface—mode, device, **Stop & save**, **Discard**—as the pointer *approaches*, not when it lands, and draws back up when the pointer leaves. Recording is not a mode the user is asked to look at; it is a mode they occasionally reach for.
- It moves like a drawer: **one fixed-size window that slides vertically**, mostly above the top edge when closed, over an eased ~190 ms travel with the contents cross-fading. Nothing resizes and no window region is recomputed per frame — both drop the window off DWM's fast path, which is what made an earlier resize-based version stutter. Rounded corners come from DWM instead. Keyboard focus holds it open, tracked explicitly because `FocusManager` reports stale focus on an inactive window.
- Text is sized to fit the drawer. Type large enough to overflow the panel is worse for legibility than smaller type that fits.
- `Escape` stops and saves at any time, which is what makes a collapsed HUD safe. The collapsed pill carries its state as a live mark plus a tooltip, with the running timer as the non-colour cue.
- Opaque near-black surface (`#09110F`) with white primary text, pale supporting text, red live mark, green level meter.
- No outline or hard bounding stroke; separation comes from the opaque surface, radius, and restrained shadow.
- Status and actions are separated, so stopping is never a near-miss for reading the timer.
- Timer, device state, **Stop & save**, and **Discard** are all visible together once expanded, at 100–250% display scale. The timer is mono with tabular figures so it does not jitter.
- The primary stop action is text-labelled; discard has both an accessible name and tooltip.
- The level meter is a rolling waveform across a short history of samples (`AudioLevelHistory`), not a single bar tracking the current level. Stems grow from a mid-line in both directions with a bright centre, so the form has a spine and reads as a waveform rather than a bar chart; quiet samples fade back instead of vanishing. Silence still draws a visible baseline so a quiet room does not look like a broken meter, and levels are square-rooted so ordinary speech registers.

### Annotation editor

- The editor is a normal Mica window the user works *in*, not a transient overlay, so the opaque-root and capture-exclusion rules for transient surfaces do not apply to it. It is brought up with `WindowPlacement.ForceForeground` on the capture path and plain `Activate()` everywhere else — a transient surface has just hidden itself on the capture path, so the source app still owns the foreground lock.
- **The editor separates selection from adjustment without hiding capability.** All sixteen tools live in a stable left panel; contextual properties and Save, Copy, Pin, Undo, and Redo live in a stable right panel. No tool is placed in `More` at any width.
- Every tool button shows its shortcut. At full width it shows icon, name, and key; constrained layouts use a two- or three-column icon-and-key matrix. The teaching compresses, never the capability. Neither side panel scrolls.
- A tool's variants are reached by pressing its key again, and the status strip states the current variant and what the next press will do. That is the teaching surface a hover submenu would otherwise be.
- **Green on this surface means the active tool and the crop handles, plus the one solid-green Save button. Nothing else.** No annotation ink may be a state colour: green is absent from the palette entirely, red is present at a deliberately different value from `Recording`, and blue is absent because it is too close to `Informational`. A green arrow would read as CursorPocket talking rather than as the user's own mark. The active *swatch* is deliberately not green either — two competing selection greens in one toolbar would make neither readable — so it takes an ink-white ring instead. Crop handles are green corner brackets: same hue as the active tool, told apart by form, which is the rule the app already uses for capture kinds.
- Mark weight and text size are derived from the image's short edge, never fixed. A constant is illegible on a 4K shot and most of the frame on a small region capture.
- **What is drawn is what is saved.** The preview and the exporter take every shape from one Core geometry source and every sampled patch from one shared sampler. Neither may compute a shape or read a pixel of its own.
- **Redaction defaults to solid**, and the status strip says "nothing recoverable" against "partly recoverable" in those words. Pixelation and blur derive their output from the pixels underneath, so for a short string they are only partially destructive.
- Recognised text never reaches the clipboard unasked. A screenshot is on the clipboard from the moment it is taken; replacing that with text silently would break a promise the app makes everywhere else.
- Crop, cut, and backdrop change the exported dimensions, so **a geometry change writes a new capture and leaves the original alone.** Marks are additive and still overwrite in place. The status strip carries an output-size field, so the claim that the readout is in native export pixels survives the export no longer matching the shot.
- Backdrops are flat fills with a rendered shadow, not mesh gradients. See the exceptions table below.
- The editor is fully keyboard-drivable through page accelerators, which cannot reach another application. Only `Escape` is a scoped global, because the drawing canvas cannot take focus and the window can lose activation. Something focusable must always hold focus or no accelerator routes at all.

### Pinned captures

- A pin is a receipt the user decided to keep: it is the content itself, at a smaller size, on top. That is what separates it from a floating widget whose purpose is unexplained.
- **Only ever by explicit action, and never restored after a restart.** A window that reappears after a reboot with no explanation is precisely the anti-reference. The Library holds the durable copy.
- Deliberately **not** capture-excluded. A pin exists to be visible, so it must appear in a screenshot or recording taken while it is up. Visible equals captured; a user who does not want it in the shot closes it.
- Controls appear on hover in a strip at the bottom — a screenshot's own content usually starts at its top-left, and covering that is what makes a floating thumbnail useless.
- Dragged by pointer tracking, never by Windows' modal move loop, and it carries no window region: a region takes the window off DWM's fast path, which is what makes a dragged window lag.
- Its `Escape` is a page accelerator, never a scoped global lease. A pin can sit on screen for hours while the user works elsewhere, and holding the topmost lease would steal `Escape` from every application — including a recording, where `Escape` means stop and save.

### Receipt and Library

- The Library gives the preview the majority of the width, and the preview can take the whole window when the list is collapsed. Video and audio use the full transport controls — seek, volume, playback rate, skip, fast forward and rewind, zoom, repeat, full window.
- Rows are compact strips carrying the capture's own thumbnail — the screenshot, a video frame, or its waveform — with the kind icon only as a fallback. Each row states kind and file size. Selection is extended, and delete moves every selected capture to the Recycle Bin, saying how many.
- Receipt appears without stealing focus. Screenshot receipts remain for 3 seconds; video and audio receipts remain for 6 seconds. The countdown pauses on hover or keyboard focus, and a 36 px dismiss control plus `Esc` provide an immediate exit.
- Receipts never register global action shortcuts. Their labelled controls are pointer- and keyboard-operable when focused, and `Esc` dismisses a focused receipt without stealing keys from the user's work.
- The Library is fully keyboard-drivable through page accelerators, which cannot affect other applications: arrows to move, `Enter` open, `Space` play or pause, `Ctrl+R` reveal, `Ctrl+C` copy path, `Delete` remove, `Ctrl+A` select all, `Ctrl+M` fill the window, `Ctrl+1`–`Ctrl+6` filters. Every one stands down while a text box has focus, so Settings never loses `Space` or `Ctrl+A`. The list takes focus when the Library opens.
- Receipts use the correct media preview and labelled click actions such as Open, Mark up, and Show in folder. Receipt-specific global action shortcuts are forbidden. A screenshot is copied to the clipboard when taken and after a saved annotation; Copy inside the editor never overwrites the source capture.
- Library is a standard resizable Mica window with a top navigation bar, so the wordmark and section names stay visible at every width, plus All/Screenshots/Video/Audio/Text/Links filters.
- Selection is carried by fill, not by a green wash. Filters are one segmented control with a live count per filter.
- Beside the preview the detail pane states kind, size, saved time, and file name as facts rather than leaving an empty well.
- Media-specific previews and playback are first-class; recoverable deletion always goes to Recycle Bin.

### Updates and public distribution

- The friend-facing path is one per-user `CursorPocket-Setup-x64.exe` linked from the CursorPocket GitHub Pages site and hosted by GitHub Releases. ZIP and MSIX builds are development artifacts, not competing public choices.
- Automatic checks are on by default and read only the static project `update.json` at most once every 24 hours. They transmit no custom identifier, settings, capture metadata, or analytics and fail silently offline. Settings exposes the current version, last state, opt-out, and a manual Check now action.
- An available update uses the ordinary receipt coordinator with labelled Download and install, Release notes, and Later actions. Nothing downloads without approval. Update UI inherits the current app theme and uses the update glyph as information, not a new decorative colour.
- The installer is accepted only when its size and SHA-256 match the manifest, its Authenticode chain and timestamp are trusted, its publisher is Tanmay Sharma, its Windows floor is satisfied, and its version is newer. A mismatch changes nothing and produces an actionable error.
- Installation never begins during recording, capture, preflight, region selection, or annotation. It uses the recording lifecycle and application shutdown path, preserves captures and settings, and relaunches only after a successful update install.
- Public tags are stable, match the centralized version exactly, and are release-blocked until GitHub OIDC Artifact Signing, signature verification, installed-payload verification, provenance, hashes, notices, and the Pages manifest all succeed.

## Brand mark

The identity is **Pocket + Orbit**, led by one stable application mark. The full rationale, usage rules, glyph system, imagery direction, and export map live in [BRAND.md](BRAND.md).

- **Installed application identity:** brand logo #1—the orbit around a coral focal disc with three signal ticks and a transparent negative-space cursor—is used in the tray, taskbar, Start, installer, title bar, and command surfaces in every state.
- **Secondary local/saved illustration:** the green capture orbit drops into a lighter teal pocket. Use it where the pocket metaphor itself is being explained, not as the installed application icon.
- **Recording state:** remains unmistakable through the red cursor companion, tray tooltip, recording HUD, and recording controls rather than swapping the application brand mark.
- The teal fold is deliberately `#2B6F63`, light enough to remain visible on Pine and black at the active display scale.
- The motion wordmark carries a two-loop green gesture line ending in a square capture token. Use it for brand signatures, splash/onboarding, documentation, and installer surfaces—not as page decoration inside everyday utility UI.
- At 16–20 px use the hand-tuned logo #1 tray assets, fitted to nearly the full available canvas. The 16–64 px Windows application frames optically enlarge the orbit and negative-space cursor relative to the three signal ticks so the taskbar mark reaches the usable slot without clipping. Command mode and the persistent title bar use a 40 px mark. Do not downscale the 1024 px application image and do not add a badge.
- Canonical approved raster masters live in `assets/brand/` and `assets/brand/imagery/`; derived PNG/ICO deliverables live in `assets/brand/export/` and are regenerated without redrawing the masters using `python tools/make_brand_assets.py`. The SVGs remain implementation-safe and small-size references rather than the source of the approved full-colour artwork.
- The product-specific monochrome glyph family lives in `assets/brand/icons/`. Capture kinds remain distinguished by glyph rather than hue.

## Deliberate exceptions

Four rules above are overridden by explicit product decision on one surface each. They are written down here so the code and the design system do not contradict each other, and so nobody "fixes" the code back.

| Rule | Override | Scope |
| --- | --- | --- |
| Avoid excessive gradients | The custom-ink swatch shows a colour wheel | One 26 px control, and only until a colour has been sampled. Backdrops are flat fills, not mesh gradients — that half of the reference tool's look was declined. |
| Separation comes from the opaque surface, radius, and restrained shadow | Exported backdrops render a layered drop shadow under the image | The exported artwork only. No app chrome gains a shadow stack, and `ThemeShadow` is still absent everywhere. |
| Default tooltip delay | Instant tooltips | The annotation toolbar only. |
| `Escape` in annotation cancels without losing the original | `Escape` is two-stage: an armed tool returns to Select, and Select closes | The end state is unchanged — nothing is ever lost — but it can take two presses. The status strip says so after the first, and creation tools return to Select on their own, so most first presses *are* the closing press. |

## Design acceptance gate

The design-consultation gate requires all of the following before release:

- a new profile sees onboarding exactly once on a visible launch, can skip it, can rerun it from Settings, and can open the real command surface from the rehearsal step;
- onboarding exposes the registered activation shortcut and all seven command actions without horizontal or nested scrolling at required scales;
- no opaque gray capture surface or black companion rectangle;
- the command panel stays small, keeps the blurred desktop readable behind it, drags from anywhere except a button, reopens where it was left, and never moves on its own;
- command mode has no scroll region and switches to a drill-down layout before clipping at 100–250% scale;
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
- green appears only on live state, the primary action, the current selection, and the command-mode field;
- typography matches the scale above and every button shares one height and hover response;
- the brand mark is legible at 16 px and carries no gloss, bevel, or sphere;
- screenshots, video, audio, text, and links each produce the correct receipt and Library item.
- automatic update checks can be disabled, never block offline startup, and cannot install an untrusted, corrupt, older, or wrong-publisher artifact;
- every annotation tool key is visible on its own button at full width, and no tool is hidden at any width;
- no annotation ink is a state colour, and green on the annotation surface appears only on the active tool, the crop handles, and Save;
- a drawn mark and the saved PNG are the same shape, at every display scale;
- a solid redaction leaves nothing of the content recoverable, and the status strip says which mode is live;
- recognised text reaches the clipboard only when asked;
- a crop, a cut, or a backdrop writes a new capture and leaves the original untouched, with correct dimensions in the Library;
- `Escape` never loses the original, in one press or two;
- a pin appears only by explicit action, is visible in a subsequent capture, does not survive a restart, and never swallows `Escape` from another application.

Approval is based on an end-to-end visual and interaction pass of the installed build, not on XAML inspection alone.
