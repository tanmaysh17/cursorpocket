# Repository guidance

## Design system

All CursorPocket UI changes must follow [DESIGN.md](DESIGN.md). Preserve the cursor-field Fluent direction, green-ready/red-recording semantics, Segoe UI Variable typography, 8 px spacing rhythm, and the distinction between transient capture surfaces and the persistent Library/Settings window. Do not add cloud, sharing, analytics, decorative glass cards, or raster UI marks without an explicit product decision.

Any change to command mode, the cursor companion, recording preflight/HUD, receipts, or Library must be checked at the active Windows display scale and must not reintroduce focus-dependent shortcuts, opaque fallback windows, clipped critical controls, or unexplained recording state.

## Pull requests

Do not use the `ship` workflow for this project. Create and update pull requests directly with Git and GitHub tooling, and run the repository's required tests and release checks explicitly.
