# Contributing to CursorPocket

CursorPocket should remain a quiet Windows utility that makes a capture obvious, quick, and recoverable. Keep screenshots and audio as the most prominent paths, preserve local-only storage, and avoid adding background collection.

## Set up

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements-build.txt
```

## Before submitting a change

```powershell
.\.venv\Scripts\python.exe -m unittest discover -s tests -v
.\.venv\Scripts\python.exe main.py --self-test
powershell -ExecutionPolicy Bypass -File .\build.ps1
.\dist\CursorPocket.exe --self-test
py -m tools.verify_video_media --ffmpeg .\third_party\ffmpeg\bin\ffmpeg.exe
```

For UI changes, also verify the packaged app on Windows with one and multiple displays when available. Check that the capture window can move and resize, the dot stays close to the pointer, the recording state is unmistakable, and all saved files land under the configured capture folder.

## Project map

- `main.py` — application entry point and self-test
- `cursorpocket/app.py` — Windows UI and capture workflows
- `cursorpocket/hotkeys.py` — global Windows shortcuts
- `cursorpocket/storage.py` — organized local file storage and index
- `cursorpocket/audio.py` — WAV recording
- `cursorpocket/video.py` — FFmpeg command construction and recorder lifecycle
- `cursorpocket/media_devices.py` — Windows camera and microphone discovery
- `cursorpocket/tray.py` — system tray menu and state
- `cursorpocket/settings.py` — persisted user preferences
- `cursorpocket/startup.py` — per-user Windows startup toggle
- `tests/` — unit and interaction-level regression checks

Do not commit `.venv`, `build`, `dist`, user settings, or captures. Distribute compiled executables through a versioned GitHub Release instead of checking binaries into source control.

`build.ps1` downloads a checksum-pinned LGPL FFmpeg sidecar through `tools/fetch_ffmpeg.ps1`. The media verifier encodes screen-only, narrated, webcam, combined, and forcibly interrupted fragmented fixtures. Do not update the FFmpeg URL or hashes without repeating those fixtures plus the real-device screen, microphone, webcam, and combined capture gates and updating `THIRD_PARTY_NOTICES.md`.
