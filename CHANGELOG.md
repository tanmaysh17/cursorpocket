# Changelog

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
