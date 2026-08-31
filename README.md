# CursorPocket

CursorPocket is a local-first Windows capture utility. A tiny green cursor mark follows the pointer, giving you quick access to screenshots, video, audio notes, selected text, and the current browser link. It turns red while audio or video is recording.

Everything is saved to one organized folder on your computer. CursorPocket has no account, analytics, cloud upload, or AI service.

## The everyday workflow

1. Enter command mode by drawing two circles with the mouse, clicking the green dot, choosing **Open command mode** from the tray icon, or pressing `Ctrl + Shift + Space`.
2. A transparent, edge-lit overlay appears on the current display. Tap one of the plain keys shown in its top-right shortcut guide—no `Ctrl` or `Alt` needed.
3. Screenshots open in a full-resolution annotation surface. Audio and video use a clear recording HUD with save/discard controls. Every successful capture gets a receipt and appears immediately in the Library.

Drag anywhere on the panel to move command mode wherever you want it; it reopens in the same place next time, on whichever display your pointer is on. Double-click it to put it back in the top-right corner.

Command mode closes after one action, when you press `Escape`, or automatically after thirty seconds. Click the pulsing logo in its bottom-right corner when you want the full CursorPocket library and settings window. That full window can be moved and resized, and CursorPocket remembers its size and position.

### Command-mode keys

These are individual one-key actions: enter command mode, then tap one key. The letters work only while the overlay is open, so CursorPocket never interferes with normal typing.

| Key | Capture |
| --- | --- |
| `S` | Screenshot; then press `R` region, `W` window, `D` display, `A` all displays, or `P` previous region |
| `V` | Open video preflight to confirm source, microphone, and camera |
| `Shift + V` | Repeat the last valid video setup |
| `A` | Start an audio note with the remembered microphone |
| `T` | Save text highlighted in the previous window |
| `L` | Save the page currently open in your browser |
| `O` | Open the movable, resizable Library |

For text, highlight what you want, open CursorPocket, and press `T`. CursorPocket copies only that active selection. For a link, leave the webpage open, open CursorPocket, and press `L`; it reads the current address from Chrome, Edge, Firefox, Brave, Vivaldi, or Opera. It creates no file when the source is not a complete `http://` or `https://` webpage.

### Keyboard only

Every capture can be dealt with without touching the mouse.

A receipt appears after each capture without stealing focus. Its labelled Open, Play, Mark up, and Show in folder actions can be clicked or reached after focusing the receipt. A focused receipt closes with `Escape`; it never registers global action keys that could interfere with another application.

Inside the Library, ordinary keys work because the window has focus:

| Key | Action |
| --- | --- |
| `↑` `↓` | Move through captures |
| `Enter` | Open the selected capture |
| `Space` | Play or pause |
| `Ctrl + R` | Reveal in the capture folder |
| `Ctrl + C` | Copy the file path |
| `Delete` | Move the selection to the Recycle Bin |
| `Ctrl + A` | Select everything |
| `Ctrl + M` | Fill the window with the preview |
| `Ctrl + 1` … `Ctrl + 6` | Switch filter |

### Screenshot annotation

Every screenshot is copied to your clipboard the moment you take it, so it is ready to paste, and opens a full-resolution markup preview. Saving with marks copies the marked-up version. Choose **Pen**, **Highlight**, **Arrow**, **Rectangle**, or **Text**, pick a color, and draw directly on the image. `Ctrl + Z` undoes the last mark, `Enter` saves, and `Escape` keeps the original screenshot without markup.

### Screen walkthroughs

Open command mode and press `V`. A dedicated preflight shows the source, named microphone with a live level, camera toggle with live preview, pointer choice, frame rate, countdown, free disk space, and one **Start recording** action. CursorPocket releases the preflight preview before the recording self-view opens the same camera, then runs the countdown. During recording, the excluded HUD shows elapsed time, audio activity, active devices, **Stop and save**, and **Discard**.

