# Verification scope and evidence

The repository keeps separate gates for the platform-neutral C# engines, the shared Uno UI, and the independent JavaScript prototype. A successful prototype test does not establish that the Uno application works.

## Executable gates

| Gate | Invocation / workflow |
| --- | --- |
| Native compiler, storage and virtual scans | `dotnet run --project tests/ControlSpace.Tests -c Release -- --fixtures tests/fixtures` |
| Workbench layout and editor session | `dotnet run --project tests/ControlSpace.Workbench.Tests -c Release` |
| JavaScript engine regressions | `npm test` |
| Prototype browser interactions | `python tests/browser/prototype_test.py` after bundling |
| Windows, Linux and macOS Uno compilation | `build.yml` desktop matrix |
| Compiled Uno browser workflows | `pages.yml` publishes WASM, then runs `tests/browser/uno_smoke.py` under `/ControlSpace/` |
| Deployment identity | `pages.yml` verifies that live `build-info.json` contains the deploying commit |

The C# engine suite contains 56 tests, including 25 shared conveyor scan vectors. The workbench suite adds 38 layout/session and editor line-ending assertions. The independent JavaScript suite contains 125 tests. Test counts are not a claim of complete language, runtime, UI or manufacturer compatibility.

## Reproducible evidence

The initial published Uno preview at commit `e71e46a970416bf1a378463a65842eaa3b59f911` passed [build run 36475400882](https://github.com/wieslawsoltes/ControlSpace/actions/runs/36475400882) and [Pages run 36475401056](https://github.com/wieslawsoltes/ControlSpace/actions/runs/36475401056). Those runs verify the original preview, not subsequent source changes.

The workbench UI changes are tracked in [PR #7](https://github.com/wieslawsoltes/ControlSpace/pull/7). The commit-specific Actions checks are authoritative for that revision. Browser validation is a separate mandatory check before deployment; failed intermediate revisions are not verified browser releases.

For current evidence, inspect the [repository workflows](https://github.com/wieslawsoltes/ControlSpace/actions). The `uno-browser-verification` artifact contains screenshots, browser console messages, checks, action coordinates, failure traces and the final state. Successful site builds stage genuine Uno screenshots under `docs/uno/`; `docs/images/prototype-*.png` are separately labelled JavaScript prototype previews.

## Compiled Uno interaction coverage

The browser suite uses real pointer clicks and keyboard input. A `?verify=1` switch enables read-only bounds/state telemetry using the standard .NET JavaScript host API; normal visits do not start the probe. The test harness parses the telemetry but does not invoke test-only commands to mutate the app.

Scenarios cover initial startup, project navigation, unique editor tabs, complete SCL text preservation, active and inactive tab closure, folder expansion, ancestor-preserving project search, keyboard and pointer pane resizing, maximize/restore, task cards, inspector tabs, compile/run/stop with an executed SCL value, Portal return, compact drawers, and layout persistence across reload. Source conformance checks cover CR, CRLF and LF comment termination, diagnostic lines and token-preserving renaming in both engines.

The suite must complete before the Pages artifact is uploaded for deployment. A PR never deploys Pages. Main deployments also check the public build identity after publishing. A failed browser test may retain its site artifact for diagnosis; retaining that artifact does not deploy it or mark the failed check as successful.

## Historical prototype evidence

Files in `docs/verification/` are retained evidence from the initial JavaScript prototype session. They are not fresh reports for the compiled Uno workbench. Their recorded environment, test harness and limitations apply only to that historical run. Current CI regenerates evidence as workflow artifacts rather than silently overwriting old results with unrelated claims.

## What these checks do not establish

- Pixel-exact TIA Portal appearance, the complete original command/dialog catalog, floating/split windows, or full editor semantics.
- Native Windows/macOS/Linux pointer interaction parity: the matrix compiles those hosts; the browser suite runs Chromium.
- Physical-GPU throughput, input latency, rendering quality across devices, accessibility certification, or production-scale stability.
- Full IEC/S7 behavior, native Siemens project compatibility, controller communication, WinCC compatibility, real-time execution, safety, motion, drives, or industrial qualification.

All controller operations remain simulation. Never use this application to operate or commission real machinery.
