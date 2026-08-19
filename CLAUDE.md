# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

CursorPocket is a local-first Windows capture utility (screenshots, screen video, audio notes, selected text, browser links). No account, cloud, analytics, or AI services — everything lands in one dated folder tree on disk.

## Two implementations

- `native/` — the **shipping app**: .NET 8 / WinUI 3, x64, self-contained, unpackaged. All product work happens here.
- `cursorpocket/` + `main.py` — the **previous Python implementation, kept only as a behavioral parity reference**. It still runs in CI (unit tests + `--self-test`) but is not shipped in artifacts. Do not add a Python bridge to the native release; do not port new features into it.

`tools/` (FFmpeg fetch, media verifier, icon generation) is shared by both.

## Commands

`dotnet` is often not on `PATH` on the dev machine; it lives at `%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe`. `native/build-native.ps1` resolves this itself.

```powershell
dotnet restore .\native\CursorPocket.Native.sln -p:RuntimeIdentifier=win-x64
dotnet build .\native\CursorPocket.App\CursorPocket.App.csproj -c Debug
dotnet test .\native\CursorPocket.Tests\CursorPocket.Tests.csproj -c Release --no-restore
```

Single test / single class:

```powershell
dotnet test .\native\CursorPocket.Tests\CursorPocket.Tests.csproj --filter "FullyQualifiedName~UtilitySurfaceContractTests"
```

