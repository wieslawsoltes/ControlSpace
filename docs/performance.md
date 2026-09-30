# Performance and rendering

This pass targets the shared C# engines and the compiled Uno workbench. It does not substitute the independent JavaScript prototype, change the simulator's 100 ms virtual period, or claim measured equivalence to Siemens TIA Portal.

## Removed work

`Workspace.IsDirty` is a constant-time cached query. Transactions compare detached project snapshots structurally instead of serializing two complete JSON documents just to detect a no-op. Mutable collection containers are still detached; immutable record leaves can be shared. Changes must go through workspace transactions, not direct mutation of the exposed project collections.

Address overlap validation uses integer intervals rather than allocating a string for every occupied bit. Randomized regression cases compare the result with a bit-occupancy oracle. Undo, redo, no-op edits, renamed symbols and late asynchronous save completion retain their validation and isolation requirements. An export marks the snapshot actually written as saved, not a newer edit made while its picker was open.

The virtual controller reuses its working value array and power-flow dictionary and uses indexed traversal without boxed enumerators. It still validates every resulting value and commits only successful scans. `ReadView` exposes a synchronous read-only view for same-thread rendering without cloning every tag and flow entry. Call `Snapshot()` when an independently retained image is required; the live view must not be retained as historical data or read concurrently with mutation.

Historical trace samples remain detached copies. The default controller trace is capped by both 2,048 samples and a conservative 32 MiB retained-payload estimate; the byte estimate is not a process-memory guarantee. `TraceBuffer.DroppedSamples`, `EstimatedRetainedBytes` and `Count` expose retention behavior. `TraceEnabled = false` disables history capture for library consumers without disabling execution or validation. Indexed trace rendering avoids allocating a new list of all samples for every paint.

## Rendering and UI retention

The renderer culls ladder networks and individual contacts in both axes. Symbols outside the viewport are not converted into hit regions or drawn. Fitted text runs and tag-to-slot lookups are cached with bounded text retention; changes to the project snapshot or loaded fonts invalidate their relevant caches. HMI and device grid points use a cached native point batch rather than thousands of individual circle calls.

`EngineeringCanvas.RequestRender` coalesces invalidations within the current UI turn. Uno still owns presentation and its cached scene; there is no application-owned frame timer or extra bitmap surface. Visible ladder/HMI scenes are invalidated when the controller's displayed image or state changes. Trace views follow the sample cycle; static device configuration does not follow PLC scans. Hidden engineering canvases are not invalidated by watch-table monitoring.

Repeated compilation does not rebuild the current editor, toolbar, instruction palette or document tabs. Tab activation updates existing controls, and overflow navigation reveals the active tab. Context-dependent libraries/device items refresh when their project changes. An unchanged SCL document retains its native text editor and caret rather than being recreated on compile/save notifications. Recovery still captures uncommitted source drafts but skips full-project cloning and serialization when neither the project nor draft changed. Runtime status text updates at most four times per second, while PLC scans and changing graphics retain their original update path.

Compact toolbar chrome uses accessible icon buttons and vector contact symbols. LAD rendering uses centered quoted operands, separate address labels and tighter network spacing. These are visual refinements, not a pixel-difference qualification against Siemens screenshots.

## Reproducible measurement

The `performance` job in `build.yml` copies the exact same `tools/ControlSpace.Benchmarks` source into a worktree of baseline `4ec2bd939d9a712ce7e87e1e248f339d256c25c7` and builds it against each revision. Both execute on the same runner/SDK with `DOTNET_TieredCompilation=0`, so asynchronous tier transitions do not affect the two short runs differently. The harness reports the median, minimum and maximum of seven warmed batches and calling-thread managed allocation bytes.

Scenarios cover dirty/no-op queries, validation and edits on 10,000 tags; scanning with trace enabled; a 64-contact by 16-branch ladder viewport; and an HMI with 500 objects and 10,000 tags. The harness uses pre-existing public APIs in both revisions. It does not switch off tracing for the candidate or use a different scene solely to obtain a favorable comparison.

The `performance-comparison` artifact contains `before.json`, `after.json` and `comparison.md`. Each report identifies its exact revision. On PR builds, the candidate identifier is GitHub's tested merge revision rather than the branch head. Timing ratios are reported evidence, not unstable wall-clock pass/fail thresholds. The early PR run used default tiered compilation and is not interchangeable with the stabilized harness configuration.

These measurements are native .NET engine and CPU-raster Skia microbenchmarks. They exclude cold startup, network transfer, browser FPS, physical GPU time, full-window composition and end-to-end interaction latency. They do not measure TIA Portal itself or establish one overall application speedup.

## Browser mechanism gates

`tests/browser/uno_performance_test.py` uses normal pointer and keyboard input in the actual compiled Uno application, alongside the existing workbench, tag-table and authoring suites. It checks idle recovery/scene caching, repeated-compile retention, unchanged and changed PLC image rendering, hidden-canvas behavior, tab/palette retention, and source-draft recovery.

The `?verify=1` opt-in probe reports read-only counters; normal visits do not enable it. `paintCount`, `renderRequests`, `renderSubmissions`, `snapshotCopies`, `recoverySerializations`, `editorBuilds`, `toolbarBuilds`, `paletteBuilds`, `tabCreations` and text-run counts establish which operations took place. `lastPaintMilliseconds` measures CPU-side renderer elapsed time, not GPU execution. The probe itself allocates telemetry and is not a suitable FPS or startup benchmark. Browser reports and real screenshots are retained in `uno-browser-verification`.

Native desktop interaction, high-DPI visual equivalence, accessibility qualification, browser startup optimization and physical-GPU profiling remain separate work. Full/pixel-exact TIA Portal compatibility is not established by these tests. All execution is simulation-only.
