# CursorPocket native handoff

This is the continuation map for another coding agent. Read `AGENTS.md` and `DESIGN.md` before changing any UI.

## Current product state

- Active branch: `codex/winui-redesign`
- Remote: `https://github.com/tanmaysh17/cursorpocket.git`
- Pull request: `https://github.com/tanmaysh17/cursorpocket/pull/1`
- Native stack: .NET 8, WinUI 3, x64, self-contained unpackaged release
- Installed executable: `%LOCALAPPDATA%\Programs\CursorPocket\CursorPocket.exe`
- User settings: `%LOCALAPPDATA%\CursorPocket\settings.json`
- Default capture root: `%USERPROFILE%\Documents\CursorPocket Captures`
- Release outputs: `artifacts\CursorPocket-Setup-x64.exe` and `artifacts\CursorPocket-portable-win-x64.zip`

The Python app remains only as a behavioral reference. Do not add a Python bridge to the native release.

## Architecture map

- `native/CursorPocket.App`: WinUI windows, activation, tray, cursor companion, native capture/recording implementations.
- `native/CursorPocket.Core`: settings, capture records, storage contracts, path safety, and command-building logic.
- `native/CursorPocket.Tests`: xUnit behavior and UI-surface contract tests. XAML/code-behind files are linked as text fixtures.
- `native/build-native.ps1`: restores, tests, publishes, stages XBF/PRI/assets and FFmpeg notices, then creates ZIP/installer artifacts.
- `DESIGN.md`: required visual and interaction contract.
- `README.md`: user and developer workflow.

Central runtime objects are created by `AppServices`. `MainWindow` owns the tray, companion, command palette, preflight, HUD, receipts, and Library navigation. Captures flow through `CaptureStore.CaptureCompleted` into receipts and the Library.

## Latest latency and transient-surface work

The white frame around the audio/video HUD and saved receipt came from transparent WinUI root gutters. The current implementation:

- fills each utility HWND edge-to-edge with its intended opaque graphite surface;
- removes the receipt stroke and HUD theme shadow;
- asks DWM for rounded native window corners;
- keeps green/red semantics and capture exclusion unchanged.

The latency pass:

- removed deliberate 170 ms command, 130 ms region, and 350 ms FFmpeg-status waits;
- shows the video HUD immediately in a disabled `Starting…` state, enabling actions only once recording is live;
- persists remembered video settings after FFmpeg starts;
- uses a lossless uncompressed BMP for the transient desktop snapshot instead of synchronously compressing PNG;
- constructs the command palette and its message thread once, keeps them warm while hidden, and registers bare command keys only while command mode is visible;
- changes the default video countdown to zero. The installed user setting must also be zero for an existing profile.

Relevant commits:

- `ba35fba` — remove capture chrome and startup latency
- `62dae63` — keep command palette warm between activations

## Build and verification

On this machine `dotnet.exe` is at `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe` and is not always on `PATH`.

```powershell
$dotnet = "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
& $dotnet test .\native\CursorPocket.Tests\CursorPocket.Tests.csproj -c Release --no-restore
& $dotnet build .\native\CursorPocket.App\CursorPocket.App.csproj -c Release -r win-x64 --no-restore
& .\native\build-native.ps1
```

Expected native suite after `62dae63`: 38 passing tests, zero failures. `build-native.ps1` must fail if compiled XBF/PRI/assets or FFmpeg licensing files are absent.

For an installed smoke test:

1. Stop only the process whose executable path equals `%LOCALAPPDATA%\Programs\CursorPocket\CursorPocket.exe`.
2. Run the generated installer silently or interactively.
3. Confirm the installed executable SHA-256 equals `artifacts\CursorPocket-win-x64\CursorPocket.exe`.
4. Start with `--background`; confirm the Library is hidden and the tray/companion remain available.
5. Use `Ctrl+Shift+Space`, then test `A` and `V`. Confirm immediate feedback, no white HWND gutter, readable HUD, save receipt, and Library playback.
6. Confirm ordinary typing is unaffected after the palette hides; bare command hotkeys must be unregistered while hidden.

The recording HUD and receipt use capture exclusion, so Windows Graphics Capture-based automation may show the underlying source rather than those surfaces. Use accessibility inspection for state/actions and direct visual inspection on the unlocked desktop for chrome.

## Known follow-up

- Recheck the current GitHub Actions run. An earlier duplicate run failed before the latest latency commits; inspect its logs rather than assuming the native code failed.
- Measure warm command-mode activation with a native in-process timestamp or ETW if sub-frame numbers are required. Computer-control round trips add substantial automation overhead and are not a trustworthy few-millisecond benchmark.
- Do not optimize by leaving S/V/A/T/L/O registered while the palette is hidden; that would steal normal typing.
- Preserve source-window state: never restore, resize, unmaximize, or minimize a healthy source window merely to return focus.
- Keep camera preview release before FFmpeg camera acquisition.