Packaging (portable ZIP always, installer if Inno Setup 6 is present) into `artifacts\`:

```powershell
powershell -ExecutionPolicy Bypass -File .\native\build-native.ps1
```

`build-native.ps1` accepts `-SkipRestore -SkipTests -SkipFfmpeg -RequireInstaller` (CI uses all four).

Python reference checks:

```powershell
.\.venv\Scripts\python.exe -m unittest discover -s tests -v
.\.venv\Scripts\python.exe main.py --self-test
```

FFmpeg media pipeline verification (encodes screen-only, narrated, webcam, combined, and interrupted-fragment fixtures):

```powershell
py -m tools.verify_video_media --ffmpeg .\third_party\ffmpeg\bin\ffmpeg.exe
```

`CONTRIBUTING.md` lists the full pre-submit gate. UI changes additionally require a visual pass on the *installed* build at the active display scale — XAML inspection is not sufficient (see `DESIGN.md` acceptance gate).

## Architecture

**Layering.** `CursorPocket.Core` is plain `net8.0` with no Windows UI dependency: models (`AppSettings`, `CaptureRecord`, `RecordingOptions`), the `ICaptureService`/`IRecordingService`/`ILibraryService`/`IContextCaptureService`/`IHotkeyService` contracts, `CaptureStore`/`SettingsStore`, and pure logic that is unit-testable — `FfmpegCommandBuilder`, `FfmpegDeviceParser`, `MediaDeviceSelector`, `DoubleCircleGestureDetector`, `HotkeyCandidateResolver`, `WindowActivationPolicy`. Put decision logic here, not in code-behind, so it can be tested.

`CursorPocket.App` holds every WinUI window plus the Win32 interop (`Services/NativeMethods.cs`, `WindowPlacement.cs`, `NativeCompanionWindow.cs`) and the real capture/recording implementations.

**Composition.** `AppServices.CreateAsync` builds all runtime singletons (settings, capture store, screenshot/recording/preview/library services, hotkeys, startup) and is reached statically via `App.Services`; UI marshals through `App.DispatcherQueue`. Changing the capture folder in settings tears down and rebuilds `CaptureStore` and everything derived from it — new store-dependent services must be recreated in `AppServices.UpdateSettingsAsync` too.

**Capture flow.** Every capture path writes through `CaptureStore`, which appends to `captures.jsonl` and raises `CaptureCompleted`; `AppServices` re-raises it, and `MainWindow` turns it into a receipt window and a Library refresh. Video uses reserve → fragmented-MP4 partial → finalize, so an unexpected exit leaves a recoverable partial that `CaptureStore.RecoverOrphanedMediaAsync` picks up on next launch.

**Window ownership.** `MainWindow` is the hub: it owns the tray icon (WinForms `ToolStripMenuItem`), the native cursor companion, the command palette, video preflight, recording HUD, receipts, and Library navigation. `MainPage` is the persistent Library/Settings surface. Transient surfaces (companion, palette, region selector, HUD, receipt) are configured through `WindowPlacement.ConfigureUtilityWindow`, which applies topmost, rounded clipping, and `SetWindowDisplayAffinity(WdaExcludeFromCapture)` so CursorPocket's own chrome never appears in captured media. Two surfaces are deliberately *not* excluded: video preflight, so it stays inspectable by screen readers and QA, and the camera self-view, which has to appear in the recording (see **Camera ownership**).

**Recording.** `FfmpegCommandBuilder` builds one screen-capture command (`ddagrab` for a display, `gdigrab` for a region or window) plus an optional `dshow` microphone, producing H.264 (`h264_mf`) / AAC MP4 with `+frag_keyframe+empty_moov+default_base_moof`. `RecordingService` owns the process lifecycle and parses `-progress pipe:2`. Audio-only notes use NAudio → WAV.

**Camera ownership.** The camera is deliberately *not* an FFmpeg input. DirectShow grants a single consumer exclusive use of the device, so an FFmpeg `dshow` camera input made a live self-view impossible. Instead `CameraSelfViewWindow` holds the camera for the whole recording and the webcam reaches the file by being on screen inside the captured rectangle — which is also what lets the user watch their own feed. `CameraSelfViewPlacement` (Core) guarantees the window lands inside that rectangle; anything outside it is absent from the file. Consequences: window-source recordings (`gdigrab hwnd=`) cannot carry the self-view into the file, and the camera must be released the moment recording stops or the next preflight preview finds it busy.

**Storage compatibility contract.** Settings live at `%LOCALAPPDATA%\CursorPocket\settings.json`; captures default to `Documents\CursorPocket Captures\<date>\{screenshots,audio,videos,text,links}` with a `captures.jsonl` index. The native app must keep reading and writing the existing layout and must never move or rewrite existing captures. `SettingsStore.Normalize` is the single place that clamps/repairs persisted values.

## Test strategy (non-obvious)

`CursorPocket.Tests` is a plain xUnit `net8.0-windows` project that cannot instantiate WinUI. It covers UI guarantees two ways:

1. Interop files (`NativeMethods.cs`, `GlobalHotkeyService.cs`, `WindowContextService.cs`) are **`Compile`-linked** from the App project and tested directly.
2. XAML and code-behind files are **copied as `.txt` fixtures** into `Fixtures\` and asserted as text (`ReadFixture("MainWindow.xaml.cs.txt")`). So `UtilitySurfaceContractTests`, `NativePolicyTests`, and `MainPageContractTests` assert on literal source substrings — e.g. `x:Name="BackdropImage"`, `excludeFromCapture: false`, `_x = x + 10`.

Consequence: renaming an element, a local variable, or an offset in App code can break tests without any behavior change. Fix the assertion to match the new intent rather than reverting; and when adding a new window or service whose behavior must be locked in, add the `<None Include=... Link="Fixtures\...">` entry to `CursorPocket.Tests.csproj` first.

## Packaging constraints

- Unpackaged WinUI publish can omit compiled XAML resources, which makes the installed app die with `XamlParseException`. `build-native.ps1` therefore stages `*.xbf`, `*.pri`, and `Assets\` from `TargetDir` after publish and **throws if any required resource is missing** — keep that verification list current when adding top-level XAML.
- The FFmpeg sidecar is a checksum-pinned LGPL build fetched by `tools/fetch_ffmpeg.ps1` (archive + executable SHA-256, and a GPL/nonfree rejection check). Changing the URL or hashes requires re-running the media verifier fixtures *and* real-device screen/mic/webcam/combined capture gates, plus updating `THIRD_PARTY_NOTICES.md`.
- Third-party package notices are copied out of the NuGet cache into `licenses\`; the build fails if one is missing, and CI re-checks the required artifact list.
- Installer target: `%LOCALAPPDATA%\Programs\CursorPocket\CursorPocket.exe`, per-user, no admin. `--background` launches with the Library hidden (tray + companion active).
- Never commit `.venv`, `bin`, `obj`, `artifacts`, `third_party`, settings, or captures.

## Behavioral invariants

These were each fixed after a real regression (`AGENTS.md`, `DESIGN.md`, `HANDOFF.md`):

- Bare command keys (`S/V/A/T/L/O`) are registered **only while the command palette is visible**. Leaving them registered would steal ordinary typing — do not "optimize" that away. The palette itself is constructed once and kept warm while hidden.
- Never restore, resize, unmaximize, or minimize a healthy source window just to return focus to it.
- Release the preflight camera preview before the recording self-view acquires the same device.
- The camera self-view is the one surface that must **not** be capture-excluded — excluding it would drop the webcam from the recording. It is also click-through and never activated.
- Transient surfaces must be opaque edge-to-edge with no transparent WinUI root gutter (that gutter produced the white frame around HUD/receipt).
- Green `#43E08D` = ready/saved/primary; red `#FF5A67` = recording/discard/destructive. Never decorative. Recording state must also be non-color-conveyed.
- No added cloud, sharing, analytics, accounts, decorative glass cards, or raster brand marks without an explicit product decision.
- `Escape` semantics: during recording it stops **and saves**; in region selection and annotation it cancels without losing the original. `Enter` on the annotation surface saves the screenshot with or without marks — it is bound as a root `KeyboardAccelerator` because the drawing `Canvas` cannot take keyboard focus, so a `KeyDown` handler there never fires.
- Command mode is a compact corner panel that steps away from an approaching pointer (`PalettePlacementPolicy` in Core decides where). It holds still while the pointer is on it so rows stay clickable, and clicking outside deliberately does not dismiss it.
- Deletion goes to the Recycle Bin, never a hard delete.

Because the HUD and receipt use capture exclusion, Windows Graphics Capture–based automation screenshots show the source content instead of those surfaces. Verify them by accessibility inspection plus direct visual inspection on an unlocked desktop.
