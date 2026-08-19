# Changelog

## Unreleased

- Added a live camera self-view during screen walkthroughs, so the webcam feed is visible on screen while recording instead of only in the saved file.
- Moved camera ownership from FFmpeg to CursorPocket. The webcam is now recorded from the screen at the chosen corner and size, which is what makes a live self-view possible; DirectShow grants a single consumer exclusive use of the device.
- Window-source recordings keep the on-screen self-view but cannot carry it into the saved file, and the preflight now says so before recording starts.
- A camera Windows will not open no longer prevents a recording; the screen still records without a webcam inset.
- Replaced the brand mark with a geometric cursor entering a pocket, rendered per icon size so the tray and taskbar icons stay legible at 16–32 px; the mark now turns red end to end while recording instead of carrying a corner badge.
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
