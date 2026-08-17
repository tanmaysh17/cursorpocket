# CursorPocket Screen + Webcam Recording Plan

Status: implemented; final visual click-through pending an unlocked Windows desktop
Target: Windows 10 22H2 and Windows 11, x64  
Primary user outcome: start a narrated screen walkthrough in two actions, optionally include a webcam bubble, and always find the finished video beside the rest of the day's captures.

## Product decisions

- Enter command mode as usual, then tap `V` to record the display under the pointer. Tap `V` again, click the red dot/Stop bar, or use the tray menu to stop and save.
- Microphone is on by default for walkthroughs. Webcam is off by default and remembers the user's last choice.
- The full CursorPocket window exposes the deliberate workflow: source (`Display`, `Region`, or `Window`), microphone, webcam, camera device, camera corner, and quality.
- A three-second countdown provides time to return to the subject. It can be disabled in Settings.
- During recording, CursorPocket shows a red dot and a compact red Stop bar with elapsed time and explicit `Screen`, `Mic`, and `Camera` status. CursorPocket's own controls are excluded from the captured video.
- A successful recording creates a normal H.264/AAC `.mp4` in the existing capture folder. Nothing is uploaded.
- Audio notes and video recordings are mutually exclusive. Other capture actions are temporarily disabled during video recording so CursorPocket overlays and competing device access cannot corrupt the walkthrough.

## Why this backend

| Approach | Benefits | Costs | Decision |
| --- | --- | --- | --- |
| FFmpeg sidecar controlled by Python | One synchronized screen/camera/mic process; mature device enumeration, compositing, H.264/AAC muxing, and recovery options; small application-side API | Adds a large bundled binary, LGPL notices/source obligations, and a legacy DirectShow camera path | **Use for v1 behind a replaceable backend interface** |
| Native C++/WinRT helper using Windows Graphics Capture + Media Foundation | Best Windows-native capture path and avoids FFmpeg distribution | Requires a new native toolchain and substantial custom work for frame composition, camera/mic synchronization, encoding, IPC, and recovery | Reconsider after v1 performance data |
| Python frame loop using screen/camera libraries + PyAV | Keeps most code in Python | More dependencies, worse 4K performance, difficult audio/video synchronization, and a similarly large media bundle | Reject |
| Launch Windows Snipping Tool | Very little code and native capture UI | Unpackaged apps cannot reliably receive the result, and it does not provide CursorPocket's webcam composition | Reject |

The recorder will prefer FFmpeg's Desktop Duplication capture for full displays and fall back to GDI capture when required. Region and window capture will use GDI capture because it directly supports bounds and window handles. Encoding will prefer Windows Media Foundation's `h264_mf` encoder and use `nv12` input. The backend must probe these capabilities rather than assume they exist.

## User journey

### Fast path

1. Open command mode with two mouse circles, the green dot, tray icon, or `Ctrl + Shift + Space`.
2. Tap `V`.
3. See a three-second edge countdown; webcam and microphone states are named in the corner.
4. Record. CursorPocket's dot and Stop bar turn red but are not present in the video.
5. Tap `V` from command mode, click Stop, click the red dot, or choose **Stop and save video** in the tray.
6. Receive `Video saved · 3:42 · Click to play`; the file is immediately present in the day's `videos` folder.

### Configured path

The full interface gains a `VIDEO · V` section above Audio:

- `Record display` is the primary button.
- `Region` and `Window` are secondary source choices.
- `Mic` and `Camera` are obvious on/off controls with device names, not icon-only toggles.
- Enabling Camera reveals a live framing preview, camera selector, corner selector, and small/medium/large size.
- A short line states the exact result before starting, for example: `Display 2 · 1080p · Mic on · Camera bottom-right`.

Settings holds slower-changing choices: default source, 3-second countdown, 30/60 fps, 1080p/native output, pointer inclusion, microphone device, camera device, camera position/size, and whether webcam is enabled for the next recording.

