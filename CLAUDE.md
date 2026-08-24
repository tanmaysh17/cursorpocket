# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

CursorPocket is a local-first Windows capture utility (screenshots, screen video, audio notes, selected text, browser links). No account, cloud, analytics, or AI services — everything lands in one dated folder tree on disk.

## Implementation

- `native/` — the shipping and sole supported app: .NET 8 / WinUI 3, x64, self-contained. All product work happens here.
- `legacy-python/` — unsupported historical source for archaeology only. It is outside CI and release scope.

`tools/` contains native build helpers for FFmpeg, models, backgrounds, verification, and icons.

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

`build-native.ps1` accepts `-SkipRestore -SkipTests -SkipFfmpeg -SkipModels -RequireInstaller -RequireMsix`. `-SkipModels` skips the segmentation-model fetch used by camera background effects.

FFmpeg media pipeline verification (encodes screen-only, narrated, webcam, combined, and interrupted-fragment fixtures):

```powershell
py -m tools.verify_video_media --ffmpeg .\third_party\ffmpeg\bin\ffmpeg.exe
```

Segmentation model verification — asserts the tensor layout, the 0..1 RGB input, and that **high means person**. The unit tests run against a fake `IPersonMaskModel`, so nothing else catches a model whose mask is inverted, which blurs the person instead of the background. Run it after changing the pinned model (needs `onnxruntime` and `numpy`, dev-only):

```powershell
py -m tools.verify_segmentation_model
```

`CONTRIBUTING.md` lists the full pre-submit gate. UI changes additionally require a visual pass on the *installed* build at the active display scale — XAML inspection is not sufficient (see `DESIGN.md` acceptance gate).

## Architecture

**Layering.** `CursorPocket.Core` is plain `net8.0` with no Windows UI dependency: models (`AppSettings`, `CaptureRecord`, `RecordingOptions`), the `ICaptureService`/`IRecordingService`/`ILibraryService`/`IContextCaptureService`/`IHotkeyService` contracts, `CaptureStore`/`SettingsStore`, and pure logic that is unit-testable — `FfmpegCommandBuilder`, `FfmpegDeviceParser`, `MediaDeviceSelector`, `DoubleCircleGestureDetector`, `HotkeyCandidateResolver`, `WindowActivationPolicy`. Put decision logic here, not in code-behind, so it can be tested.

`CursorPocket.App` holds every WinUI window plus the Win32 interop (`Services/NativeMethods.cs`, `WindowPlacement.cs`, `NativeCompanionWindow.cs`) and the real capture/recording implementations.

**Composition.** `AppServices.CreateAsync` builds all runtime singletons (settings, capture store, screenshot/recording/preview/library services, hotkeys, startup) and is reached statically via `App.Services`; UI marshals through `App.DispatcherQueue`. Changing the capture folder in settings tears down and rebuilds `CaptureStore` and everything derived from it — new store-dependent services must be recreated in `AppServices.UpdateSettingsAsync` too.

**Capture flow.** Every capture path writes through `CaptureStore`, which appends to `captures.jsonl` and raises `CaptureCompleted`; `AppServices` re-raises it, and `MainWindow` turns it into a receipt window and a Library refresh. Video uses reserve → fragmented-MP4 partial → finalize, so an unexpected exit leaves a recoverable partial that `CaptureStore.RecoverOrphanedMediaAsync` picks up on next launch.

**Window ownership.** `MainWindow` is the hub: it owns the tray icon (WinForms `ToolStripMenuItem`), the native cursor companion, the command palette, video preflight, recording HUD, receipts, and Library navigation. `MainPage` is the persistent Library/Settings surface. Transient surfaces (companion, palette, region selector, HUD, receipt) are configured through `WindowPlacement.ConfigureUtilityWindow`, which applies topmost, rounded clipping, and `SetWindowDisplayAffinity(WdaExcludeFromCapture)` so CursorPocket's own chrome never appears in captured media. Two surfaces are deliberately *not* excluded: video preflight, so it stays inspectable by screen readers and QA, and the camera self-view, which has to appear in the recording (see **Camera ownership**).

**Recording.** `FfmpegCommandBuilder` builds one screen-capture command (`ddagrab` for a display, `gdigrab` for a region or window) plus an optional `dshow` microphone, producing H.264 (`h264_mf`) / AAC MP4 with `+frag_keyframe+empty_moov+default_base_moof`. `RecordingService` owns the process lifecycle and parses `-progress pipe:2`. Audio-only notes use NAudio → WAV.

