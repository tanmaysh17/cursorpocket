# CursorPocket

CursorPocket is a small, local-first Windows capture utility. A tiny green dot follows the pointer, giving you quick access to screenshots, audio notes, text snippets, and web links. The dot turns red while the microphone is recording.

Everything is saved to one organized folder on your computer. CursorPocket has no account, analytics, cloud upload, or AI service.

## The everyday workflow

1. Enter command mode by drawing two quick, small circles with the mouse, clicking the green dot, choosing **Open command mode** from the tray icon, or pressing `Ctrl + Shift + Space`.
2. A transparent, edge-lit overlay appears on the current display. Tap one of the plain keys shown in its top-right shortcut guide—no `Ctrl` or `Alt` needed.
3. Screenshots open in a compact annotation window; press `Enter` to save immediately or add pen, highlight, arrow, rectangle, or text markup first. Other captures save immediately.

Command mode closes after one action, when you press `Escape`, or automatically after fifteen seconds. Click the pulsing glass button in its bottom-right corner when you want the full CursorPocket library and settings window. That full window can be moved and resized, and CursorPocket remembers its size and position.

### Keys in the capture window

These are individual one-key actions, not combinations: enter command mode and tap one key at a time. For example, tap `Q` by itself for a region screenshot or `1` by itself for display 1. Do not hold `QWER`, `ASDF`, or `1234` together. The plain keys intentionally work only while command mode or the full CursorPocket window is active, so CursorPocket does not interfere with normal typing in other apps.

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
| `T` | Save text highlighted in the previous window |
| `L` | Save the page currently open in your browser |

For text, highlight what you want, open CursorPocket, and press `T`. CursorPocket copies only that active selection. For a link, leave the webpage open, open CursorPocket, and press `L`; it reads the current address from Chrome, Edge, Firefox, Brave, Vivaldi, or Opera. It creates no file when the source is not a complete `http://` or `https://` webpage.

### Screenshot annotation

Every screenshot opens a full-resolution markup preview. Choose **Pen**, **Highlight**, **Arrow**, **Rectangle**, or **Text**, pick a color, and draw directly on the image. `Ctrl + Z` undoes the last mark, `Enter` saves, and `Escape` cancels without creating a file.

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

- **Green dot:** CursorPocket is ready. Click it to enter command mode.
- **Red dot and Stop bar:** audio is recording. Click either one to stop and save.
- **Tray icon:** right-click it for every capture action, Settings, the capture folder, and Quit.
- **Hidden mode:** hides the cursor dot while leaving the tray icon and global shortcuts active. Toggle it from the capture window, tray menu, or `Ctrl + Shift + H`.
- **Mouse gesture:** draw two quick circles, clockwise or counter-clockwise, to enter command mode. It works in hidden mode and can be disabled in Settings.

The dot can also be disabled permanently in Settings. It does not replace the Windows cursor and is excluded from screenshots while capturing.

CursorPocket remains in the Windows notification area while running. Windows may initially place its green icon under the `^` overflow menu near Wi-Fi, battery, and volume; drag the icon onto the visible notification area if you want it shown there permanently.

## Settings and Windows startup

Open **Settings** from the top-right of the capture window or from the tray menu. You can:

- choose the single folder that holds all captures;
- show or hide the cursor dot;
- turn **Start CursorPocket when I sign in** on or off.

Starting with Windows is off until you enable it. CursorPocket uses the current Windows user's startup setting and does not require administrator access.

## Install for everyday use

Build and install CursorPocket with:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

The installer copies the self-contained executable to `%LOCALAPPDATA%\Programs\CursorPocket`, adds **CursorPocket** to the Start menu, enables launch at Windows sign-in, starts the notification-area icon, and attempts to pin the shortcut to the taskbar. On Windows versions that require a manual pin, search for CursorPocket in Start, right-click it, and choose **Pin to taskbar**.

Launching the Start-menu or taskbar shortcut while CursorPocket is already running opens the existing capture window.

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
- Text is copied only from the selection you explicitly highlighted. Link capture briefly reads the active supported browser's address bar. Both actions use the Windows clipboard and replace its current contents with the captured text or URL.
- This project focuses on fast local capture. It does not reproduce Clicky's AI assistant or screen-reading features.
