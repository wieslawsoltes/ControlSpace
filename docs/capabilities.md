# Capability and parity matrix

This matrix describes the current implementation, not independent compatibility certification. The primary application is the shared C# / Uno workbench; the separately labelled JavaScript prototype is not a substitute for testing that application. Commit-specific [Actions results](https://github.com/wieslawsoltes/ControlSpace/actions) establish which build and interaction gates ran for a revision.

| Area | Shared C# / Uno implementation | Remaining boundary |
| --- | --- | --- |
| Engineering shell | Compact menus/toolbars, project/details panes, task cards, bottom inspector, editor bar, Portal view | Complete original commands/dialogs and pixel-exact visual qualification |
| Panes and documents | Pointer/keyboard splitters, collapse/pin, maximize/restore, independent layout persistence, close/reorder/cycle tabs | Floating/split windows, drag docking, multiwindow layouts |
| Project tree | Hierarchical navigation, collapse/expand, ancestor-preserving search, block context commands | Arbitrary hierarchy editing, user folders and full device trees |
| Program blocks | LAD/SCL create/properties/duplicate/delete, automatic numbers, offline/cyclic participation, simulator order | Siemens OB/FB/FC/DB classes, interfaces, instances, block calls, interrupt dispatch |
| LAD authoring | Selected-path insertion, typed operand dialogs, network title/comment editing, deep copies, contact/network ordering and deletion | Incomplete instruction placeholders, unrestricted graph editing and mixed textual networks |
| LAD viewport | Shared Skia geometry, clipped labels, fixed contact spacing, network folding, two-axis scrolling, keyboard selection | Version/DPI-specific visual comparisons and full accessibility peers |
| Contacts and coils | NO/NC, positive/negative edges, numeric comparisons, assignment/set/reset coils | Manufacturer-complete instruction semantics |
| Parallel paths | Full flat paths, selected-path insertion/removal, structural validation | Arbitrary nested branches, partial branch starts/joins and wire routing |
| Timers and counters | TON/TOF/TP, CTU and constant MOVE with typed operands/presets | Complete IEC/S7 instruction catalog and qualification |
| SCL | Bounded typed parser/interpreter and source editor with draft/caret retention | Full SCL, loops, calls, interfaces, IntelliSense and IDE language services |
| FBD/STL/GRAPH | Not implemented | Separate language and editor implementations |
| PLC tag table | Virtualized editable grid, sort/filter, column controls, Retain, native copy/TSV paste, undoable batches, safe rename | Multiple table groups, constants, arrays, UDTs, optimized DBs and full Siemens types |
| Symbol integrity | Transactional renames through LAD/SCL/HMI; referenced-tag deletion protection | Complete manufacturer symbol/type-resolution model |
| Cross-references | LAD read/write view | Full indexed SCL/HMI/indirect reference navigation |
| Undo/redo | Bounded project snapshot transactions, stale-dialog/paste protection | Branching history and collaborative revisions |
| Project interchange | ControlSpace JSON, CSV tag API/export and clipboard TSV | Native `.ap*`/`.zap*`, Openness XML, XLSX/SDF interchange |
| Recovery/preferences | Draft recovery adapter; independent desktop/browser layout persistence | Crash-consistent project recovery under every browser/storage failure |
| Simulator | In-process cyclic scans, input image, virtual forcing, timers/counters, rollback and trace | PLCSIM/S7 equivalence, hard-real-time behavior and physical execution |
| Online communication | No transport implementation | S7, PROFINET, OPC UA, discovery and secure gateways |
| Hardware topology | Generic device/module/link model and shared drawing, basic properties | Manufacturer catalogs, GSDML, complete link-authoring UI and real bus behavior |
| HMI editing | Basic object types, per-screen navigation, properties, drag-on-release and tag bindings | Full WinCC authoring/runtime, alarms, recipes, scripts and permissions |
| Trace | Bounded snapshots and a selected-channel plot | Trigger configuration, multiaxis acquisition and long-term storage |
| Rendering | Host-backed `SKCanvasElement`, retained Skia resources and packaged text faces | Direct C# WebGPU backend, physical-GPU performance and visual qualification |
| Large projects | Viewport-recycled tag controls; culling and cached geometry for ladder networks | Production-scale latency/memory budgets and whole-workspace virtualization |
| Keyboard/touch | Workbench and authoring shortcuts, pointer controls, compact pane drawers | Complete IME, screen-reader, native mobile and accessibility qualification |
| Safety/motion/drives | Not implemented | Certified safety, motion planning, drives and commissioning |
| Enterprise services | Not implemented | Authentication, permissions, audit, collaboration and continuous cloud sync |

## Verification evidence and limits

PR #8's merged revision `4b77f7d684de5356320a257b57be788a60436c14` passed the [main build](https://github.com/wieslawsoltes/ControlSpace/actions/runs/36534783897) and [browser/deployment workflow](https://github.com/wieslawsoltes/ControlSpace/actions/runs/36534783817). This included Windows/Linux/macOS compilation, portable tests and 29 compiled-Uno browser workflows. Its normal file-picker test imported 10,000 tags and reached the final row with 22 realized row controls at the tested viewport. This is a control-count result, not a GPU latency measurement.

Program-authoring changes are tracked in [PR #9](https://github.com/wieslawsoltes/ControlSpace/pull/9), with dedicated portable and compiled-browser tests. Use the checks for the actual revision under review; earlier passing runs do not verify later changes. Build success is not native-desktop interaction qualification. See [verification scope](verification.md), [tag tables](tag-tables.md), and [program editing](program-editing.md).

## Reference scope

The workflow reference is Siemens TIA Portal, which includes substantially more than PLC logic editing. ControlSpace remains an independent implementation focused on a reusable offline engineering shell and bounded simulation. No proprietary Siemens catalogs, firmware, logos or screenshots are distributed as application assets. Full/pixel-exact compatibility remains a goal, not a completed result.

Never use this simulator to operate or commission physical machinery.
