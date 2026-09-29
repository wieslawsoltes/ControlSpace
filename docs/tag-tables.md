# PLC tag-table editing

The shared Uno `EngineeringTable` implements a compact, viewport-recycled grid for the current ControlSpace tag model. It replaces the former read-only list and works in both desktop and browser hosts. Bind a `Workspace` to enable editing; an unbound table remains read-only.

## Interactions

Select a cell with the pointer or arrow keys. Shift extends a rectangular selection; the numbered row selector selects its visible columns. F2 or double-click starts editing. Enter applies the value and advances one row, Tab applies and advances one column, and Escape cancels. Invalid values remain in the editor with an explanatory status message. Changes use the same project undo/redo history as the other editors.

Click a column header to sort, drag its edge to resize, or use its context menu to move the column. The Columns menu controls visibility. Filtering matches names, addresses, types and comments without reordering the project. Monitor values are optional, read-only, and come exclusively from the in-process virtual controller. They do not represent a physical PLC connection.

Copy/paste uses the native text clipboard with quoted, rectangular tab-separated data. Names are matched against the captured row identities, not the new sort positions after edits. A paste either commits every cell in one transaction or changes nothing. A direct data-type edit converts the address width at the same byte address when it is valid and nonoverlapping. Paste the type and address together as one rectangle to choose a different location. A paste never silently adds rows: use Add first. Clipboard access can be denied by the browser or operating system; failures are reported in the table status.

Add and Duplicate allocate a unique name and an unoccupied marker address, respecting the supported BOOL/INT/DINT/REAL/TIME widths. Delete rejects tags still referenced by LAD instructions, SCL source or HMI objects. Renaming rewrites complete symbols simultaneously across those sources while preserving comments and longer identifiers. No edits to declarations are allowed while the virtual controller is running.

## Reuse and limits

`TagTableEditor`, `TagCellEdit`, `TagColumn`, `SymbolText` and `TableClipboard` are UI-independent types in `ControlSpace.Engineering`. A batch checks the project identity and revision before committing through `Workspace.Edit`. The grid also passes a captured snapshot identity, preventing asynchronous paste from accepting an unrelated state after an undo/redo revision reuse. The clipboard reader is limited to 1 MB, 10,000 rows, 70,000 cells and 16,384 characters per cell. Existing model validation remains authoritative for types, names, addresses and overlaps.

The grid only creates controls for visible rows plus a small overscan; it does not create a TextBox for every model cell. Simulation updates visit realized monitor cells, not all 10,000 possible rows. These are algorithmic bounds, not physical-GPU throughput or latency guarantees. The browser test imports a real 10,000-tag project through the normal file picker and verifies first/last-row navigation with a bounded realized-control count.

## Reference and remaining compatibility

The workflow reference is Siemens [Structure of the PLC tag tables (V21)](https://docs.tia.siemens.cloud/r/en-us/v21/declaring-plc-tags/structure-of-the-plc-tag-tables), particularly Name, Data type, Address, Retain, Comment, optional monitoring and customizable columns. ControlSpace's optional Start value column belongs to its simulation model; it is not a claim that every original table has that column.

Still outside this implementation: multiple user-defined tag-table groups, user/system constants, structured/array/UDT declarations, Siemens-specific type/address families, HMI/OPC/Web API access policies, hardware-derived constants, native XML/XLSX/SDF interchange, fill handles, and drag-to-operand authoring. The Retain flag is metadata used by the virtual runtime's retain-aware reset, not certified hardware power-loss behavior. No Siemens logos, proprietary catalogs or original application assets are distributed. Pixel-exact appearance across TIA Portal versions and native desktop accessibility/interaction parity have not been qualified.

## Executable checks

```sh
dotnet run --project tests/ControlSpace.Table.Tests -c Release
# After publishing and serving the actual Uno app under the Pages subpath:
python tests/browser/tag_table.py http://127.0.0.1:4173/ControlSpace/
```

CI retains screenshots and `tag-table-logs.json` in `uno-browser-verification`. The browser probe is read-only and opt-in; tests make changes through real UI input, the clipboard and the file picker. Failed browser runs do not deploy Pages.