**Physical pixels versus layout coordinates.** Screen capture (`CopyFromScreen`, `gdigrab`, `ddagrab`) works in physical pixels; a XAML `PointerRoutedEventArgs` position is in device-independent pixels. Passing the latter to the former is what made region screenshots lose their right and bottom edges on a scaled display. Anything that turns pointer input into a captured rectangle or a window position takes its coordinates from `WindowPlacement.PointerPosition()` (`GetCursorPos`), which needs no DPI conversion and stays correct across monitors with different scale factors. `RegionSelection` (Core) does the corner math.

**Which screen gets recorded.** Two index spaces must never be confused: `EnumDisplayMonitors` ordering, and the DXGI output ordering that FFmpeg's `ddagrab` `output_idx` uses. Passing the former as the latter silently records a different monitor. `DisplayOutputLocator` resolves a monitor's `\\.\DISPLAYn` device name to a DXGI output index on the default adapter; `RecordingOptions.DisplayOutputIndex` carries only that. When it is null — monitor on another adapter, or DXGI unavailable — `FfmpegCommandBuilder` grabs `Bounds` with `gdigrab` instead, which is slower but always the right pixels. The target is resolved by `WindowPlacement.DisplayTargetUnderPointer()` **when command mode opens**, never when Start is pressed: by then the pointer is over the preflight window, which Windows may have placed on another display.

**Camera ownership.** The camera is deliberately *not* an FFmpeg input. DirectShow grants a single consumer exclusive use of the device, so an FFmpeg `dshow` camera input made a live self-view impossible. Instead `CameraSelfViewWindow` holds the camera for the whole recording and the webcam reaches the file by being on screen inside the captured rectangle — which is also what lets the user watch their own feed. `CameraSelfViewPlacement` (Core) guarantees the window lands inside that rectangle; anything outside it is absent from the file. Consequences: window-source recordings (`gdigrab hwnd=`) cannot carry the self-view into the file, and the camera must be released the moment recording stops or the next preflight preview finds it busy.

**Camera effects.** Because the webcam reaches the file by being on screen, effects are applied by *rendering* them into the self-view — there is no FFmpeg video filter involved. `CameraEffectRenderer` (App) reads frames with a `MediaFrameReader` (`Bgra8`, `AcquisitionMode.Realtime`), hands the packed buffer to `CameraEffectPipeline` (Core), and presents the result through a `SoftwareBitmapSource` on an `Image`. All pixel math lives in `CursorPocket.Core/Media` on `Span<byte>` (LUT, box blur, resize, touch-up, mask composite) and is unit-tested with a fake `IPersonMaskModel`. The person mask comes from `SelfieSegmenter` (App), an ONNX Runtime session over a hash-pinned MediaPipe Selfie Segmenter fetched by `tools/fetch_models.ps1`. Deliberate choices worth keeping: **no Win2D** (v1.4 targets WinAppSDK 1.8, unsupported against our 2.4.0, and CPU is ample at ≤480 px); the plain `MediaPlayerElement` path is kept verbatim and still used whenever no effect is enabled, so the no-effects case carries zero new risk; `FrameArrived` is gated by an interlocked busy flag released only after the frame reaches the screen, giving latest-frame-wins instead of growing latency; and without a mask the background is left *untouched* rather than blurred, because blurring everything would erase the user. The renderer is shared by the self-view and the preflight preview so both show the same thing.

**Microphone cleanup.** `AudioCleanupFilterBuilder` (Core) builds an `-af` chain from stock avfilters (`highpass`, `afftdn`, `loudnorm`). It is applied by the *finalize* passes, not the live capture: `MuxVideoMicrophoneAsync` for narration (where video is already `-c:v copy`) and `TryCleanupAudioNoteAsync` for audio notes. Both write the raw capture first and only replace it when FFmpeg succeeds — audio notes must keep saving with no FFmpeg present at all. `FfmpegCommandBuilder`'s `dshow` mic branch remains dead at runtime (`RecordingService` strips it and muxes NAudio's WAV afterwards), so do not add audio filters there.

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

