# Repository guidance

## Design system

All CursorPocket UI changes must follow [DESIGN.md](DESIGN.md). Preserve the cursor-field Fluent direction, green-ready/red-recording semantics, Segoe UI Variable typography, 8 px spacing rhythm, and the distinction between transient capture surfaces and the persistent Library/Settings window. Do not add cloud, sharing, analytics, decorative glass cards, or raster UI marks without an explicit product decision.

Any change to command mode, the cursor companion, recording preflight/HUD, receipts, or Library must be checked at the active Windows display scale and must not reintroduce focus-dependent shortcuts, opaque fallback windows, clipped critical controls, or unexplained recording state.

## Pull requests

Do not use the `ship` workflow for this project. Create and update pull requests directly with Git and GitHub tooling, and run the repository's required tests and release checks explicitly.

## Task isolation and completion

Start every new repository work request in a new Git worktree on its own `codex/` branch before editing tracked files. Do not reuse the primary checkout or another task's worktree unless the user explicitly asks to continue that existing task.

After implementing and validating the request, commit only that request's changes, push the branch, and create a pull request before reporting completion. If authentication, network access, or another external blocker prevents the pull request, report that blocker explicitly instead of presenting the request as fully completed.