When the camera is on, a live self-view appears at the corner and size you chose, inside the area being recorded, so you can watch your own feed while you record. You can drag it anywhere inside the area being recorded, and it is what lands in the file — the webcam is recorded from the screen rather than composited separately. A window recording captures only that window, so the self-view stays visible to you but does not appear in the saved file; the preflight says so before you start. If Windows will not open the camera, the recording still runs without it.

CursorPocket saves a normal H.264/AAC MP4 locally. Its dot, command overlay, and recording bar are excluded from the captured video; the camera self-view is deliberately not. Press `Escape` while recording to stop and save. If Windows or the app closes unexpectedly, CursorPocket keeps the fragmented partial recording and attempts to recover it on the next launch. Camera and microphone choices can be changed before recording; Windows privacy settings still control whether each device is available.

## Where things are saved

The default location is `Documents\CursorPocket Captures`. Change it from **Settings** in the capture window or tray menu.

```text
CursorPocket Captures\
├── captures.jsonl
└── 2026-08-16\
    ├── screenshots\
    │   └── 14-05-09_screenshot_a1b2c3.png
    ├── audio\
    │   └── 14-08-33_audio_1a2b3c.wav
    ├── videos\
    │   └── 14-10-41_video_7f8e9d.mp4
    ├── text\
    │   └── 14-06-12_text_d4e5f6.txt
    └── links\
        └── 14-07-21_link_9a8b7c.url
```

`captures.jsonl` is a local index used for recent captures. The actual files remain normal PNG, MP4, WAV, TXT, and URL files.

## Dot, tray, and hidden mode

- **Green dot:** CursorPocket is ready. It follows while the mouse is moving and disappears after a short idle pause, so it never hangs on the screen. Click it to enter command mode.
- **Red cursor and recording HUD:** audio or video is recording. The HUD always exposes save and discard.
- **Tray icon:** right-click it for every capture action, Settings, the capture folder, and Quit.
- **Hidden mode:** choose **Off** in Settings or **Hide cursor companion** in the tray menu. The tray and confirmed global engagement shortcut remain active.
- **Mouse gesture:** draw two circles, clockwise or counter-clockwise, to enter command mode. Small wrist circles and fairly wide sweeps both work, drawn quickly or slowly; what matters is that the path loops round roughly twice in one direction, so ordinary mouse movement does not set it off. It works in hidden mode and can be disabled in Settings.

The dot can also be disabled permanently in Settings. It does not replace the Windows cursor and is excluded from screenshots while capturing.

CursorPocket remains in the Windows notification area while running. Windows may initially place its green icon under the `^` overflow menu near Wi-Fi, battery, and volume; drag the icon onto the visible notification area if you want it shown there permanently.

## Settings and Windows startup

Open **Settings** from the top-right of the capture window or from the tray menu. You can:

- choose the single folder that holds all captures;
- show or hide the cursor dot;
- turn **Start CursorPocket when I sign in** on or off.
- choose whether screen walkthroughs start with microphone, webcam, and a countdown.
- choose the default display/region/window source, 30 or 60 fps, pointer inclusion, and webcam layout.

The final welcome step and Settings both offer startup at sign-in. CursorPocket keeps that choice in one app setting, applies it for the current Windows user, and does not require administrator access.

## First launch

A normal first launch opens a short field guide that explains the cursor companion, confirms the registered global shortcut, shows the seven mnemonic command keys, and opens the real command surface for a rehearsal. It can be skipped and rerun later from **Settings → Help → Run tour**. Background startup stays silent.

## Private updates

