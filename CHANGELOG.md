# Changelog

## Unreleased — configurable HMI runtime

- Added optional immutable runtime options, numeric Output/Input/InputOutput modes, validated ranges, decimal precision and units.
- Added guarded one-shot simulator input/marker writes, SetBit/ResetBit/ToggleBit/SetValue button actions and bounded screen navigation/history.
- Added release-over-target dispatch and invalidation of pending numeric entries across screen/session/controller lifecycle changes.
- Protected navigation references during deletion, duplication and clipboard paste; retained tag rename and undo semantics.
- Shared configured numeric drawing between the public renderer and Uno designer/runtime, without changing legacy object defaults.
- Added native source regressions, eight browser scenario groups, an operator sample and a runtime guide. See commit-specific checks for validation; these entries are not test-pass evidence.

## Unreleased — HMI basic graphics

- Added rectangle, ellipse and line objects to the shared Uno toolbox and both Skia HMI rendering APIs.
- Added filled-ellipse and line-stroke hit tests, including transparent-corner click-through and zoom-correct line tolerance.
- Shapes participate in existing geometry edits, group transforms, clipboard, ordering and undo; invalid tag bindings are rejected.
- Added native geometry/roundtrip/rendering regressions and real pointer/clipboard/browser shape workflows.
- Older builds and the separate six-object JavaScript prototype do not accept shape-bearing projects; detailed line/style limitations are in docs/hmi-design.md.

## Unreleased — project navigation responsiveness

- Recycled project-tree rows only for the viewport and overscan; retained an indexed outline across source, tag-value, network and HMI-object edits which do not alter it.
- Added linear ancestor-preserving search, result counts, empty-search feedback, a clear-search button, explicit active-editor reveal and metadata in Details.
- Kept keyboard selection, focus and scrolling synchronized for Home/End, PageUp/PageDown and parent/child navigation. Initial-letter keys cycle visible matching captions without opening documents or changing the filter.
- Added native navigation regressions and actual Uno browser tests importing 1,000 blocks plus 1,000 screens. These measure bounded controls and correct behavior, not a TIA-relative or end-to-end latency improvement.

## Unreleased — performance and rendering quality

- Removed JSON serialization from dirty/no-op checks and per-bit string allocation from address overlap validation.
- Added reusable scan buffers and a read-only live renderer view; retained full value validation, rollback and detached history snapshots.
- Bounded retained trace payload and cached text/tag lookups; culled offscreen ladder contacts and batched design-grid drawing.
- Coalesced canvas requests, skipped unchanged or hidden-scene redraws and unchanged recovery serialization, and retained editor/palette/toolbar/tab controls.
- Refined compact command chrome, vector contact icons, ladder spacing and operand/address alignment.
- Added same-harness baseline benchmarks, allocation regressions and real Uno browser performance-mechanism checks. See [measurement scope](docs/performance.md); no overall or TIA-relative speedup is implied.

## Unreleased — program and LAD authoring

- Added a reusable program directory and transactional LAD/SCL block creation, properties, duplication, deletion and simulator scan-order commands. Copies use fresh graph identifiers and start offline.
- Added network title/comment dialogs, copy/order/delete, selection-aware contacts, full parallel branches and typed instruction/operand suggestions. Invalid or stale dialog edits are rejected without partial changes.
- Added graphical selection, keyboard authoring and task-palette dragging, view-only network collapse and a shared logical ladder layout with horizontal/vertical scrollbars.
- Loaded packaged fonts for shared Skia drawing and retained readable contact spacing for long rungs.
- Added portable authoring/geometry tests and compiled-Uno dialog, ladder and program-directory browser scenarios. Full TIA Portal model and pixel-exact parity are not claimed.

## Unreleased — editable PLC tag tables

- Replaced read-only Uno tag rows with a compact viewport-recycled grid and editable cells.
- Added atomic rectangular clipboard batches, reference-safe simultaneous renames, guarded deletion, and nonoverlapping marker allocation.
- Added sorting/filtering, column resizing/reordering/visibility, range selection, keyboard navigation and visible-row monitoring.
- Added portable table/clipboard regressions and actual Uno browser editing, clipboard and 10,000-tag file-import tests.
- No full or pixel-exact Siemens compatibility claim; see [tag-table scope](docs/tag-tables.md).

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
