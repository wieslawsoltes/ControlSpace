# HMI runtime libraries

## Delivery scope

This increment adds reusable runtime models, validation, navigation history and numeric operator-input APIs. **The current Uno application does not yet expose the new runtime-settings dialogs, screen-action dispatch or numeric-entry interface.** Existing HMI Design/Runtime controls still behave as documented in [HMI design](hmi-design.md). The public rendering APIs do not yet consume the new formatting/range options; hosts can call the formatting helpers directly. This is library functionality, not a completed WinCC runtime or a new interactive Pages demonstration.

## Project metadata and references

`HmiObject.Button` optionally specifies `MomentaryInput`, `ActivateScreen`, `PreviousScreen` or `None`, with a stable target screen ID for `ActivateScreen`. A missing value preserves the existing momentary-button default. Navigation/disabled buttons cannot also have a PLC tag binding. Only a button can carry button behavior.

`HmiObject.Numeric` optionally specifies editable mode, finite increasing minimum/maximum limits, 0–9 decimal places and up to 24 printable unit characters. Numeric displays, gauges and tanks can carry display options; only a numeric display can request operator entry. An unbound configured object is valid for authoring, but cannot create an input session until bound.

Validation rejects missing screen targets, unsupported option combinations and incompatible input bindings. It constructs reference indexes only for projects which contain configured objects. Screen renaming preserves target IDs; deleting a screen referenced by another screen is rejected. Deep screen duplication retargets self-links to the duplicated screen while preserving external links. Object clipboard data preserves settings and rejects targets absent from the destination project. These operations remain atomic and undoable.

The immutable option records participate in structural project equality, cloning, tag renaming and clipboard persistence. Null properties are omitted, preserving the ordinary version-1 JSON representation of legacy projects. **Projects containing these new optional properties are not compatible with older builds or the separate JavaScript prototype**, whose strict importers reject unfamiliar fields. The version-1 envelope itself is unchanged.

## Navigation session

`HmiNavigationSession.Activate(project, currentId, targetId)` returns the validated target and records the previous screen. `Back(project, currentId)` skips deleted entries and returns the current screen when no valid previous entry exists. History is limited to 64 entries. Self-navigation adds no entry; invalid navigation does not change history. A change in project ID resets the session. Hosts should also call `Clear()` on project replacement, runtime start/end or another application-specific navigation reset, including reloads reusing the same project ID.

The session does not open views, stop/start controllers or edit/persist project content. A host dispatches a completed click, cancels held momentary inputs before switching views, and keeps the intended controller instance. Those UI integrations are separate from these APIs.

## Numeric operator entry

`HmiNumericInputSession` binds one entry operation to the current project, compiled controller, HMI object and controller lifecycle epoch. Construction checks actual project contents against the compiler snapshot, not only its ID and revision. The same project reference and controller instance must still be current at commit. Ordinary scans do not invalidate a pending entry; stop, reset, restart, fault, controller replacement or a changed project does. A successful session cannot be committed twice. Invalid text can be corrected and retried without partial changes.

The parser accepts invariant decimal and exponent notation using a decimal point, not locale grouping separators. It rejects empty/nonfinite/oversized input, values outside the configured limits, invalid PLC data types and excess fractional precision. It never silently clamps or rounds an entered value. Display rounding is separate: `HmiRuntimeOptions.Format` returns fixed precision and units for configured objects; `Fraction` returns a clamped display fraction; `IsOutOfRange` reports a display-limit violation. These helpers do not write the controller.

`VirtualPlc.SetOperatorValue` writes numeric **input-image or marker** tags only. It rejects BOOL and output-image tags, invalid values, faulted controllers and currently forced tags. It never creates, changes or releases a force. Input-image values persist into later scans; marker values are one-shot writes and may be overwritten by program logic on the next scan. A changed value advances `VisualVersion`; unchanged values do not. Trace samples and explicit snapshots remain independent historical copies.

Operator writes affect the simulator image, not tag initial values, project revision, dirty state or project undo history. These are in-process simulation APIs and cannot communicate with physical machinery. They do not implement authentication, permissions, alarm acknowledgement, Siemens protocols or safety behavior. The live controller APIs are single-threaded; hosts must serialize controller access and close/cancel entry dialogs when their screen or lifecycle changes.

## Host example

```csharp
using ControlSpace.Core;
using ControlSpace.Engineering;
using ControlSpace.Languages;
using ControlSpace.Simulation;

var project = DemoProject.Create();
project.Screens.Clear();
project.Screens.Add(new HmiScreen("panel", "Operator panel", 960, 540,
[
    new HmiObject("setpoint", HmiKind.Numeric, "Speed setpoint",
        "Speed_Setpoint", 40, 40, 240, 100)
    { Numeric = new HmiNumericOptions(true, 0, 100, 2, "rpm") }
]));

var compilation = ProjectCompiler.Compile(project);
if (!compilation.Success)
    throw new InvalidOperationException(string.Join("\n", compilation.Diagnostics));

var controller = new VirtualPlc(compilation.Program!);
controller.Run();
var entry = new HmiNumericInputSession(project, controller, "panel", "setpoint");
entry.Commit(project, controller, "42.25");
Console.WriteLine(HmiRuntimeOptions.Format(entry.Object, controller.Read(entry.Tag.Name)));
// 42.25 rpm; project initial values and the force table are unchanged.
controller.Stop();
```

A host must distinguish errors from successful commits and must not automatically accept a pending value after a controller/project transition. The example intentionally contains no UI or physical-device transport.

## Verification

Run `dotnet run --project tests/ControlSpace.Hmi.Tests -c Release`. New native groups cover persistence, reference guards, clone/clipboard/undo, input validation, display helpers, session isolation, fault/restart handling, program overwrites and bounded history. The existing Actions HMI gate executes them without changing workflow scope. The existing compiled-Uno browser suites remain separate regression gates; passing them does not establish that the unconnected runtime UI exists.

No TIA-relative performance, full-feature compatibility, physical-GPU performance or pixel-exact visual equivalence is claimed by this library increment.
