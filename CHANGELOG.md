# Changelog

## Unreleased

## 0.4.10 — 2026-08-27

- Made Library Copy place the real capture on the clipboard, including a pasteable bitmap representation for screenshots, instead of copying path text.
- Increased panel-glass transparency slightly while preserving the denser recording HUD treatment.
- Removed the command-center logo pulse so the brand mark remains quiet and static.

## 0.4.9 — 2026-08-27

- Made the command center visibly translucent again by allowing its Desktop Acrylic backdrop to show through the shell.

## 0.4.8 — 2026-08-27

- Restored Liquid Glass across app-owned chrome while keeping screenshots, video, pinned media, and other captured content visually exact.
- Added Low, Balanced, and High sensitivity choices for the two-circle mouse gesture, with live updates and saved settings.

## 0.4.6 — 2026-08-25

- Tightened the public-site hero so the primary Download action stays in the first viewport on common desktop and mobile sizes.
- Added a first-install file-properties Unblock method alongside the SmartScreen continuation steps, with source verification and policy-enforced-block guidance.

## 0.4.5 — 2026-08-25

- Refined the public-site hero so the complete catch-field cyclone remains visible, and paired the main orbit mark with the correct light- and dark-theme motion wordmarks in the header.
- Made the unsigned-installer warning explicit before download, with exact SmartScreen continuation steps and a separate explanation for policy-enforced blocks where Windows does not offer Run anyway.

## 0.4.4 — 2026-08-25

- Expanded the public site with detailed annotation, video recording, and Library capability previews, practical Support guidance, and the approved cyclone, orbit, pocket, and motion-wordmark brand assets.

## 0.4.2 — 2026-08-25

- Moved update checks from the order-sensitive GitHub Pages mirror to the latest GitHub Release manifest, while retaining a main-branch Pages deployment for v0.4.1 compatibility.
- Made release publication explicitly queue that compatibility deployment after the Release asset exists, avoiding tag protection and same-run Pages artifact retry failures.

## 0.4.1 — 2026-08-24

- Made successful `main` builds automatically queue the release pipeline, require a new documented version before merge, and safely resume partial publication, so each app merge becomes a GitHub Release and live update manifest without manual tagging.
- Removed superseded logo iterations, obsolete vector marks, duplicate application-state icons, and generated per-size PNG intermediates; the repository now keeps four approved brand masters plus the final assets consumed by Windows and the public site.
- Promoted brand logo #1—the transparent-cursor orbit—into the installed Windows app across the tray, taskbar, Start, command panel, installer, and splash surfaces; recording state remains explicit through the tray tooltip, cursor companion, and recording HUD.
- Enlarged logo #1 to fill the tray and taskbar icon canvases, with an optically larger orbit in the 16–64 px Windows frames, and to render at 40 px in command mode and the persistent window title bar; the app now sets its taskbar icon explicitly from the installed brand asset.
- Fixed the cursor companion's ready indicator so its documented bright green core remains visible in light and dark themes instead of being darkened or consumed by its contrast ring.
- Added a first-run field guide with live theme support, all seven mnemonic commands, shortcut readiness, a real command-mode rehearsal, persistent completion, and a Settings entry to rerun it.
- Added a tag-release gate with installed-payload verification, build provenance, hashes, notices, and manifest validation before a friend-facing GitHub Release can publish.
- Added a private GitHub update flow with daily throttling, manual checks, explicit approval, approved-origin and exact hash/size validation, active-work protection, quiet installation, and relaunch.
- Added a responsive GitHub Pages product, privacy, installation, and support site with no analytics, cookies, external fonts, or app-content upload.
- Made the per-user Setup EXE the sole public app download and added hashes, GitHub build provenance, installed-payload verification, and a static update manifest. Releases are intentionally unsigned so the open-source distribution remains free and needs no Azure subscription.
- Documented the one-time manual 0.4.1 install required to move early signature-enforcing builds onto the free unsigned update channel; later updates remain automatic.
- Centralized application and installer versions in `native/Version.props` and versioned onboarding completion for safe future revisions.

