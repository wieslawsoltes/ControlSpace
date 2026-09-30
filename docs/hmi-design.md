# HMI screen design

The shared Uno workbench has an offline HMI designer for nine ControlSpace objects: text label, momentary button, Boolean lamp, numeric display, percentage tank, percentage gauge, rectangle, ellipse and line. It is an original engineering preview, not a complete WinCC or Siemens TIA Portal implementation. No controller transport or safety function is present.

## Screen workflow

Open an existing screen from the project tree. Use **Screens** in its toolbar, or **View > HMI screen overview**, for the screen directory. Create, rename, resize, duplicate and delete screens. The directory supports search and keyboard opening. Screen names are unique case-insensitively; dimensions are 100–8192 pixels. A resize which would place existing objects outside the screen is rejected rather than silently cropping or moving them. Duplicating a screen creates fresh screen/object identifiers. Deletion is confirmed and undoable; deleting the last screen is allowed.

The **Toolbox** inserts labels, buttons, lamps, tanks, numeric displays, gauges and the three basic shapes. The **Objects** card lists them front-to-back and supports filtering and multiple selection. The list uses Uno's virtualizing ListView rather than instantiating one control for every object. It does not create persistent object groups or layers: a multiple selection is temporary view state.

## Basic graphics

Rectangle and ellipse objects use a solid fill. A circle is an ellipse with equal width and height. Lines have a two-screen-pixel stroke (reduced for smaller legacy bounds), round ends, and run from the top-left to the bottom-right of their positive-size bounding box; height or width of one pixel gives a horizontal or vertical segment. Independent endpoint editing, negative-slope lines, rotations, borders, dash styles, fill patterns and gradients are not implemented.

These shapes share geometry, color, clipboard, duplication, z-order, group transforms and undo with the other objects. Their Text property is a description in the object list, not text painted onto the shape. Shapes have no PLC tag binding; property edits, imports and pastes reject a nonempty binding. Ellipse selection tests the filled oval, allowing clicks through transparent corners to objects below it. Line selection uses its stroke with a four-view-pixel pointer tolerance rather than the entire bounding rectangle. Existing six control types retain their rectangular hit behavior. Both the public Skia Hmi/HmiView API and the Uno HmiDesigner renderer draw the new primitives.

Projects with these kinds retain the ControlSpace version-1 envelope, but older builds reject the unfamiliar enum values. The separately labelled JavaScript interaction prototype still supports its original six HMI kinds and rejects shape-bearing projects; it is not the compiled Uno runtime.

## Selection, geometry and arrangement

Click an object to select it. Ctrl or Shift adds or removes an object from the selection. Drag a rectangle on the screen to select fully enclosed objects. Eight fixed-size handles resize a selection; dragging inside a selection moves it as a unit. Previews do not change the document or undo history. Releasing the pointer commits one complete, validated transaction. Escape, lost pointer capture, screen navigation and controller changes discard an uncommitted gesture.

The first selected object is the reference for edge/center alignment and width/height/size matching. Distribution requires three objects and preserves the outermost objects while equalizing gaps; overlapping selections can have negative gaps. Screen-center commands move the entire selection without altering relative positions. Bring to front/back and move forward/backward preserve selected objects' relative paint order. Invalid arrangements, including objects crossing the screen boundary, are rejected atomically.

Movement snaps to a ten-pixel grid and nearby edges/centers. Toggle **Snap** independently of **Grid**, or hold Alt during a gesture to bypass snapping. Arrow keys nudge one pixel; Shift+arrow nudges ten. New/resized objects have a one-pixel minimum; previously valid subpixel objects retain their original smaller minimum. The screen-edge constraint is shared between gesture previews and committed edits.

**Fit** centers the screen in the viewport. **100%**, zoom buttons and Ctrl+wheel select manual zoom. Manual zoom supports horizontal/vertical scrollbars; Shift+wheel pans horizontally. Rulers, object hit testing, selection overlays and rendering use the same transform. The renderer clips content to both the viewport and screen, skips drawing offscreen objects and keeps text caches bounded. This is not a hardware-GPU or frame-latency measurement.

## Properties and clipboard

