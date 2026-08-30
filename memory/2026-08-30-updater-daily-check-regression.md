# Debug report: seamless daily updater

Date: 2026-08-30

## DEBUG REPORT

Symptom: CursorPocket scheduled a delayed background update check, but it did not reliably satisfy the product contract. Failed or malformed attempts were not written to `LastUpdateCheckAt`, so an offline machine could contact GitHub again on every relaunch. The scheduler also stopped after its first check, so a tray-resident process did not check again on later days. An available update receipt could appear during recording/capture/annotation work, and choosing install after work became active discarded the offer instead of deferring it. The completion audit found one final gap: if a scheduled check returned `Disabled`, enabling automatic checks later did not wake the loop, so the first enabled check could remain almost 24 hours away.

Root cause: `ApplicationUpdateCoordinator.CheckAsync` persisted only `Available` and `UpToDate` results, while the original `ScheduleAutomaticCheck` ran a one-shot task. `MainWindow` raised the update receipt immediately and treated active work as an error only after the user selected install. `LaunchInstaller` wrote `pending-update.txt` before starting the process but did not remove it when process creation failed. In the follow-up path, `RunAutomaticChecksAsync` correctly chose the normal daily interval after `Disabled`, but `MainWindow.Services_SettingsChanged` did not reschedule the coordinator when `AutomaticallyCheckForUpdates` changed from false to true.

Fix:

- `native/CursorPocket.App/Services/ApplicationUpdateCoordinator.cs:50` runs the delayed check on a cancellable daily loop. A throttled startup check waits only until the persisted 24-hour boundary rather than adding another full day.
- `native/CursorPocket.App/Services/ApplicationUpdateCoordinator.cs:121` persists every real check attempt, including offline and invalid-manifest outcomes, while leaving `Disabled` and `Throttled` untouched. Manual checks still bypass the throttle and count as the day's attempt.
- `native/CursorPocket.App/Services/ApplicationUpdateCoordinator.cs:47` defines the false-to-true transition predicate used by the settings path.
- `native/CursorPocket.App/MainWindow.xaml.cs:78` initializes the remembered automatic-update setting before subscribing to changes. `native/CursorPocket.App/MainWindow.xaml.cs:1074` updates that value for every settings event and calls the existing no-argument scheduler only on false-to-true, retaining its short 30-second non-blocking delay. Unrelated settings events, timestamp persistence, true-to-true, and true-to-false changes do not restart the loop.
- `native/CursorPocket.App/MainWindow.xaml.cs:908` queues update offers and presents the existing non-activating receipt only after recording, capture, region selection, preflight, annotation, and onboarding are idle. If work starts after the receipt appears, selecting install re-queues the offer instead of raising or activating the Library.
- `native/CursorPocket.App/MainWindow.xaml.cs:922` preserves explicit `Download and install`, `Release notes`, and `Later` actions. Nothing downloads without approval.
- `native/CursorPocket.App/Services/ApplicationUpdateCoordinator.cs:187` still writes a pending target, launches the verified installer with `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELAUNCH`, and relies on the Inno `[Run]` rule to relaunch only for updater-initiated installs. A launch failure removes the pending marker, and `MainWindow` reports `Win32Exception` through an error receipt.

Evidence:

- Installed app inspected at `%LOCALAPPDATA%\Programs\CursorPocket\CursorPocket.exe`: product version `0.4.15`, file version `0.4.15.0`.
- Persisted settings inspected without mutation: `automatically_check_for_updates = true`, `last_update_check_at = 2026-08-30 13:00:53 -07:00`.
- Live manifest request: `https://github.com/tanmaysh17/cursorpocket/releases/latest/download/update.json` redirected to the `v0.4.15` release asset and returned HTTP 200 with a valid 448-byte manifest for `CursorPocket-Setup-x64.exe`, version `0.4.15`, SHA-256, byte length, Windows floor, notes URL, and publication time.
- Before the fix, `Failed_automatic_attempt_is_persisted_and_throttles_the_next_check` failed at `Assert.NotNull(settings.Current.LastUpdateCheckAt)` after the first offline attempt.
- Before the follow-up fix, `Enabling_automatic_checks_reschedules_the_background_loop` failed because `MainWindow` contained no transition-aware reschedule call.
- Focused updater tests after all fixes: 26 passed, 0 failed.
- Full native test suite against the final onboarding/updater merge: 566 passed, 0 failed, 0 skipped.
- `dotnet build native/CursorPocket.Native.sln -c Release --no-restore -p:UseSharedCompilation=false -nodeReuse:false`: succeeded with 0 warnings and 0 errors.
- The self-contained publish/package pass was intentionally not run in this follow-up, per the completion-audit request. The native solution itself built successfully.

Regression tests:

- `native/CursorPocket.Tests/ApplicationUpdateTests.cs:98` proves an offline automatic attempt is persisted and the immediate next check is throttled without a second HTTP request.
- `native/CursorPocket.Tests/ApplicationUpdateTests.cs:121` proves the scheduler checks again while one process remains resident.
- `native/CursorPocket.Tests/ApplicationUpdateTests.cs:140` proves the settings handler initializes from the current preference and invokes the existing short scheduler path when enabling automatic checks.
- `native/CursorPocket.Tests/ApplicationUpdateTests.cs:164` exercises all four boolean transitions and proves rescheduling is limited to false-to-true.
- `native/CursorPocket.Tests/ApplicationUpdateTests.cs:175` proves a failed installer launch cannot leave a false pending-update result.
- `native/CursorPocket.Tests/ApplicationUpdateTests.cs:193` locks the active-work deferral, receipt actions, and installer relaunch contract.

Related: The release transport was recently repaired in commits `35a55ba` (read updates from GitHub Releases), `96428ff` (publish unsigned/free updates), `126f4ca` (missing-tag handling), and `a23e9a9` (repeatable releases). The live manifest path and release workflow are healthy; these bugs were local scheduling/persistence and prompt orchestration, not release publication.

Status: DONE. The automatic updater now checks at most daily per real attempt, continues across a tray-resident process, resumes through the existing short delay when the preference is newly enabled, stays dormant for unrelated settings changes, defers prompts during active work, preserves explicit approval, cleans up failed installer launches, and passes focused tests, the full native suite, and the native solution build.
