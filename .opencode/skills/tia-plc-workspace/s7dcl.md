# SIMATIC SD (`.s7dcl`) and compile errors

## Files

- `.s7dcl` — interface, attributes, program (SCL/LAD/FBD/DB).
- `.s7res` — multilingual comments. `.s7dcl` references IDs such as `{ S7_MLC := "MLC_3SC" }`.
- YAML in `.s7res`: `MultiLingualTexts:` then `- id: MLC_3SC` and language keys (`de-DE`, `en-US`, …).
- New comment = new unique ID in **both** files. Missing IDs → import rejected, TIA unchanged.
- XML (`SimaticML/XML`): all texts inside the XML; no `.s7res`. Do not mix XML content with format `SimaticData/SD`.

## SCL compile line mapping

`tia_plc_compile_item` / `tia_plc_compile_plc` messages:

- `Location` example: `["PLC_1", "Program blocks", "Plant", "Main (OB1)", "75"]`
- Last element for SCL = line number **in the SCL source**, not in the whole `.s7dcl`.
- In the `.s7dcl` file this is: **line of the `NETWORK` keyword + that number**.

Example: `NETWORK` on line 354 and compiler position `75` → edit line **429** of the `.s7dcl`.

If `BlockName` on the message is not the block you imported, open/export that block; the error is in a dependency (type, tag, callee).

## Creating an FC from a template

1. Copy a similar FC `.s7dcl` (and `.s7res` if present).
2. Rename the function in the declaration to the new `BlockName` (must match the file stem).
3. Keep SD headers/attributes unless you know they must change.
4. Put the files in `directory` = `PlcDir(path)` (see SKILL.md); pass the same TIA folder array as `path`.
5. Import with `tia_plc_blocks_put_file` (`directory` + `path`), then compile.

Prefer editing exported SD over writing a block from an empty buffer. TIA is strict about SD structure.

## Typical compile errors

| Message theme | Likely fix |
|---|---|
| Identifier not found | Tag table / UDT / callee missing or misspelled; import dependency first |
| Type mismatch | Interface or UDT member vs call site |
| Cannot overwrite know-how | Stop |
| Library 409 | `libraryMode` `"InTest"` |
| Inconsistent / must compile others | Expected after UDT change; compile item then `tia_plc_compile_plc` |

After a UDT change, blocks that use it become inconsistent until compiled.