CursorPocket reads one static file from the [latest GitHub Release](https://github.com/tanmaysh17/cursorpocket/releases/latest/download/update.json) at most once every 24 hours. The check sends no installation ID, capture information, settings, or analytics, and it fails silently when offline. Disable it or run a manual check under **Settings → Updates**.

When a newer release is available, CursorPocket asks before downloading. It accepts only this project's HTTPS GitHub Release address and verifies the exact byte length, SHA-256, Windows requirement, and version before launching the installer. Installation waits until recording, capture, and annotation work is finished.

Early builds that still require a Tanmay Sharma code-signing certificate cannot install the first unsigned release automatically. Install version 0.4.1 manually from this repository once; automatic updates are seamless from 0.4.1 onward.

## Send feedback

Choose **Feedback** in the main window, select General feedback, Idea, or Problem, and write a short note. CursorPocket shows the app version, Windows version, architecture, and display scale that will accompany it. If a local crash report exists, you can explicitly include a shortened copy after reviewing it; private paths and known capture or device names are removed.

**Open GitHub issue** sends the displayed draft to GitHub to prefill a public issue, but does not post it. Review and submit the issue in your browser. CursorPocket keeps the in-app draft for the rest of the session, and **Copy draft** remains available if the browser cannot open.

## Native Windows build

The current application is a native .NET 8 / WinUI 3 x64 build. It is self-contained and does not require Python on the destination PC.

```powershell
powershell -ExecutionPolicy Bypass -File .\native\build-native.ps1
```

This produces the self-contained app under `artifacts\CursorPocket-win-x64`. With Inno Setup 6 installed it also creates `artifacts\CursorPocket-Setup-x64.exe`. Portable ZIP and MSIX outputs remain optional development artifacts and can be skipped with `-SkipPortableArchive` and `-SkipMsix`.

For another person, the only supported public artifact is `CursorPocket-Setup-x64.exe`: it installs per user, requires no administrator access, and includes the self-contained runtime and FFmpeg. Download it from the [CursorPocket site](https://tanmaysh17.github.io/cursorpocket/) or the latest GitHub Release. Releases are intentionally unsigned so this open-source project can publish entirely through GitHub's free public-repository infrastructure. Windows may therefore show **Unknown publisher** or a Microsoft Defender SmartScreen warning; verify that the download came from this repository before choosing **More info → Run anyway**. The release workflow still installs and checks the finished artifact in CI, publishes SHA-256 hashes, and attests its GitHub build provenance.

Release configuration and the exact version/tag procedure are documented in [RELEASING.md](RELEASING.md).

## Develop locally

Requirements: Windows 10 version 2004 or newer, Windows 11 recommended, and the .NET 8 SDK. Visual Studio 2022 with WinUI tooling is optional.

```powershell
dotnet restore .\native\CursorPocket.Native.sln -p:RuntimeIdentifier=win-x64
dotnet build .\native\CursorPocket.App\CursorPocket.App.csproj -c Debug
dotnet test .\native\CursorPocket.Tests\CursorPocket.Tests.csproj -c Debug
```

The native app preserves the existing `settings.json`, capture root, dated folders, `captures.jsonl`, and ordinary PNG/WAV/MP4/TXT/URL files. It never moves or rewrites existing captures.

### Historical implementation

The unsupported pre-native Python source is retained under `legacy-python/` for archaeology only. It is not tested, packaged, or released. The WinUI 3 application under `native/` is the sole maintained product path.

See [CONTRIBUTING.md](CONTRIBUTING.md) before changing capture behavior or packaging.

## Privacy and current scope

- Screenshots happen only after an explicit capture action.
- The screen, microphone, and webcam are accessed only after you explicitly start a capture; active recording is shown with a red cursor and recording HUD.
- CursorPocket never records keystrokes or continuously records the screen.
- The optional update check reads one public GitHub Pages JSON file daily and sends no custom identifiers or content. The app remains fully usable offline.
- Feedback contacts GitHub only when you choose **Open GitHub issue**. It sends the draft and visible system details needed to prefill a public issue; recent crash details are unchecked by default and included only after you review and select them. CursorPocket never reads or adds captures, the capture index, settings, capture paths, or device names to the draft.
- Text is copied only from the selection you explicitly highlighted. Link capture briefly reads the active supported browser's address bar. Both actions use the Windows clipboard and replace its current contents with the captured text or URL.
- This project focuses on fast local capture. It does not reproduce Clicky's AI assistant or screen-reading features.