## Keyboard and command-mode behavior

- Add `V: Record display` to the top-right command guide.
- `V` is a state-aware action: idle starts the last/default video setup; recording stops and saves.
- Add `C: Camera on/off for next video`. It changes the remembered setup only while idle. Camera composition is fixed once a recording starts.
- Existing plain-key behavior remains scoped to command mode or the full interface, so normal typing is unaffected.
- If command mode opens while recording, it shows only recording status and stop/discard actions. It must not start a second capture.
- No new modifier-heavy global shortcut is required.

## Recording state machine

```text
IDLE
  └─ start request
      └─ PREFLIGHT (backend, source, disk, mic, camera)
          ├─ error ────────────────> IDLE + actionable rescue
          └─ ready
              └─ COUNTDOWN
                  ├─ cancel ───────> IDLE
                  └─ start
                      └─ RECORDING
                          ├─ save ──> STOPPING ─> FINALIZING ─> IDLE
                          ├─ discard > STOPPING ─> DISCARDING ─> IDLE
                          └─ failure > RECOVERY_REQUIRED ─────> IDLE
```

Only the recorder worker owns the FFmpeg process. Tkinter receives immutable events through the existing application event queue; no worker thread updates widgets directly. Every public transition is idempotent so rapid double-clicks or duplicate tray/key events cannot start or stop twice.

## Proposed code structure

### New files

- `cursorpocket/video.py`
  - `RecordingState` enum.
  - `VideoSource`, `VideoOptions`, `VideoDevice`, and `VideoResult` dataclasses.
  - `VideoBackend` protocol so FFmpeg can later be replaced by a native backend.
  - `FFmpegVideoRecorder` for capability probing, command construction, lifecycle, progress parsing, graceful stop, forced-stop fallback, and cleanup.
- `cursorpocket/media_devices.py`
  - Parse FFmpeg device enumeration into stable camera/microphone choices.
  - Preserve both display name and alternative device identifier where available.
- `cursorpocket/video_preview.py`
  - Own the short-lived camera preview process and deliver preview frames to Tkinter.
- `tests/test_video.py`
  - State, command, device parsing, lifecycle, recovery, and validation tests.
- `THIRD_PARTY_NOTICES.md` and `third_party/ffmpeg/`
  - Exact FFmpeg license, build configuration, pinned checksum, corresponding-source instructions, and binary provenance.

### Existing files to extend

- `cursorpocket/app.py`: video section, command actions, countdown, generalized recording indicator, application state coordination, toasts, history icon, quit handling.
- `cursorpocket/storage.py`: reserve/finalize/discard/recover video files and add the `videos` category.
- `cursorpocket/settings.py`: backwards-compatible video preferences and selected device identifiers.
- `cursorpocket/windows.py`: mark CursorPocket top-level controls with `WDA_EXCLUDEFROMCAPTURE` where Windows supports it.
- `cursorpocket/tray.py`: start/stop video item, recording-specific title/icon, and disabled conflicting actions.
- `main.py`: self-test a short synthetic video fixture and manifest entry without hardware.
- `build.ps1`: verify and package pinned FFmpeg/FFprobe binaries and notices.
- `install.ps1`: install the media sidecars beside CursorPocket and retain startup behavior.
- `README.md`, `PRODUCT.md`, `CHANGELOG.md`: workflow, privacy disclosure, requirements, shortcuts, saved layout, and limitations.

## FFmpeg pipeline

The backend builds arguments as an array and never passes a user/device string through a shell.

Inputs:

- Full display: preferred `ddagrab` with selected output index; fallback `gdigrab` with display bounds, including negative multi-monitor coordinates.
- Region: `gdigrab` with `offset_x`, `offset_y`, and even-numbered `video_size`.
- Window: `gdigrab` using the captured foreground `hwnd` and a preflight check that the window is not minimized.
- Webcam/microphone: one DirectShow input when both are enabled to improve synchronization; separate microphone input when webcam is off.

