# Changelog

## Unreleased — workbench UI

- Reworked the shared Uno project-view shell: compact menus/toolbars, project tree and details, separate task cards, bottom inspector, editor bar and status line.
- Added reusable pane chrome, keyboard/pointer splitters, closable/reorderable editor tabs, layout normalization and independent user preference storage.
- Added hierarchical project navigation, ancestor-preserving search, keyboard navigation, Portal task pages, maximize/restore and compact-view drawers.
- Preserved SCL drafts/caret and LAD view state during navigation and refresh; isolated tab-close behavior from neighboring source blocks; recovered drafts without project revision changes.
- Fixed multiline editor initialization and CR/CRLF/LF source handling, including comment termination, diagnostic positions and token-safe renaming in both engines. Added complete-source preservation and executed-value browser checks.
- Replaced unavailable system-font overrides with the host text font and packaged icon glyphs.
- Added explicit per-screen HMI navigation and context-sensitive editor actions.
- Added platform-neutral workbench tests and pointer/keyboard workflows against the real compiled Uno browser application. CI outputs genuine Uno screenshots separately from prototype images.
- Full/pixel-exact TIA Portal UI parity is not claimed. See [workbench behavior and boundaries](docs/workbench.md).

## 0.1.0 — source preview (2026-09-28)

Initial original ControlSpace engineering implementation: eight reusable .NET libraries, Uno desktop/browser shell, shared Skia editors, LAD and SCL subset compilation, deterministic virtual controller, JSON/CSV storage, editable project model and sample conveyor. Added a separately labelled JavaScript interaction prototype, automated engine/browser checks, shared scan fixtures, build/Pages/release workflows, documentation and original screenshots.

The subsequent publishing fix restored the native C# test entry point. GitHub Actions runs 36475400882 and 36475401056 verified the original preview's C#/JavaScript tests, desktop compilation, packaging, Uno WASM startup, and Pages deployment of e71e46a970416bf1a378463a65842eaa3b59f911. Physical-GPU qualification and full compatibility are not implied.