- Global keys registered while a surface is visible must be **bare only if that surface owns the user's attention**. Command mode does, so its mnemonics are bare; a capture receipt does not — the user carries on working while it is up — so its keys are `Ctrl+Alt`-modified. `PaletteHotkeyService` takes the key set as a constructor argument for exactly this. Anything the user can act on without a mouse should also be reachable through page `KeyboardAccelerator`s where the surface can take focus, since those cannot leak into other applications.
- Bare command keys (`S/V/A/T/L/O`) are registered **only while the command palette is visible**. Leaving them registered would steal ordinary typing — do not "optimize" that away. The palette itself is constructed once and kept warm while hidden.
- Never restore, resize, unmaximize, or minimize a healthy source window just to return focus to it.
- Release the preflight camera preview before the recording self-view acquires the same device.
- The camera self-view is the one surface that must **not** be capture-excluded — excluding it would drop the webcam from the recording. It is draggable (so it accepts pointer input) but never takes activation, and its position is clamped to the recorded rectangle, outside which the webcam is simply absent from the file.
- Transient surfaces must be opaque edge-to-edge with no transparent WinUI root gutter (that gutter produced the white frame around HUD/receipt).
- Green `#43E08D` = ready/saved/primary; red `#FF5A67` = recording/discard/destructive. Never decorative. Recording state must also be non-color-conveyed.
- No added cloud, sharing, analytics, accounts, decorative glass cards, or raster brand marks without an explicit product decision.
- `Escape` semantics: during recording it stops **and saves**; in region selection it cancels without losing the original. In annotation it is **two-stage** — an armed creation tool returns to Select, and `Escape` from Select closes and keeps the original. The end state is unchanged, but it can take two presses, so the status strip says so after the first. `Enter` on the annotation surface saves the screenshot with or without marks. Both are root `KeyboardAccelerator`s because the drawing `Canvas` cannot take keyboard focus, so a `KeyDown` handler there never fires.
- Command mode is a small panel the **user** positions: drag anywhere except a button, double-click to reset. Nothing else may move it — an earlier pointer keep-away behaviour was removed for predictability, so do not reintroduce it. Its position persists as fractions of the display's free space (`CommandPanelPlacement`, clamped in `SettingsStore.Normalize`) rather than screen coordinates, so it survives display, resolution, and DPI changes. Clicking outside deliberately does not dismiss it.
- Whole-surface dragging is tracked from pointer events (`Root.CapturePointer` → `PointerMoved` → `WindowPlacement.MoveTo`), **not** by handing the window to Windows' modal move loop: WinUI consumes the messages that loop needs, so the window never moved. `WindowPlacement` has no `BeginNativeDrag`, and `UtilitySurfaceContractTests` asserts `WmNcLButtonDown` never appears in it. Command mode, the camera self-view, and pinned captures all use the pointer-tracking path. Presses are filtered by walking the visual tree for a `ButtonBase` — the keycaps are Buttons nested inside a Button's content, so trusting routed-event bubbling alone is not enough.
- Command mode is the one transient surface using a `SystemBackdrop` (`DesktopAcrylicBackdrop`) for a live blurred desktop. That does not violate the opaque-root rule: the system paints the whole window, so there is no unpainted WinUI gutter.
- CursorPocket's own capture surfaces must be brought up with `WindowPlacement.ForceForeground`, not `Window.Activate()`. A transient surface has just hidden itself, so the source app already owns the foreground and its foreground lock refuses the change — that is what left the annotation window minimized. Source windows still go through `WindowContextService.RestoreFocus`, which never restores a healthy window.
- Holding both mouse buttons together opens command mode (`ChordActivationDetector`, 700 ms). Two things make it work and must survive edits: the hold is timed by a **timer**, not by mouse messages — a perfectly still hold emits none, so the hook alone would never fire it; and the **second** button-down and every event after it are swallowed (`return 1` from the hook) with a synthetic release sent for the first button. Passing the chord through instead would pop the target app's context menu behind command mode and leave it thinking a button is still down. The first button is never swallowed, so ordinary clicks and drags are untouched.
- `DoubleCircleGestureDetector` is permissive about gesture size and speed and strict only about shape (angular travel, directionality, radius consistency). Loosen size/speed if asked; do not loosen the shape checks, which are what stop ordinary mouse movement from opening command mode. The thresholds have been tuned in both directions already — a much looser pass fired during normal work — so change them by moving one knob at a time and re-checking on a real session.
- Never leave a `SetWindowRgn` clip on a surface *while* the user drags it. A window region takes the window off DWM's fast path and dragging visibly lags; `ConfigureUtilityWindow` already asks DWM for rounded corners. The camera self-view is both shaped and draggable, so it drops its region on pointer-press and re-cuts it on release (`ApplyShapeClip` / `WindowPlacement.ClearWindowRegion`) — square corners show only while the user is actively holding the window. Restore the clip from `PointerCaptureLost` too, since capture can be lost without a release.
- Deletion goes to the Recycle Bin, never a hard delete.
- Every camera effect and audio cleanup option defaults to off, and with all camera effects off the self-view runs the original `MediaPlayer` path. Do not "simplify" by routing the no-effects case through the frame reader.
- Camera effects degrade, never fail: a missing or broken model disables background blur/replacement while colour and touch-up keep working, and a slow machine stretches inference across frames before anything visible drops. A failed effect must never take down a recording.
- Audio cleanup runs at finalize time against a raw capture already on disk. Never filter the live capture — that would put a filter failure between the user and their take.
- The squircle self-view is cut with `CreatePolygonRgn`, the rounded one with `CreateRoundRectRgn`, both through the single `ApplyShapeClip` path so the drag handlers have one thing to restore. Shape also drives aspect: squircle records 1:1, rounded 16:9 (`CameraSelfViewPlacement.Compute`).
- **A WinUI accelerator only fires while something inside the window holds focus.** A `Canvas` cannot take focus and `Focus()` on one silently returns `false`; a `Grid` cannot stand in either, because `IsTabStop` belongs to `Control` and a `Grid` is a `Panel`. The annotation editor wraps its drawing area in a focusable `ContentControl` and focuses it on `Loaded` — not on `Activated`, which fires before the content tree exists. `FocusManager.GetFocusedElement` *throws* rather than returning null when handed a `XamlRoot` that is not ready, so every call is guarded.
- Annotation tool keys are **page accelerators, not global hotkeys**. The editor can take focus, so accelerators are strictly better: they cannot leak a keystroke into another application and cannot fail to register because command mode still holds a key. The apparent clash with command mode's bare keys is inert — the palette disables its key set on hide, and `MainWindow` hides it before any capture surface opens. Do not "unify" the two key sets.
- Declaring an accelerator in XAML and mapping its key in the handler are two separate edits, and missing the second is silent: the accelerator fires, the switch returns null, and the key does nothing. Four tools shipped that way once. A test reads the declarations out of the XAML and insists each has a mapping.
- WinUI only parses the abbreviated `"M4,20 L20,4"` path syntax through `Path.Data`'s own type converter. `PathGeometry.Figures` rejects it at compile time (`WMC0055`) and an `x:String` resource reaches `Path.Data` untyped and fails at load, so icon geometry lives inline at each use site.
- Redaction defaults to **solid**. Pixelation and blur derive their output from the pixels underneath, so for a short string the glyph shapes leak through the block averages. Nothing in the redaction path may read a clock or `System.Random` — its sequence is not guaranteed stable across .NET versions, so a framework bump would silently change existing output.
- Recognised text never reaches the clipboard unasked, and the OCR service contains no clipboard call at all. Scaling a small region past the engine's 40 px floor does not help and can hurt: Windows OCR wants document-like input, and an 1882x160 strip comes back empty whether it was upscaled or drawn at that size.
- Marks overwrite the capture in place; **a crop, a cut, or a backdrop writes a new capture instead.** Those delete pixels, and a save overwrites rather than deleting, so there would be no Recycle Bin copy to fall back on. `SaveTarget.For` decides it in Core. The new record carries its own dimensions, so `captures.jsonl` stays append-only and nothing repairs a stale line.
- Pins are created only by explicit action, are never restored after a restart, are deliberately **not** capture-excluded, and must never take a scoped `Escape` lease — a pin can sit on screen for hours, and holding the topmost lease would steal `Escape` from every application including a live recording.
- `build-native.ps1` throws if a listed compiled XAML resource is missing. **Add every new top-level XAML to `$requiredWinUiResources`**, or the installed build dies with `XamlParseException` while the Debug build is fine.
- `build-native.ps1` sets the publish flags itself, overriding the csproj: `-p:PublishTrimmed=false` and `-p:PublishReadyToRun=true`. Nothing that ships is trimmed, so a trimming-only concern is moot; and editing the csproj alone changes nothing about the artifact.

Because the HUD and receipt use capture exclusion, Windows Graphics Capture–based automation screenshots show the source content instead of those surfaces. Verify them by accessibility inspection plus direct visual inspection on an unlocked desktop.