Filters:

- Normalize timestamps at zero and keep constant output frame rate.
- Downscale only when the selected quality limit requires it; never upscale.
- Convert to even dimensions and `nv12` before Media Foundation encoding.
- Crop and scale the webcam to a circular or softly rounded bubble, place it in the remembered corner with safe margins, and preserve the screen aspect ratio.
- Use audio resampling with bounded asynchronous correction to prevent narration drift.

Output:

- Video: H.264 through `h264_mf`, 30 fps by default, quality-based rate control, keyframe every two seconds.
- Audio: AAC, 48 kHz, mono by default for narration.
- Container: fragmented MP4 written as `.partial.mp4`, then atomically renamed to `.mp4` after FFprobe validation.
- Stop: send a graceful FFmpeg quit request, wait up to eight seconds, then terminate and enter recovery if it does not close.

## Storage and recovery

```text
CursorPocket Captures\
├── captures.jsonl
├── .in-progress\
│   ├── <capture-id>.partial.mp4
│   └── <capture-id>.json
└── 2026-08-17\
    └── videos\
        └── 14-32-10_video_a1b2c3.mp4
```

The manifest receives a `video` record only after FFprobe confirms at least one decodable video stream, non-zero dimensions, and positive duration. Metadata includes duration, dimensions, fps, source kind/bounds, cursor inclusion, microphone/webcam flags, selected device identifiers, codec, and whether the result was recovered.

On startup, CursorPocket scans `.in-progress`:

- valid fragmented MP4: offer/finalize as `Recovered video`;
- zero-byte or undecodable file: offer deletion and explain that recording was interrupted;
- active PID marker: do not touch it;
- stale metadata without media: clean it after confirmation.

Disk space is checked before start and monitored while recording. At less than 1 GB free, preflight warns; at a critical threshold, recording stops and attempts to preserve the partial file.

## Error and rescue behavior

| Failure | User-facing response | Recovery |
| --- | --- | --- |
| FFmpeg missing or wrong build | `Video recorder is unavailable · CursorPocket's media component is missing` | Button opens repair/reinstall instructions; screenshots/audio continue working |
| Screen backend unsupported | `This display could not be recorded with the fast capture path` | Automatically retry GDI once, then show the captured backend error |
| Camera absent, busy, or denied | `Camera couldn't start · It may be in use or blocked by Windows privacy settings` | Start screen + mic without camera, retry, or open `ms-settings:privacy-webcam` |
| Microphone absent, busy, or denied | `Microphone couldn't start` | Start a muted video, retry, or open `ms-settings:privacy-microphone`; never silently omit narration |
| Source window minimized/closed | `That window is no longer recordable` | Return to source selection; no file is created |
| Display disconnected or resolution changes | `The recorded display changed` | Stop and finalize what is valid; explain why recording ended |
| Encoder unavailable | `Windows H.264 encoding couldn't start` | Retry approved software fallback only if the bundled build provides it |
| Disk full/write failure | `Recording stopped because the capture drive is full` | Preserve and validate the partial file, then open the destination folder |
| FFmpeg hangs on stop | `CursorPocket is recovering the video` | Terminate after timeout, validate the fragmented file, and keep it out of the manifest until valid |
| App exits/restarts during recording | Explicit save/discard prompt; crash has no prompt | Recover `.partial.mp4` on next launch |

## Packaging and licensing

- Pin a numbered FFmpeg release/build and its SHA-256 checksum; do not package a mutable `latest` download.
- Use an LGPL-only Windows x64 build. Do not use `--enable-gpl`, `--enable-nonfree`, `libx264`, or `libx265`.
- Include FFmpeg's license, exact build configuration, provenance, and a corresponding-source download alongside every distributed CursorPocket build.
- Keep FFmpeg as a separate executable invoked through a subprocess; do not import/link its libraries into CursorPocket.
- The build must fail if the binary hash, license files, `gdigrab`/`ddagrab`/`dshow` inputs, `h264_mf`, AAC encoder, or MP4 muxer are missing.
- Expect the installed footprint to grow substantially. Record the before/after installer size in the release checklist.

