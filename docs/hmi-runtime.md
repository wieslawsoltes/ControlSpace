# HMI runtime values and button actions

The shared Uno designer can configure the existing Numeric, Gauge, Tank and Button objects for a local virtual-controller preview. These are a supported subset of operator-panel workflows, not WinCC project compatibility or the full Siemens runtime event system. There is no physical controller connection.

## Configure a control

Select a numeric object or button in Design mode and open **Runtime settings…** in the editor toolbar or **Configure runtime…** in the inspector. Runtime configuration is a normal validated project edit, with undo/redo. Configuration editing is blocked during simulation RUN, just like other design changes.

The **Use configured runtime behavior** checkbox controls whether the optional configuration is stored. Uncheck it to restore legacy behavior. Restoring defaults is rejected when the old binding is incompatible with that behavior—for example, a marker-bound toggle button cannot become a legacy momentary input button without rebinding it to a BOOL input.

### Numeric displays and I/O fields

Numeric objects offer `Output`, `Input`, and `InputOutput` modes. Output remains read-only. Input hides the current value and prompts for an entry. InputOutput shows the current value and opens the same entry dialog. Gauge and Tank retain output-only behavior.

Configure minimum, maximum, 0–6 decimal places and a unit label of at most 24 printable characters. Gauge and Tank fills use the configured range rather than assuming a percentage. Configured displays have consistent formatting in the public Skia renderer and the Uno designer/runtime. A value outside its configured range receives a limit-warning color in runtime; this is a visual warning, not an alarm/acknowledgement subsystem.

In Runtime, click an input-capable numeric field, enter a number, then use **Write value** or Enter. Escape/Cancel discards the draft without stopping the running simulator. Typing or leaving the field does not commit it. Parsing uses a decimal dot, optional sign and exponent; it rejects grouping separators, unit suffixes, nonfinite numbers, excess decimal precision and out-of-range values. Integer and TIME tags additionally require an integer value. TIME values use this simulator's millisecond representation, without unit conversion.

The configured entry interval must contain at least one value representable by the tag type and configured precision. A failed entry leaves both the dialog and original controller value intact so the entry can be corrected.

### Buttons

| Action | Behavior |
| --- | --- |
| `Momentary` | Original press/release behavior for a BOOL input-image tag. Release/cancellation clears the original controller's input. |
| `SetBit` / `ResetBit` | One completed click writes 1 / 0 to a writable BOOL tag. |
| `ToggleBit` | One completed click inverts a writable BOOL tag. |
| `SetValue` | One completed click writes a validated constant to a writable tag. |
| `ActivateScreen` | Opens the configured screen by its stable ID without recompiling or discarding controller state. |
| `PreviousScreen` | Returns through the runtime session's bounded history; an empty history is a no-op. |

Only Momentary writes on press. Other actions require release over the same object that received the press. Outside release, lost capture, Escape or a changed screen/controller lifecycle does not dispatch a click action. Navigation buttons must be unbound. SetValue applies the tag's type/range checks; it does not use a numeric display's entry limits.

Runtime navigation keeps at most 32 previous screen IDs. Self-navigation does not add a history entry. Leaving runtime through ordinary workbench navigation discards that runtime history. The history is not project data and does not enter undo/redo.

## Write semantics and lifecycle

Runtime writes are distinct from editing the project or forcing a tag. Inputs update the virtual input image. Markers receive a one-shot write and can be overwritten by the next program scan. Output-image tags are read-only through these new HMI operations. Active simulated forces must be released before numeric, one-shot or momentary HMI writes; a faulted controller must be reset.

Numeric entry captures the original session generation, active screen and controller lifecycle epoch. Stop, Reset, a new Run transition, a fault, runtime navigation, deactivation, opening another entry or replacing/disposing the session invalidates the pending entry. Ordinary scans do not invalidate an entry. Returning to the same screen or restarting the same controller does not make an old entry valid again.

The host calls `SuspendHmiInput()` on deactivation, and creates/disposes the session as the project or controller changes. The helper is not a safety mechanism or a concurrent transport API. Like the existing virtual controller, it is designed for same-thread operation. All source mutations must use workspace transactions rather than direct changes to exposed collections.

## Persistent references and compatibility

