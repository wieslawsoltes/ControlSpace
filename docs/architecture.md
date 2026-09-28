# Architecture and runtime semantics

## Dependency direction

```text
Core ← Languages ← Simulation
  ↑        ↑            ↑
Storage ← Engineering ──┘
  Core + Simulation ← Rendering.Skia
  Rendering.Skia + Engineering ← Controls.Uno
  Controls.Uno ← Workbench.Uno ← App
```

Core, Languages, Simulation, Storage, Engineering and Rendering.Skia target `net10.0`. Controls.Uno and Workbench.Uno target `net10.0-desktop` and `net10.0-browserwasm`. Only App owns native file pickers, recovery storage and host entry points.

`ProjectSnapshot.Clone` detaches mutable collections. Model leaves are C# records; editing replaces leaves in a draft. `Workspace.Edit` validates before committing, increments the revision, clears redo history and invalidates the compiled controller. `ProjectCompiler` detaches the validated project from subsequent editor mutations. Public collections remain mutable by design in this preview: consumers must not mutate a published compiled image or bypass the workspace. Thread-safe concurrent editing is not implemented.

The separately labelled JavaScript prototype follows the same model shape but is not a port with established differential parity. `tests/fixtures` contains a common JSON project and expected scan sequence. The JavaScript side has executed; the C# side remains an authored gate pending .NET execution.

## PLC model and language subset

Addresses use `%I`, `%Q`, and `%M`, Boolean byte.bit notation, `W` for INT and `D` for DINT/REAL/TIME. The validator rejects overlaps within an address area. It does not implement the complete Siemens memory model, optimized DB access, UDTs, aliases, DB instances, byte/string/array types or absolute S7 layouts.

A LAD network is an OR of series-contact paths feeding a single output instruction. This is deliberately smaller than a fully general graphical network. Contacts and comparisons are evaluated in stored order. All edge instructions are evaluated even when an earlier contact has already made a path false, so their previous-input state updates on every executed scan.

SCL supports tag assignments and IF/THEN/ELSE/END_IF statements. Expressions include unary `+`, `-`, `NOT`, arithmetic `+ - * / MOD`, comparisons and Boolean `AND OR XOR`. Quoted and unquoted identifiers are case-insensitive; comments use `//` or `(* ... *)`. There are no loops, calls, arrays, block interfaces, instances, strings, TIME literals or arbitrary host-language evaluation. Unsupported syntax is rejected rather than approximated.

The parser limits source size to 1,000,000 characters, token count to 100,000 and expression/statement nesting to 64. Model imports have an 8 MiB byte limit. Individual collection limits are checked before compilation. These are defensive bounds, not an independent denial-of-service or security qualification.

## Virtual scan

A successful scan performs the following work:

1. Copy the current value image and apply external input-image values and virtual forces.
2. Execute only blocks marked `Cyclic`, in stored project order. All blocks are nevertheless checked by the compiler.
3. Execute LAD networks and SCL statements in order against the working image.
4. Reapply forces, validate resulting values, commit the working image and append a trace snapshot.

An arithmetic or type/range fault prevents a partial value-image commit, clears output tags and forces, records a fault, and requires an explicit reset before running again. Timer/edge internal state is discarded on a fault. STOP clears `%Q` outputs, forces and instruction-state memory. Ordinary `%M` values remain until changed or reset. A cold reset restores initial values; a warm reset preserves tags marked retain. Input values are controlled by the simulator, never by a network interface.

Single-step can execute while the virtual controller is stopped. Its resulting output image remains visible for inspection until STOP, reset, another scan or an edit. This is a debugging convention, not a claim about real PLC STOP behavior.

Accepted scan periods are whole milliseconds in `1..1000`. The UI uses 100 ms of virtual time per scan. TON accrues the supplied period on the first energized scan. TOF accrues on the first de-energized scan. TP starts on a rising edge, ignores retriggers while active, and does not retrigger merely because its input remains high. CTU increments on a rising edge, saturates at the integer type maximum, and gives reset priority. These are documented local semantics, not PLCSIM-certified instruction timing.

Arithmetic uses binary64 values with explicit type/range validation. REAL is range-limited to single-precision bounds but is not rounded after each operation to IEEE-754 binary32. Integer division, conversion, overflow, retentivity and startup behavior therefore must not be assumed to match all S7/IEC implementations.

Trace storage keeps at most 2,048 snapshots, each containing the complete tag-value image. Retained memory grows with tag count. This initial implementation is not designed for unbounded acquisition; consumers needing high-rate or very large projects should implement channel-selective trace storage and measure allocation pressure before deployment.

## Rendering

### Uno / Skia

`EngineeringCanvas` derives from `Uno.WinUI.Graphics2DSK.SKCanvasElement`. The host supplies the canvas through `RenderOverride`; `Invalidate()` schedules work through Uno rather than a competing application render loop. `EngineeringRenderer` reuses paints/fonts, draws resolution-independent primitives, culls offscreen ladder networks and returns hit regions.

The supported host chooses the actual Skia backend. Neither direct Vulkan/Metal/Direct3D integration nor a C# WebGPU backend is implemented. Text uses the Skia font path and system/fallback fonts; typography, IME, shaping, screen-reader peers, touch and high-DPI behavior still need platform qualification. No font files are distributed.

### JavaScript prototype

`GraphicsSurface` attempts a WebGPU adapter/device and uses a retained-capacity vertex buffer, a resolution uniform, MSAA and a single triangle batch for geometric primitives. Device loss selects the Canvas 2D fallback. Text is a separate Canvas 2D layer. CPU layout and triangle generation are not compute-shader implementations.

`Scene` contains vector primitives, text labels and transformed hit regions. LAD culling skips offscreen networks. HMI dragging produces a preview position and commits one transaction on pointer release. Rendering invalidations are coalesced with requestAnimationFrame; simulation scheduling remains a separate 100 ms timer. The prototype stops simulation when the page becomes hidden to avoid presenting throttled background scheduling as real time.

The delivered browser evidence was captured using Canvas 2D fallback in Chromium. The WGSL shader, real GPU device-loss behavior and physical GPU performance remain unverified.

## Persistence and trust boundaries

ControlSpace JSON v1 is the only complete project format. Both implementations reject duplicate JSON keys and unknown model fields; C# deserialization also requires non-optional constructor fields. Optional record fields may take their documented defaults. The prototype uses stricter presence checks for all schema fields, so not every hand-authored JSON accepted by one engine is necessarily accepted by the other.

CSV contains the tag table only. It is not a project archive. Importing a CSV into the prototype replaces the tag list through a validated transaction; unresolved PLC operands are then surfaced by compilation. HMI bindings are checked structurally before the edit is accepted.

The native atomic-save helper writes a sibling temporary file and replaces the destination. The App's picker and browser recovery adapters use their platform storage APIs, whose durability differs by platform. Browser local recovery is not a backup, authentication, version control, enterprise storage or live shared filesystem. No secrets are collected or sent. No network transport or user-script evaluation is present.