## Privacy and trust

- Screen, camera, and microphone access begins only after an explicit record action and visible countdown.
- Red, non-color-only status remains visible throughout recording.
- Webcam and microphone state is named before and during recording.
- No hidden/background video capture, no cloud upload, no analytics, and no automatic recording at startup.
- CursorPocket recording controls use `WDA_EXCLUDEFROMCAPTURE` on Windows 10 2004+; verify exclusion with both capture backends.
- Protected/DRM content may appear blank and is not bypassed.

## Test plan and acceptance gates

### Automated unit tests

- Argument construction for every source and device combination, including spaces, quotes, Unicode names, negative display coordinates, odd dimensions, and camera corner/size.
- Device-list parsing for duplicate names and alternative identifiers.
- All valid and invalid state transitions, including rapid duplicate events.
- Fake-process lifecycle: readiness, progress, graceful stop, stop timeout, crash, discard, and stale event rejection.
- Storage reserve/finalize/discard/recovery and manifest compatibility with older records/settings.
- UI dispatch for `V` and `C`, disabled conflicting actions, correct status copy, and tray state.
- Window-exclusion API behavior and safe fallback on older Windows.

### Media integration tests

- Use FFmpeg synthetic color/video and sine/audio sources so CI does not require a real camera or microphone.
- Generate screen-only, screen+mic, screen+camera, and screen+camera+mic fixtures.
- Validate every output with FFprobe: streams, codecs, duration, dimensions, frame rate, audio sample rate, and non-zero packets.
- Interrupt a fragmented recording mid-process and prove the recovery path produces a playable result.

### Windows hardware matrix

- One 1080p display; 150% DPI.
- Two displays with the secondary to the left (negative coordinates).
- 4K display at 30 fps and 60 fps.
- Integrated webcam, USB webcam, no webcam, camera already in use, and camera privacy denied.
- Default mic, USB/Bluetooth mic, no mic, mic already in use, and microphone privacy denied.
- Source window resized, minimized, closed, or moved between displays.
- Screen lock, sleep/resume, display disconnect, app shutdown, and forced process termination.

### Release acceptance

- A new user can record the display with narration in `Ctrl + Shift + Space`, `V`, then one Stop click.
- Webcam inclusion is clear before recording and appears in the selected corner in the final file.
- CursorPocket controls never appear in supported screen recordings.
- A 30-minute 1080p30 walkthrough has less than 150 ms perceived audio/video drift, no progressive drift, and fewer than 2% dropped/duplicated frames on the target machine.
- The finished MP4 opens in Windows Media Player, PowerPoint, Teams, Chrome, and Edge.
- A forced app termination leaves a recoverable or explicitly diagnosed partial file, never a false successful manifest entry.
- Existing screenshot, annotation, audio, text, link, startup, tray, hidden-mode, and shortcut tests remain green.
- The installed build works on a clean Windows account without Python, FFmpeg, .NET, Visual Studio, or administrator rights already installed.

## Implementation sequence

1. **Backend spike and legal gate**
   - Pin/probe an LGPL FFmpeg build on the target machine.
   - Prove four 15-second fixtures: screen; screen+mic; screen+camera; screen+camera+mic.
   - Measure 1080p30 and 4K30 CPU, dropped frames, file size, and A/V drift.
   - Confirm `WDA_EXCLUDEFROMCAPTURE` works with the selected screen backends.
   - Stop if H.264 Media Foundation, camera/mic synchronization, window exclusion, or redistribution requirements fail.
2. **Recorder core**
   - Add `video.py`, typed configuration/state, process lifecycle, event parsing, device enumeration, and deterministic command tests.
3. **Crash-safe storage**
   - Add video destinations, partial metadata, FFprobe validation, atomic finalization, recovery, and self-test fixture.
