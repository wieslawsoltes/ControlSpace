# Program blocks and ladder authoring

ControlSpace's shared Uno workbench provides program-block management and graphical LAD editing on top of the existing typed project model. The implementation follows selected TIA Portal workflows, but does not implement the complete Siemens program model or pixel-exact interface.

## Program directory and block dialogs

Open **Program blocks > Block overview** in the project tree or **View > Program blocks**. The directory shows each block's name, number, language, simulation participation and network count. Search filters the view without changing program order. Double-click a row to open its editor; the toolbar and project-tree context menus expose properties, duplication, deletion and order changes.

**Add new block** creates a LAD or SCL block with an automatic or explicit unused number. Names are unique, case-insensitive identifiers of up to 64 characters. Numbers occupy one ControlSpace namespace from 1 through 65535; this is not the separate Siemens OB/FB/FC/DB numbering model. The language is fixed after creation to avoid silently discarding source or network data. A new LAD block has no networks, and a new SCL block starts with a source comment.

New and duplicated blocks are offline by default. Enable **Execute cyclically in the simulator** explicitly in block properties. The directory's up/down actions change the deterministic order in which enabled ControlSpace programs are scanned. This is not Siemens interrupt priority, OB dispatch, function invocation or block-instance behavior. Duplicating a block creates fresh block/network/instruction identifiers and leaves its tag references intact. Deletion asks for confirmation and is undoable.

## Networks, paths and instructions

Open a LAD block and use **+ Network** or Insert. The new network follows the selected network; with no selected network it is appended to the active block. Insertion never silently targets another block. To remain valid in the current model, a new network starts with one compatible BOOL contact and one writable BOOL coil. Create suitable PLC tags first when the project has none.

Double-click the network header to edit its title and multiline comment. Header chevrons and **Collapse / Expand** toolbar actions affect view state only: collapsed networks still compile and execute. Network context menus and the inspector support duplication, ordering and deletion. Copies have independent identifiers and can be restored through normal undo/redo.

Select a contact before inserting another instruction to insert immediately after it in its own path. Select a network or its output to append to that network's first path. **Branch** or Shift+F8 inserts a full parallel path after the selected path. Delete parallel path operates on the path containing the selected contact. Contacts can be reordered within their path or deleted. At least one path and one contact per path must remain; remove the branch or network to remove its final element. The supported model is limited to 16 full parallel paths and 64 contacts per path, not arbitrary nested branches or partial-branch joins.

Double-click an instruction, press F2/Enter, or choose its properties action. The operand field suggests matching compatible PLC tags. Contacts accept BOOL, comparisons accept numeric values, outputs require compatible writable tags, timer elapsed operands require writable TIME, and counter reset operands require BOOL. Invalid values leave the dialog open with an explanation. Timer presets are integer milliseconds; MOVE accepts a representable constant. All supported output kinds are also available in the task palette. Click an instruction to insert at the selection, or drag it onto a network/contact.

## Viewport and rendering

The LAD viewport uses fixed logical contact spacing rather than compressing large rungs. Horizontal and vertical scrollbars navigate the same geometry used for vector drawing and hit testing. Shift+wheel scrolls horizontally; Page Up/Down moves vertically. Selection navigation reveals the selected instruction. Zoom and both offsets are retained per open LAD editor in the current session.

The renderer loads the host's packaged Open Sans faces, avoiding missing system-font fallback in the browser. Headers and operand labels are clipped to their allocated regions, and chevrons are vector paths. Power-flow coloring follows each contact's accumulated path result. These changes improve readability but are not a version/DPI-specific pixel comparison against Siemens.

## Keyboard commands while the ladder canvas has focus

| Key | Action |
| --- | --- |
| Insert | Insert a network |
| F9 / F10 | Insert normally open / normally closed contact |
| Shift+F8 | Add parallel path |
| F2 / Enter | Edit selected network or instruction |
| Delete | Delete selected contact; selected network asks for confirmation |
| Ctrl+D | Duplicate selected network |
| Arrow keys, Home, End | Navigate networks and visible instructions |
| Page Up / Page Down | Scroll vertically |

Workbench compile/run/stop and undo/redo commands remain available. Browser or operating-system shortcuts may take precedence; toolbar and menu equivalents remain provided.

## Transactions and reuse

`ProgramEditor` and `LadderInstructions` in `ControlSpace.Engineering` own authoring rules and transactions. `ProgramBlockBrowser` and `EngineeringViewport` in `ControlSpace.Controls.Uno` are reusable host controls. `LadderLayout` in `ControlSpace.Rendering.Skia` supplies logical layout, visible-network enumeration and contact hit regions.

Each mutation either commits as one undoable project operation or changes nothing. Dialogs capture the exact project snapshot and reject stale updates, including undo/redo that reuses the same numeric revision. Structural edits are rejected while the virtual CPU runs. New authoring validation is scoped to these APIs; importing an existing ControlSpace file still uses the project validator and does not imply complete Siemens validation.

## Verification and boundaries

The build and Pages workflows run `tests/ControlSpace.Program.Tests` for authoring, structural limits, rollback, snapshot protection, copy identity, operand typing and viewport geometry. `tests/browser/program_editor.py` exercises the actual compiled Uno app through pointer, keyboard and dialog interactions; it does not mutate the app through test-only APIs. Commit-specific Actions results and saved browser reports are authoritative.

FB/DB interfaces, function calls and instances, optimized data blocks, structured data, arbitrary nested ladder topology, empty/incomplete instruction placeholders, mixed textual networks, full SCL, FBD/STL/GRAPH, native Siemens formats and real-controller communication remain unsupported. All execution is in-process simulation, not industrial commissioning or safety software.

Workflow references: Siemens TIA V21 [creating and managing blocks](https://docs.tia.siemens.cloud/r/en-us/v21/creating-and-managing-blocks), [LAD network insertion](https://docs.tia.siemens.cloud/r/en-us/v21/creating-lad-programs/working-with-networks/inserting-networks), [network expansion/collapse](https://docs.tia.siemens.cloud/r/en-us/v21/creating-lad-programs/working-with-networks/expanding-and-collapsing-networks), and [program-editor keyboard commands](https://docs.tia.siemens.cloud/r/en-us/v21/program-editor/using-the-keyboard-in-the-program-editor).