- Added camera effects to the self-view: background blur, background replacement (three bundled backgrounds or your own image), face touch-up, and brightness, warmth, and contrast. Everything runs on-device — the person mask comes from a hash-pinned local model, and no frame or derived data leaves the machine.
- Added a squircle self-view shape — a 1:1 plump square — alongside the existing 16:9 rounded rectangle.
- Recording preflight now previews the shape and every effect live in the framing slot, so the look is settled before recording rather than discovered afterwards.
- Added microphone noise suppression and volume auto-levelling for both narration and standalone audio notes. Cleanup runs when the recording is finalized and the raw capture is only replaced on success, so a filter problem can never cost the take.
- Every new effect defaults to off, and with all of them off the camera preview runs the untouched pre-effects path.
- Added a live camera self-view during screen walkthroughs, so the webcam feed is visible on screen while recording instead of only in the saved file.
- Moved camera ownership from FFmpeg to CursorPocket. The webcam is now recorded from the screen at the chosen corner and size, which is what makes a live self-view possible; DirectShow grants a single consumer exclusive use of the device.
- Window-source recordings keep the on-screen self-view but cannot carry it into the saved file, and the preflight now says so before recording starts.
- A camera Windows will not open no longer prevents a recording; the screen still records without a webcam inset.
- Reserved green for live state, the primary action, the current selection, and the command-mode field. Capture kinds are told apart by glyph and file-type tag rather than by colour.
- Rebuilt the shared design system: a full neutral ramp, seven named type styles, and one button template so height, radius, hover, pressed, and disabled match on every surface.
- Moved the Library to top navigation, 52 px rows with file type and size, filter counts, and a detail pane that states kind, size, saved time, and file name.
- Reordered command mode into three primary captures above a rule with the rest below, aligned the key chips into one column, and dropped a trailing icon column that mixed a submenu chevron with category glyphs.
- Reordered recording preflight so its numbered steps read down one column, and gave the second column a framing preview with the camera shown in the slot it will occupy.
- Rebuilt Settings on the Windows settings-card pattern so every non-obvious control explains itself.
- Fixed the Library at the 720×540 minimum window, where capture titles truncated to ten characters and the detail pane overflowed the window.

## 0.3.0 — 2026-08-17

- Added local H.264/AAC screen walkthrough recording with `V` to start or stop.
- Added optional synchronized webcam picture-in-picture with `C` and microphone narration on by default.
- Added display-under-pointer, dragged-region, and previous-window sources plus named device cycling, 30/60 fps, pointer, webcam corner, and webcam size choices.
- Added a visible countdown, named screen/microphone/camera recording state, red dot and tray state, safe Stop/Discard flows, and save-on-quit handling.
- Added crash-tolerant fragmented MP4 storage, startup recovery, validation, and dated `videos` folders under the existing capture location.
- Added a checksum-pinned LGPL FFmpeg sidecar with license/notices, capability validation, and an end-to-end packaged self-test.
- Excluded CursorPocket’s dot, command overlay, settings, toast, and recording bar windows from supported Windows screen captures.
- Made Settings movable, resizable, and vertically scrollable so all recording and startup controls remain reachable on smaller displays.

## 0.2.0 — 2026-08-16

- Added a movable, resizable capture window that remembers its placement.
- Added plain QWER screenshot controls, ASDF audio controls, and 1–4 display captures inside the window.
- Added direct cursor-dot and system-tray access to the capture window and Settings.
- Added an organized single-folder library for screenshots, audio, text, and links.
- Added a visible recording timer and Stop control, plus red recording state on the dot and tray icon.
- Added hidden mode and an optional per-user **Start with Windows** setting.
- Reduced and tightened the borderless cursor dot for direct pointer tracking.
- Fixed stale and conflicting shortcut guidance; the interface now teaches the working window-level keys.