4. **Application integration**
   - Generalize recording state/indicator, enforce audio-video mutual exclusion, add quit/restart recovery, and wire command/tray events.
5. **Everyday UX**
   - Add `V`/`C`, countdown, full-window Video section, device controls, camera preview, actionable privacy/settings rescue, and clear state copy.
6. **Packaging and docs**
   - Bundle pinned media binaries/notices, update build/install scripts, privacy docs, folder tree, shortcuts, changelog, and contributor instructions.
7. **Verification and dogfood**
   - Run automated suites, hardware matrix, 30-minute soak, clean-account install, playback compatibility, forced-crash recovery, and regression checks.
8. **Release**
   - Commit in reviewable units, tag the release, build colleague-ready artifacts, publish checksums and corresponding FFmpeg source information.

## Explicitly not in v1

- System/desktop audio capture.
- Pause/resume within one video.
- Switching camera, source, or camera position during a recording.
- Multiple cameras or picture-in-picture screen sources.
- Background removal, virtual backgrounds, captions, transcription, editing, or cloud sharing.
- macOS/Linux support.

These are deferred so the first release can make the core promise dependable: local screen walkthroughs with synchronized narration and an optional webcam, started and found as easily as every other CursorPocket capture.

## Implementation record

- The legal/backend gate uses BtbN `autobuild-2026-08-17-13-05`, artifact `ffmpeg-n8.1.2-44-g7c533d0f86-win64-lgpl-8.1.zip`, archive SHA-256 `BDA492675BDB354AC55F93B96AF2DBB35BABEF7DE264C37D4FF83E022831B19D`, and executable SHA-256 `E8E106D6F6A4166747FBD7374FBF47FFC4D2DD883520C3558FEEAC0281A2712D`.
- Four real-device 15-second gates passed on the target machine: screen, screen + microphone, screen + webcam, and screen + microphone + webcam. The combined gate produced 449 of 450 expected frames with 15.04 seconds of audio.
- DirectShow microphone and camera inputs are intentionally separate and opened before Desktop Duplication. The planned combined DirectShow input hung during shutdown on the target hardware; the separate-input pipeline stopped cleanly and synchronized correctly.
- The frozen app packages only `ffmpeg.exe`; the same executable performs media validation, avoiding a second 114 MB `ffprobe.exe` while preserving the validation gate.
- The optional camera is a clean 16:9 picture-in-picture in v1. Live camera preview and advanced size/corner selectors remain follow-up polish; the everyday `C` toggle and remembered corner are implemented.
- Automated storage, lifecycle, shortcut, tray, settings compatibility, window-affinity, and synthetic H.264/AAC self-tests pass. The final computer-controlled visual click-through could not run while the Windows desktop was locked; it must be repeated after unlock before publishing a release artifact.

## Technical references

- Microsoft Windows Graphics Capture: https://learn.microsoft.com/windows/apps/develop/media-authoring-processing/screen-capture
- Microsoft capture-item Win32 interop: https://learn.microsoft.com/windows/win32/api/windows.graphics.capture.interop/nn-windows-graphics-capture-interop-igraphicscaptureiteminterop
- Microsoft camera privacy handling: https://learn.microsoft.com/windows/apps/develop/camera/camera-privacy-setting
- Microsoft `WDA_EXCLUDEFROMCAPTURE`: https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity
- FFmpeg Windows input devices (`gdigrab`, `dshow`): https://ffmpeg.org/ffmpeg-devices.html
- FFmpeg Media Foundation encoders: https://ffmpeg.org/ffmpeg-codecs.html#MediaFoundation
- FFmpeg fragmented MP4 behavior: https://ffmpeg.org/ffmpeg-formats.html#Fragmentation
- FFmpeg licensing guidance: https://ffmpeg.org/legal.html
- BtbN LGPL Windows build variants: https://github.com/BtbN/FFmpeg-Builds