Configuration is an optional immutable `HmiRuntimeOptions` value on `HmiObject.Runtime`; the existing constructor and deconstruction signature are retained. Null options are omitted from JSON so legacy objects still export without new members. The project envelope remains version 1. Projects with non-null runtime options require this implementation: older builds and the separate JavaScript prototype reject the unknown `runtime` member.

JSON import, property editing and clipboard paste validate bindings and navigation targets. Renaming a tag keeps its action/options while updating its binding. A referenced destination screen cannot be deleted until external navigation buttons are retargeted or removed. Self-links do not prevent deleting their own screen. Screen duplication changes copied self-links to the new screen ID while preserving links to other screens. Clipboard paste into another project rejects missing targets instead of matching unrelated screens by display name.

## Reusable components

`ControlSpace.Core` contains `HmiRuntimeOptions`, `HmiIoMode`, `HmiButtonAction` and the shared validation, parsing and formatting rules. `ControlSpace.Simulation` provides `VirtualPlc.WriteHmiValue`, `HmiRuntimeSession`, `HmiNumericEntry` and a captured `HmiRuntimeActivation` context. `Engineering` integrates properties, clipboard validation, undo and screen references. Shared Skia numeric drawing and Uno pointer/dialog integration compose those parts without adding new dependencies or an application-owned render loop.

A host using `HmiRuntimeSession.ActivateButton()` should display the resulting `ScreenId` after navigation. `BeginNumericInput()` returns a disposable draft; call `Commit()` only when the operator confirms. The existing `HmiMomentaryInput` still handles momentary controls rather than treating a press as a completed click.

## Sample and regression commands

Open `samples/hmi-runtime.controlspace.json` through the normal project picker. Its two operator screens demonstrate a configurable speed entry, a ranged actual-speed gauge, momentary process buttons, a preset action, bit actions and previous-screen navigation. Marker actions affect only simulator values.

```sh
dotnet run --project tests/ControlSpace.Hmi.Tests -c Release
# Once the real Uno browser build is served under its Pages base path:
python tests/browser/hmi_runtime.py http://127.0.0.1:4173/ControlSpace/
```

The HMI test driver includes source tests for import/clipboard roundtrip, limits and numeric type checks, invalid/cancelled/stale writes, Run/Stop/Reset/fault transitions, forces, navigation, reference-safe deletion/duplication, tag renaming and public/designer rendering agreement. The Pages workflow includes eight pointer/keyboard/file-picker runtime scenario groups. These commands describe the tests to run; they are not evidence that a particular source revision has passed. Use commit-specific CI results.

## Reference and remaining gaps

The workflow reference is Siemens' [I/O field modes and formatting documentation](https://docs.tia.siemens.cloud/r/en-us/v21/creating-screens-basic-panels-panels-comfort-panels-rt-advanced-rt-professional/display-and-operating-elements-basic-panels-panels-comfort-panels-rt-advanced-rt-professional/objects-basic-panels-panels-comfort-panels-rt-advanced-rt-professional/i/o-field-basic-panels-panels-comfort-panels-rt-advanced-rt-professional) and [system-function overview](https://docs.tia.siemens.cloud/r/en-us/v20/using-global-functions-basic-panels-panels-comfort-panels-rt-advanced-rt-professional/copying-between-devices-and-editors-basic-panels-panels-comfort-panels-rt-advanced-rt-professional/copying-and-pasting-between-runtime-advanced-and-panels-and-runtime-professional-basic-panels-panels-comfort-panels-rt-advanced-rt-professional/system-functions-basic-panels-panels-comfort-panels-rt-advanced-rt-professional). The implementation deliberately differs in several respects: excess decimal precision is rejected rather than truncated, input uses an explicit modal buffer, writes are limited to the simulator's I/M model, and a button has one configured action rather than a general event/function list.

Unsupported areas still include binary/hex/string/date I/O modes, symbolic lists, locale-specific keyboards, per-bit operations within integer tags, engineering-unit conversion, arbitrary event chains, scripts, alarms, recipes, archives, authentication, native WinCC data and physical PLC communication. This is not pixel-exact appearance or complete keyboard/accessibility parity with TIA Portal. Native desktop/touch interaction and physical-GPU performance require independent verification.
