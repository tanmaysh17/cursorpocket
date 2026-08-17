# CursorPocket

CursorPocket is a small, local-first Windows capture utility. A tiny green dot follows the pointer, giving you quick access to screenshots, audio notes, text snippets, and web links. The dot turns red while the microphone is recording.

Everything is saved to one organized folder on your computer. CursorPocket has no account, analytics, cloud upload, or AI service.

## The everyday workflow

1. Open the capture window by clicking the green dot, choosing **Open capture window** from the tray icon, or pressing `Ctrl + Shift + Space`.
2. Click an action, or press one of the plain keys shown in the window—no `Ctrl` or `Alt` needed.
3. CursorPocket saves the result automatically and shows a confirmation you can click to open it.

The capture window is a normal Windows window: drag it by its title bar, resize it from any edge, and place it wherever you like. CursorPocket remembers its size and position.

### Keys in the capture window

| Key | Capture |
| --- | --- |
| `Q` | Select a screenshot region |
| `W` | Screenshot the window you were using |
| `E` | Screenshot all displays together |
| `R` | Repeat the last screenshot region |
| `A` | Start or stop an audio note |
| `S` | Stop and save the current audio note |
| `D` | Discard the current audio note |
| `F` | Open the capture folder |
| `1`–`4` | Screenshot that numbered display |
| `T` | Review and save clipboard text |
| `L` | Save a copied web address |

For text, copy it first and press `T`. For a web page, copy its address and press `L`.

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
    ├── text\
    │   └── 14-06-12_text_d4e5f6.txt
    └── links\
        └── 14-07-21_link_9a8b7c.url
```

`captures.jsonl` is a local index used for recent captures. The actual files remain normal PNG, WAV, TXT, and URL files.

## Dot, tray, and hidden mode

- **Green dot:** CursorPocket is ready. Click it to open the capture window.
- **Red dot and Stop bar:** audio is recording. Click either one to stop and save.
- **Tray icon:** right-click it for every capture action, Settings, the capture folder, and Quit.
- **Hidden mode:** hides the cursor dot while leaving the tray icon and global shortcuts active. Toggle it from the capture window, tray menu, or `Ctrl + Shift + H`.

The dot can also be disabled permanently in Settings. It does not replace the Windows cursor and is excluded from screenshots while capturing.

## Settings and Windows startup

Open **Settings** from the top-right of the capture window or from the tray menu. You can:

- choose the single folder that holds all captures;
- show or hide the cursor dot;
- turn **Start CursorPocket when I sign in** on or off.

Starting with Windows is off until you enable it. CursorPocket uses the current Windows user's startup setting and does not require administrator access.

## Install for everyday use

Download or copy `CursorPocket.exe` to a permanent folder, then double-click it. The executable is self-contained; colleagues do not need Python installed.

For this source checkout, build the executable with:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

The result is `dist\CursorPocket.exe`.

## Develop locally

Requirements: Windows 10 or 11 and Python 3.11 or newer.

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements-build.txt
.\.venv\Scripts\python.exe main.py
```

Run the automated checks with:

```powershell
.\.venv\Scripts\python.exe -m unittest discover -s tests -v
.\.venv\Scripts\python.exe main.py --self-test
```

See [CONTRIBUTING.md](CONTRIBUTING.md) before changing capture behavior or packaging.

## Privacy and current scope

- Screenshots happen only after an explicit capture action.
- The microphone is open only while the dot and recording bar are red.
- CursorPocket never records keystrokes or continuously records the screen.
- Text and links come from the clipboard or quick editor; CursorPocket does not inject copy commands into other apps.
- This project focuses on fast local capture. It does not reproduce Clicky's AI assistant or screen-reading features.
