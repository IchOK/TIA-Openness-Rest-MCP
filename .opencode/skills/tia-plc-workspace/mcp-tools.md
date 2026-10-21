# TiaOpenness MCP tools (PLC workspace)

All successful payloads are `McpApiResponse`: `StatusCode`, `IsSuccess`, optional `Error`, `Data`.

## Project

| Tool | Role |
|---|---|
| `tia_projects_list` | Open projects: `ProcessId`, `Name`, `Path`, `Kind` (`Project` / `LocalSession`) |
| `tia_projects_open` | Open `.ap*` or retrieve `.zap*` (`upgrade`, `targetDirectory`, optional `processId`) |
| `tia_projects_save` | Save; `WasModified` |
| `tia_projects_close` | Close; requires `save` or `discardChanges` if dirty (else 409) |

## PLC identity

| Tool | Role |
|---|---|
| `tia_plc_devices_list` | `DeviceName`, `DeviceItemName`, `PlcName`, `TypeIdentifier`, counts |

Every other `tia_plc_*` call needs `processId`, `projectName`, `deviceName`, `deviceItemName`.

## Inventory

| Tool | Filters |
|---|---|
| `tia_plc_blocks_list` | `filterTypes` e.g. `FC,FB,OB,GlobalDB`; `filterLanguages` e.g. `SCL,LAD`; `path` folder array |
| `tia_plc_udts_list` | `path` |
| `tia_plc_tagtables_list` | `path` |
| `tia_plc_blocks_list_export_capabilities` | Probe SD vs XML per language |

Block list fields: `BlockName`, `BlockNumber`, `BlockType`, `ProgrammingLanguage`, `Path`, `ModifiedDate` (UTC ISO-8601), optional `Library` (`TypeName`, `Version`, `State` `Committed` / `InWork`).

UDT list fields include `UdtName`, `Path`, `ModifiedDate`. Tag table list fields include `TagTableName`, `IsDefault`, `Path`, `ModifiedDate` (from Openness `ModifiedTimeStamp`).

## Files — `directory` + `path` (no composition)

PLC file tools write/read **directly in `directory`**. They never append `<projectName>\<PlcName>`. The skill builds that layout:

```
PlcRoot   = <workspace>\<projectName>\<PlcName>
PlcDir(p) = PlcRoot\<TIA folder…>     // p empty → PlcRoot
```

**Export** (`*_get_file`, `*_get_dir`, `*_get_file_list`): REST **POST**, all fields in the JSON **body** (incl. `processId`, `projectName`, `deviceName`, `deviceItemName`, `directory`, …). URL is only the route path.

**Import** (`*_put_file`, `*_put_dir`, `*_put_file_list`): REST **PUT**; session/PLC identity and element name stay in the query; `directory` / `path` / options in the body (unchanged).

| Kind | Parameters |
|---|---|
| One object (`*_get_file` / `*_put_file`) | `directory` (absolute FS folder of the files), element name, optional `path` (TIA folder array) |
| Many named (`*_get_file_list` / `*_put_file_list`) | `items[]` each with `directory`, name, optional `path` |
| Folder (`*_get_dir` / `*_put_dir`) | `directory` (FS folder ↔ TIA `path`), optional `path` (omit = PLC root), `recursive` default true |

Replace `*` with `blocks`, `udts`, or `tagtables`.

Element name fields:

| Kind | Single | List item |
|---|---|---|
| Blocks | `blockName` | `blockName` / `BlockName` |
| UDTs | `udtName` | `udtName` / `UdtName` |
| Tag tables | `tagTableName` | `tagTableName` / `TagTableName` |

List item fields (camelCase JSON is fine): `directory`, name, optional `path`, optional `format`, optional `blockNumber` (blocks only).

Missing `directory` → HTTP 400. Do **not** use `rootDirectory` on PLC tools (HMI/TopCtrl only).

### Mapping rules

- Single export/import: files named `<Name>.s7dcl` / `.s7res` / `.xml` sit **in** `directory`.
- Dir export with `recursive` true: TIA child groups become subfolders of `directory` (SkipPrefix relative to `path`).
- Dir import with `recursive` true: FS subfolders are appended to TIA `path`.
- Optional `path` on single get: given → resolve that group; omitted → search whole PLC.
- Optional `path` on put: given → create/move into that folder; omitted → keep existing folder / PLC root for new.

### Formats

- Blocks SCL/LAD/FBD/DB (incl. F-variants): `SimaticData/SD` → `.s7dcl` + optional `.s7res`
- Blocks STL/GRAPH or SD fallback: `SimaticML/XML` → `.xml`
- UDTs: normally `.s7dcl` + optional `.s7res`
- Tag tables: SimaticML XML only → `<TagTableName>.xml`

`put_dir` retries failed imports so dependency order does not matter. `Skipped` holds the other kinds and unrecognized files.

Library instances:

- `put_dir`: always in-test, never release
- `put_file` / `put_file_list`: require `libraryMode` `"InTest"` or `"Release"` (409 otherwise)
- Discard in-test: `tia_plc_blocks_discard_type_version` / `tia_plc_udts_discard_type_version`

### Example

Block `tbDTLtoString` at TIA path `["TopCtrl_Func","Basis"]`, workspace `C:\TiaWorkspace`, project `TopCtrl_V3_Eng`, PLC `PLC_LIB`:

```
directory = C:\TiaWorkspace\TopCtrl_V3_Eng\PLC_LIB\TopCtrl_Func\Basis
path      = ["TopCtrl_Func", "Basis"]
```

`tia_plc_blocks_get_file` → `…\Basis\tbDTLtoString.s7dcl`  
`tia_plc_blocks_get_dir` on `…\PLC_LIB\TopCtrl_Func\Visu` with `path=["TopCtrl_Func","Visu"]` → root files plus `Panel\`, `Panel_Comfort\`, `Panel_Unified\`.

## Compile

| Tool | What |
|---|---|
| `tia_plc_compile_item` | One block or UDT; dependencies compiled if needed; `itemType` `Block` or `UDT` |
| `tia_plc_compile_plc` | Software (only changes). No hardware. No rebuild-all |

`IsSuccess` = compile ran. Errors live in `Data`:

- `State`: `Success` / `Information` / `Warning` / `Error`
- `ErrorCount`, `WarningCount`
- `Messages[]`: `Location` (string array), `BlockName`, `State`, `Description`

Already-consistent blocks are not compiled again; their old warnings are omitted.

## Batch `Data` (Dir / FileList)

`DeviceName`, `DeviceItemName`, `PlcName`, `Directory` (Dir tools), `Succeeded`, `Failed`, `Items[]`, `Errors[]` (`Name`, `Path`, `Files`, `StatusCode`, `Error`), `Skipped[]` (`Name`, `File`, `Reason`).

Any element failure → `IsSuccess` false, `StatusCode` 400, `Data` still complete (`Succeeded` may be > 0). No rollback. Keep successful files; report `Errors` (e.g. SimaticSdEnabler required for some pre-V20.4 SD exports).

## In-memory get/put (avoid for large blocks)

`tia_plc_blocks_get` / `put` and UDT equivalents return/accept `Content`, `Format`, `MultiLingualText`. Use files when a workspace exists.

`tia_plc_blocks_patch` only changes DB start values (`Member` + `Value`).
