# Capability and parity matrix

This matrix describes implementation scope, not independent compatibility certification. “C# source” means authored code that has not yet been built in the initial delivery. “Prototype” means the separately labelled JavaScript implementation.

| Area | C# / Uno source | JavaScript prototype | Remaining boundary |
|---|---|---|---|
| Classic engineering shell | Menus, tree, tabs, toolbars, task/output panes | Similar compact layout; resizable panes | Not pixel-exact TIA Portal; native/prototype layout differs |
| Portal/project navigation | Implemented | Implemented | Not every TIA portal workflow |
| Window docking | Fixed three-column shell | Resizable columns and output pane | Floating windows, persisted docking layouts, multiwindow absent |
| Native custom controls | Public Uno canvas/tree/table/theme | Independent DOM controls | Native controls not browser-tested |
| Project tree | Search and navigation | Search and navigation | Full collapse/expand state, hierarchy editing and folders absent |
| LAD drawing | Shared Skia primitives and hit testing | WebGPU/Canvas primitives and hit testing | No unrestricted graphical graph topology |
| Contacts and coils | NO/NC, edges, comparisons, coil/set/reset | Same intended subset | Vendor-complete instruction semantics absent |
| Parallel paths | Model/compiler and branch editing | Add/edit paths | No nested arbitrary branch graph |
| Timers and counters | TON/TOF/TP/CTU | Same intended subset | No complete IEC/S7 instruction library or qualification |
| SCL | Bounded typed parser/interpreter | Bounded typed parser/interpreter | Not full SCL; no IntelliSense, loops, calls or block instances |
| FBD/STL/GRAPH | Not implemented | Not implemented | Separate languages/editors needed |
| Tag table | Types, addresses, properties, virtual writes | Editable metadata, rename, import/export, forces | No arrays, UDTs, optimized DBs or complete types |
| Symbol rename | Core API rewrites tokens/LAD/HMI | UI exposes atomic rename | Native shell does not yet expose a rename field |
| Cross-references | LAD reads/writes | LAD reads/writes | SCL/HMI indexed reference view absent |
| Undo/redo | Bounded snapshot transactions | Bounded snapshot transactions | Branching revision graph and collaborative history absent |
| Project files | JSON read/write; CSV API/export | JSON read/write; CSV read/write | `.ap*`, `.zap*`, Siemens Openness exchange absent |
| Local recovery | Platform local-folder adapter | localStorage with error handling | Real persistence not qualified by offline test-double checks |
| Simulator | In-process scans and fault behavior | In-process scans and fault behavior | No hardware PLC, PLCSIM or hard-real-time equivalence |
| Online communication | Not implemented | Not implemented | S7, PROFINET, OPC UA, discovery and secure gateways absent |
| Hardware topology | Generic devices/modules/links model and drawing | Device/module/link creation and editing | No manufacturer catalog, GSDML import or real bus behavior |
| Native link creation | Model/API only | Exposed in UI | Native shell workflow remains to be exposed |
| HMI editing | Basic object types, properties and drag-on-release | Types, bindings, live drag, snapping and momentary inputs | No WinCC project/runtime compatibility |
| HMI runtime | Local tag bindings | Local tag bindings | Alarm systems, recipes, scripting, permissions and production runtime absent |
| Trace | Bounded snapshots and speed plot | Bounded snapshots, speed plot, CSV export | Trigger configurations, multi-axis acquisition and long-term storage absent |
| Rendering | Uno host Skia canvas | WebGPU geometry, Canvas 2D text/fallback | No physical-GPU validation, no pure-GPU layout/typography |
| Large projects | Basic bounds and LAD culling | Basic bounds, culling and edit history budget | Full table virtualization and production-scale benchmarks pending |
| Keyboard/accessibility | Standard controls and automation names | Keyboard selection lists, shortcuts, splitter keys | Complete UIA canvas peers, IME and screen-reader qualification pending |
| Touch/mobile | Pointer event foundations | Pointer event foundations; compact desktop view tested | Not a fully qualified mobile UI |
| Safety/motion/drives | Not implemented | Not implemented | No safety certification, motion planning, drive tools or commissioning |
| Enterprise/collaboration | Not implemented | Not implemented | Authentication, roles, audit trail, multiuser, cloud sync absent |
| Build/Pages/release | Workflows authored | Tests can run in build workflow | No remote workflow run or deployment in initial delivery |

## Reference scope

The requested reference product is Siemens TIA Portal, whose engineering suite extends beyond PLC logic editing. The initial work focuses on an offline engineering shell and a small deterministic simulation model, not the complete product suite. Reference documentation reviewed for scope:

- Siemens TIA Portal: https://www.siemens.com/global/en/products/automation/industry-software/automation-software/tia-portal.html
- Siemens documentation portal: https://docs.tia.siemens.cloud/
- Uno 6.7 release: https://platform.uno/blog/uno-platform-6-7/
- Uno shared canvas integration: https://platform.uno/docs/articles/controls/SKCanvasElement.html

No screenshots or code from those product documentation pages are included as application assets.

## Next engineering gates

First compile and exercise the C# libraries and Uno hosts. Resolve API/build/runtime failures before publishing the primary browser site. Then establish differential scan fixtures, measured rendering/accessibility tests and native/prototype interaction convergence. Full language support, external protocols and native project formats should each have their own scoped compatibility specification and independently testable package rather than being advertised as complete by adding toolbar labels.