F2, Enter, double-click or **Properties** opens the first selected object's properties. Edit its text, X/Y/width/height, hexadecimal color and tag binding. Text labels support multiline text. The stored text is preserved; rendering limits visible text length/line count. The dialog remains open on validation failure. Multiple-object alignment is separate from the single-object property dialog.

Default bindings are checked by kind: momentary buttons use BOOL input-image tags, lamps use BOOL tags, and numeric displays/tanks/gauges use numeric tags. Configured one-shot buttons and numeric input use the additional runtime rules. Objects can be unbound. Buttons do not implicitly force output or marker tags. The default remains output-only with 0–100 tank/gauge ranges. **Runtime settings…** adds configured numeric ranges, decimal places and units, confirmed numeric input, one-shot button actions and screen navigation; see [runtime guide](hmi-runtime.md). Arbitrary formatting expressions and the full WinCC event system remain unsupported.

Ctrl+C copies a selection as bounded ControlSpace JSON on the native text clipboard. Ctrl+V pastes with new identifiers and preserved relative positions, bindings and paint order. Ctrl+X cuts; Ctrl+D duplicates; Delete removes selected objects. Paste is one undoable transaction, including across screens. Missing/incompatible bindings, invalid data, unknown/duplicate JSON properties and selections too large for the destination are rejected without partial edits. Clipboard permission and availability are controlled by the browser or OS. This format is not Siemens interchange or arbitrary image/SVG clipboard import.

Edits use captured project identity, not only revision numbers. A stale dialog, asynchronous clipboard result or gesture is rejected after intervening edits, including undo/redo revision reuse. All mutations go through the existing workspace transaction and validation system.

## Simulation preview and input release

**Runtime** compiles when needed and displays the virtual controller image; **Start simulation** advances scans. **Design** returns to authoring. HMI editing is blocked while the virtual controller runs and while Runtime mode is selected. Momentary buttons hold a BOOL input only while pressed. Pointer release/cancellation, leaving the editor, switching screens or controllers, stopping/resetting and disposal release the original controller's input. The app host also requests release on window deactivation; backend lifecycle behavior must be qualified on each target. Hosts embedding the workbench should invoke `SuspendHmiInput()` on deactivation too.

The release controller is the instance that received the press, not whichever instance happens to be active later. This prevents a release from reaching a newly compiled controller while leaving the old one pressed. It is a simulator correctness mechanism, not a certified safety guarantee. The sample Stop button is a process input, not an emergency-stop implementation.

## Reuse and tests

`HmiShapeGeometry` is in Core and provides common line endpoints and shape-aware hit tests. `HmiEditor` and `HmiTransformSession` are in Engineering; `HmiViewportTransform` is in Core; `HmiMomentaryInput` is in Simulation. `EngineeringRenderer.HmiDesigner` is the shared Skia renderer. The canvas, viewport and `HmiExplorer` are public Uno controls. The workbench supplies commands and dialogs; the app supplies file/clipboard/lifecycle integration.

Run `dotnet run --project tests/ControlSpace.Hmi.Tests -c Release` for engine/geometry/clipboard/runtime/rendering regression groups. The Pages workflow additionally runs `tests/browser/hmi_editor.py` and `tests/browser/hmi_shapes.py` against the actual compiled Uno application. It uses normal pointer/keyboard/clipboard/file-picker operations; the optional verification probe only exposes read-only telemetry. CI artifacts are the source of truth for each revision's pass/fail status. Tests include a 10,000-object screen, but counts of drawn objects and realized rows are not end-to-end speedup measurements.

## Remaining HMI/TIA boundaries

Unsupported areas include alarms and acknowledgement, recipes, historical process archives, faceplates/templates, persistent groups/layers, vector/image import, rotations, gradients, full typography/localization, general animation/event bindings, arbitrary scripting, symbolic/non-numeric input, user/role authentication, remote clients, PLC communications, manufacturer project formats and complete WinCC runtime behavior. The current nine objects and editing workflow are not full or pixel-exact parity. Native desktop interaction, touch-device usability, high-DPI equivalence and physical-GPU throughput require separate qualification.
