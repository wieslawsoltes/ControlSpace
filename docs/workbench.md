# Engineering workbench UI

ControlSpace's compiled Uno application uses a TIA Portal-style project view: compact menu and toolbars, a hierarchical project tree and details view on the left, central editor, independent task cards on the right, Properties/Info/Diagnostics inspector below, and an open-editor bar above the status line. This is a functional approximation, not complete or pixel-exact TIA Portal parity.

## Navigation and window layout

Project folders expand and collapse. Search shows matching objects with their ancestors without changing the saved-in-session folder expansion state. Arrow keys and Home/End navigate the visible rows. Program blocks, tag/watch tables, device network, HMI screens, traces and cross-references open unique editor tabs. Tabs can be closed, cycled and reordered using their context menus. Closing the last document leaves an empty work area; it does not replace the project.

The project tree recycles only the rows visible in its viewport plus a small overscan, even for large outlines. Source, tag-value, network and HMI-object edits that do not change the outline retain its existing index. Search reports its result count, keeps matching ancestors, and can be cleared with Escape or the adjacent clear button. Selection descriptions show the block language/execution mode or screen dimensions and parent path in the Details pane.

Home/End and PageUp/PageDown move both selection and keyboard focus; they do not merely scroll the tree. With the tree focused, A–Z and 0–9 select the next visible caption starting with that character, wrapping and cycling on repeated initials without opening an editor or changing the search. The **Reveal active editor** button expands its ancestors and scrolls to it; this explicit action may clear search, while passive editor activation never clears the user's filter. Folder expansion and tree selection remain session-only view state and never enter project undo history.

Drag pane dividers to resize. Focus a divider and use arrow keys for 10-pixel increments, or Home to reset its size. The project pane, task cards and inspector can be collapsed. The task pane supports a pin/automatic-collapse preference. Maximize/restore preserves the normal pane dimensions. Window > Reset window layout restores defaults.

User layout is stored separately through the optional `IWorkbenchPreferences` host adapter: desktop uses `workbench-layout.json`; the browser uses the app-scoped `ControlSpace.Workbench.Layout.v1` local-settings key so a completed save survives immediate reload. Earlier browser layout files are still read for migration. Values are bounded and malformed or future-version settings fall back to defaults. Pane changes never edit project content or enter its undo history. Below 1100 pixels task cards use an overlay drawer; below 760 pixels the project tree does too.

Portal view groups project actions by Start, Devices & networks, PLC programming, Visualization and Online & diagnostics. Return to Project view to resume the active editor.

## Editors and inspector

The editor toolbar follows the current document: LAD networks, contacts and zoom; tags and CSV export; HMI design/runtime and object insertion; device configuration. The right task card can show context tools, the project library or virtual inputs.

Properties are edited in compact label/value rows in the bottom inspector. Info includes compilation diagnostics, severity filters, cross-references and virtual inputs. Clicking a supported compilation diagnostic navigates to its source block and position. Diagnostics displays the virtual controller state; all execution remains in-process simulation.

SCL drafts and caret positions are preserved when switching documents or when another project edit refreshes the workbench. Navigation commits the active draft transactionally. Recovery includes current drafts without compiling them or changing the live project revision. Closing a source tab cannot copy its text into a neighboring program block. LAD zoom/scroll are retained per open editor for the current session.

## Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| F6 / Shift+F6 | Next / previous workbench pane |
| Ctrl+F6 / Ctrl+Shift+F6 | Next / previous editor |
| Ctrl+Alt+Right / Left | Next / previous editor |
| Ctrl+W | Close active editor |
| Ctrl+1 / Ctrl+2 / Ctrl+3 | Project tree / inspector / task cards |
| Ctrl+Shift+F | Focus project search |
| Ctrl+O / Ctrl+S | Open / export project |
| Ctrl+Z / Ctrl+Y | Project undo / redo outside text fields |
| F7 / F5 / Esc | Compile / start simulation / stop simulation |

Text fields retain their native undo handling. Browsers or operating systems may reserve some shortcuts; menu and pointer equivalents remain available.

## Reuse and verification

`ProjectNavigator` provides the platform-neutral outline index, filtered projection and selection/expansion commands. `ProjectTree` supplies Uno row recycling, focus and scrolling. They require UI-thread use and snapshots treated as immutable by the host; arbitrary direct collection mutations bypass invalidation.

`WorkbenchLayout` and `EditorSession` live in the platform-neutral Engineering library. `WorkbenchPane`, `WorkbenchSplitter`, `EditorBar` and `ProjectTree` are public Controls.Uno components. The workbench composes them and owns model transactions and layout persistence; the app supplies file-system/browser adapters.

Run `dotnet run --project tests/ControlSpace.Workbench.Tests -c Release` for layout/session regressions. The Pages workflow additionally runs real pointer/keyboard workflows against the compiled Uno WASM app under `/ControlSpace/`; a `?verify=1` opt-in enables read-only control bounds and state telemetry for tests. It does not expose mutation commands and is disabled on ordinary visits. CI saves screenshots and logs in `uno-browser-verification`, and stages the screenshots at `docs/uno/` in the deployed site.

Navigation regressions run with `dotnet run --project tests/ControlSpace.Navigation.Tests -c Release` and `tests/browser/navigation_test.py`. The browser suite imports a normal project file with 1,000 blocks and 1,000 screens and exercises keyboard movement, filtering, reveal and source-edit retention through actual pointer/keyboard input. Row/control counts are evidence of bounded realization, not browser FPS or an end-to-end latency benchmark.

The initial-letter and arrow-key behavior follows the Siemens [project-tree reference](https://docs.tia.siemens.cloud/r/en-us/v20/introduction-to-the-tia-portal/user-interface-and-operation/layout-of-the-user-interface/project-tree/function-and-structure-of-the-project-tree) and [keyboard operation](https://docs.tia.siemens.cloud/r/en-us/v20/introduction-to-the-tia-portal/user-interface-and-operation/keyboard-operation-in-the-tia-portal/basic-functions-of-the-tia-portal). This is not a pixel-by-pixel comparison or the full Siemens command catalog.

## Remaining UI boundaries

Floating and split editor windows, drag-and-drop tab docking, the complete original command/dialog catalog, IDE-grade SCL completion and syntax services, full data-grid editing semantics, complete HMI tooling and device catalogs, and pixel-by-pixel comparison across TIA Portal versions remain future work. Desktop compilation is checked on Windows, Linux and macOS; browser interaction tests do not constitute native desktop interaction or physical-GPU qualification. Native Siemens formats, PLC download/online protocols and safety functionality remain unsupported.
