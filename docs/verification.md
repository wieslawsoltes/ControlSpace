# Verification and delivery status

**Captured on 2026-09-28. This is a source preview, not a qualified TIA Portal replacement.**

## Results actually obtained

| Check | Result | Evidence |
|---|---|---|
| JavaScript model, compiler, runtime, history, import, and scene regressions | **122 passed; 0 failed** | [Node test transcript](verification/engine-tests.tap) |
| Browser interaction workflows | **32 passed; 0 failed** | [Structured report](verification/browser-tests.json), [transcript](verification/browser-tests.txt) |
| Uncaught browser exceptions in those workflows | **0** | Structured browser report |
| Original prototype rendering | LAD, HMI and device screenshots captured; LAD and HMI visually inspected | [LAD](images/prototype-ladder.png), [HMI](images/prototype-hmi.png), [devices](images/prototype-devices.png) |
| JavaScript syntax, project/reference structure, YAML, and Python scripts | Checked by the included static validation tool | [Static check report](verification/static-checks.json) |
| Pages staging guard | Rejects the JavaScript prototype as the root Uno application | Static check report |

### Reproduce the executed checks

```sh
npm test
python3 tools/bundle-prototype.py
python3 tests/browser/prototype_test.py --chromium /usr/bin/chromium
python3 tools/validate-source.py
```

For machines with Playwright's own Chromium installation, omit `--chromium`. The browser tests accept `--url http://localhost:4173/prototype/` to test an HTTP-served application; the delivery run did **not** use that mode.

### What the browser results mean

The test environment did not allow navigation to HTTP, localhost or file URLs. Its policy was not changed. The suite therefore loaded the self-contained prototype through Playwright's `set_content` API. Most tests supplied an explicit in-memory `Storage` double; a separate check exercised denied storage access. These results do **not** qualify real browser-origin persistence, file-URL storage, the deployed site, native file pickers, or cross-browser compatibility.

The renderer reported **Canvas 2D fallback**. Geometry batching and WGSL WebGPU source are included, but WebGPU shader execution, hardware GPU quality, device-loss recovery on actual hardware and GPU performance were **not tested**. Screenshots show the separately labelled JavaScript prototype, **not compiled Uno UI**.

The 32 workflows cover real UI clicks, keyboard commands, and pointer events: compilation, virtual input writes, seal-in logic, timer completion, counter edges, SCL edits and diagnostics, undo/redo, tag edits and rename propagation, rejected overlapping addresses, LAD network insertion, device/module/link editing, invalid IP rejection, HMI dragging and insertion, momentary HMI buttons, trace acquisition, STOP, panel resizing, JSON download, rejected malformed imports, recovery through the Storage double, denied Storage, and 1024 × 768 layout.

The Node suite additionally exercises parser/resource limits, strict JSON, duplicate properties, CSV quoting, typed arithmetic, instruction semantics, forcing and fault rollback, reset/retention, bounded traces and history, atomic edits, isolated compiler snapshots, keyword-safe symbol rename, empty HMI scenes, non-demo trace tags, and unenergized stopped ladder rendering.

## Authored but not executed

| Area | Status and required gate |
|---|---|
| Portable C# engines | Regression runner and shared conveyor fixture authored. Requires `dotnet run --project tests/ControlSpace.Tests -c Release -- --fixtures tests/fixtures`. |
| Uno desktop application | Source authored; compilation and interactive qualification not performed. Build matrix targets Linux, Windows and macOS. |
| Uno WebAssembly application | Source and publish/startup-smoke workflow authored; not built, loaded or deployed here. |
| C#/JavaScript equivalence | A 25-scan fixture and expected states are shared by both test runners. The JavaScript runner passed; C# execution and differential qualification remain pending. |
| NuGet packages | Eight independently structured library projects, pack metadata and pack jobs provided. No `.nupkg` files were built or published in this delivery. |
| Release automation | Version-tag desktop/WebAssembly/source/package release workflow authored; not run. |
| GitHub Pages | Workflow stages the genuine Uno output at the root and the labelled prototype under `/prototype/`. No site was deployed from this session. |
| Remote repository changes | The available connector exposes reads, not writes; local Git cannot resolve `github.com`. Remote publication is blocked. A local commit and Git bundle are delivered instead. |

The local environment had Node.js 22.16.0, Python 3.13.5 and Chromium 144.0.7559.96, but no `dotnet` command. Network access prevented SDK/package downloads and Git publication. See [environment record](verification/environment.txt).

## Compatibility and safety boundaries

The source implements a deliberately bounded engineering and simulation subset. Native Siemens project/archive formats, Siemens binary code generation, real PLC download/upload, industrial communication drivers, S7/PLCSIM qualification, complete IEC/SCL semantics, FBD/STL/GRAPH, safety and motion engineering, full WinCC functionality, hardware catalogs, complete docking/accessibility parity and pixel-exact TIA Portal parity are **not implemented or established**.

The virtual controller is a fixed-step, in-process simulator, not a real-time control runtime. Numerical and timer semantics are explicitly documented in [architecture](architecture.md). No physical-controller communication or hardware-writing path is implemented. Do not use this preview to operate machinery or safety functions.

See the detailed [capability matrix](capabilities.md) for the difference between source availability, prototype functionality and unimplemented areas. Passing prototype tests must never be represented as Uno compilation, industrial certification, exact UI parity or successful deployment.
