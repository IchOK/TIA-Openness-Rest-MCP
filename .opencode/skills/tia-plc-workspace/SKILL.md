---
name: tia-plc-workspace
description: Maps, syncs, and compiles Siemens TIA Portal PLC software via the TiaOpenness MCP. Use for Projekt Abbilden (full export + git repo), Projekt Sync (TIA newer vs local changes), creating or editing SCL/LAD functions until they compile, TIA export/import, übersetzen, or compile loops.
license: MIT
compatibility: opencode
metadata:
  audience: plc-engineers
  workflow: tia-openness
  opencode/slash: "true"
---

# TIA PLC Workspace

Work on TIA Portal PLC software through the **TiaOpenness MCP** and a **local file + git workspace**. TIA remains the compiler and source of truth for semantics; git detects changes.

Read [mcp-tools.md](mcp-tools.md) only when a tool's parameters or result fields are unclear. Read [s7dcl.md](s7dcl.md) when creating or fixing SCL.

## Preconditions

1. TIA Portal is running with the target project open (or open it with `tia_projects_open`).
2. TiaOpenness REST + MCP are reachable (default MCP `http://localhost:6281/mcp`).
3. Every file tool needs an **absolute** `directory` on the machine that runs TIA Portal and the REST API. The API writes/reads files **directly in that folder**. It never appends project, PLC, or kind (`Blocks` / `UDTs` / `TagTables`) names — the skill builds those.

If any MCP call fails with connection errors, stop and tell the user to start TiaREST/TiaMCP. Do not invent TIA content.

## Session bootstrap (every workflow)

Call once, then reuse the values:

1. `tia_projects_list` → `processId`, `projectName` (and `Kind` if both Project and LocalSession exist).
2. `tia_plc_devices_list` → `deviceName`, `deviceItemName`, `plcName` per PLC.
3. If the workspace already has `.tia-workspace.json`, read it and confirm it still matches the open project.

Ask the user only when several projects or several PLCs are open and the target is ambiguous.

## Directory contract (skill-owned)

REST/MCP PLC file tools take:

| Parameter | Meaning |
|---|---|
| `directory` | Absolute filesystem folder. Files go **here** (or in its subfolders for recursive Dir). |
| `path` | TIA folder as string array of user groups from the PLC root. **Not** a filesystem path. Omit = PLC root / whole-PLC search. |

**Do not** pass `rootDirectory`. That parameter belongs to HMI/TopCtrl tools only; PLC tools reject a missing `directory` with HTTP 400.

### Layout this skill uses

Kinds live in **separate** trees under the PLC. TIA folders stay under the kind folder:

```
<workspace>\<projectName>\<PlcName>\Blocks\<TIA folder…>\<Name>.s7dcl
<workspace>\<projectName>\<PlcName>\Blocks\<TIA folder…>\<Name>.s7res   (optional)
<workspace>\<projectName>\<PlcName>\Blocks\<TIA folder…>\<Name>.xml     (STL/GRAPH)
<workspace>\<projectName>\<PlcName>\UDTs\<TIA folder…>\<Name>.s7dcl
<workspace>\<projectName>\<PlcName>\UDTs\<TIA folder…>\<Name>.s7res     (optional)
<workspace>\<projectName>\<PlcName>\TagTables\<TIA folder…>\<Name>.xml
```

Objects at the PLC root (empty TIA `path`) sit directly in the kind folder, e.g. `…\Blocks\<Name>.s7dcl`.

Build paths with `Safe(name)` = replace invalid filename characters with `_`.

```
PlcRoot        = Join(workspace, Safe(projectName), Safe(plcName))
BlocksRoot     = Join(PlcRoot, "Blocks")
UdtsRoot       = Join(PlcRoot, "UDTs")
TagTablesRoot  = Join(PlcRoot, "TagTables")
KindRoot(kind) = BlocksRoot | UdtsRoot | TagTablesRoot
KindDir(kind,p)= Join(KindRoot(kind), Safe(p[0]), Safe(p[1]), …)   // empty p → KindRoot
```

| Call | `directory` | `path` |
|---|---|---|
| One object (`*_get_file` / `*_put_file`) | `KindDir(kind, object.Path)` | same TIA folder array |
| File list item | `KindDir(kind, item.Path)` per item | per item |
| Dir whole PLC (one kind) | `KindRoot` for that kind | omit |
| Dir one TIA folder | `KindDir(kind, startPath)` | that start folder |

Recursive Dir: TIA subfolders relative to `path` become **subdirectories of `directory`**. Example: `directory = …\PLC_LIB\Blocks\TopCtrl_Func\Visu`, `path = ["TopCtrl_Func","Visu"]` writes `tvDummy.s7dcl` into Visu and `Panel\tvAreaIndex.s7dcl` under it — the API never inserts `Blocks`, project, or PLC names itself.

- `plcName` comes from `tia_plc_devices_list` (`PlcName`), not `DeviceName`.
- Several PLCs share one `workspace`, separated by `<PlcName>`.
- Never mix kinds in one folder: blocks only under `Blocks\`, UDTs under `UDTs\`, tag tables under `TagTables\`.
- Know-how-protected objects are skipped on export and cannot be overwritten.

Prefer **Dir** (`*_get_dir`, `*_put_dir`) or **FileList** over many single-file calls.

Default `workspace` if the user does not specify one: ask once, then store it in `.tia-workspace.json` at the git root.

### Concrete call shapes

Export one FC at `TopCtrl_Func/Basis/tbDTLtoString`:

```
tia_plc_blocks_get_file(
  …, blockName: "tbDTLtoString",
  directory: "C:\\TiaWorkspace\\TopCtrl_V3_Eng\\PLC_LIB\\Blocks\\TopCtrl_Func\\Basis",
  path: ["TopCtrl_Func", "Basis"])
→ Files: …\Blocks\TopCtrl_Func\Basis\tbDTLtoString.s7dcl (+ .s7res)
```

Export several into different folders (`*_get_file_list` / `*_put_file_list`):

```
items: [
  { directory: "…\\PLC_LIB\\Blocks\\TopCtrl_Func\\Basis", blockName: "tbDTLtoString", path: ["TopCtrl_Func","Basis"] },
  { directory: "…\\PLC_LIB\\Blocks\\TopCtrl_Func\\Basis", blockName: "tbUDINTtoStringFormat", path: ["TopCtrl_Func","Basis"] }
]
```

Export/import a folder:

```
tia_plc_blocks_get_dir(…, directory: "…\\PLC_LIB\\Blocks\\TopCtrl_Func\\Basis",
  path: ["TopCtrl_Func","Basis"], recursive: false)
tia_plc_blocks_put_dir(…, directory: same, path: same, recursive: false)
```

Derive `path` from a local file: relative folders under the **kind root** (`Blocks` / `UDTs` / `TagTables`), split into a string array — do **not** include the kind folder itself. File stem = `BlockName` / `UdtName` / `TagTableName`.

## Git model

The git repo lives at the chosen workspace root (typically `workspace`, which contains `<projectName>\<PlcName>\Blocks|UDTs|TagTables\…`).

- Tag `tia/last-export` = last committed workspace snapshot used to detect **file-side** edits (workflow 2).
- TIA-side edits are detected via `.tia-workspace.json` element `modifiedDate` vs list-tool `ModifiedDate` — not via git.
- Never `git commit` unless the user asked, except the **first** `git init` baseline of workflow 1, and an optional `tia/last-export` tag update after a successful sync/export.
- After import, TIA often **re-formats** SD files. Round-trip is not byte-stable. Treat TIA export as canonical formatting.

`.gitignore` in the workspace:

```
.tia-workspace.json
.tia-merge-tmp/
*.log
*.tmp
```

Keep `.s7dcl`, `.s7res`, `.xml` tracked.

## Tool choice

| Goal | Tool |
|---|---|
| List open TIA projects | `tia_projects_list` |
| List PLCs | `tia_plc_devices_list` |
| Inventory + TIA timestamps | `tia_plc_blocks_list`, `tia_plc_udts_list`, `tia_plc_tagtables_list` (`ModifiedDate` UTC) |
| Full/folder export | `tia_plc_udts_get_dir`, `tia_plc_tagtables_get_dir`, `tia_plc_blocks_get_dir` (`recursive` true) |
| Selected export (sync TIA-only) | `*_get_file_list` |
| Full/folder import | `tia_plc_udts_put_dir`, then `tia_plc_tagtables_put_dir`, then `tia_plc_blocks_put_dir` |
| Selected import (sync file-only) | `*_put_file` or `*_put_file_list` |
| Compile one FC/FB/OB/UDT | `tia_plc_compile_item` (`itemType` `"Block"` or `"UDT"`) |
| Compile PLC software (changes) | `tia_plc_compile_plc` |
| Save TIA | `tia_projects_save` |

Import does **not** compile. Library type instances: `put_dir` writes them as in-test; single `put_file` / `put_file_list` need `libraryMode` `"InTest"` or they return 409.

Batch results: `IsSuccess` false with `StatusCode` 400 still has `Data.Succeeded`, `Data.Failed`, `Data.Items`, `Data.Errors`, `Data.Skipped`. Read them. Successful items were written; failed items are not rolled back. Partial export (e.g. SimaticSdEnabler missing for some blocks) is normal — keep the successes and report the errors.

---

## `.tia-workspace.json` (element catalog)

Written at the git root (listed in `.gitignore`). Sync compares **per element**, not a single global timestamp.

Each successfully exported element has one entry with the TIA `ModifiedDate` and the relative workspace files that belong to it:

```json
{
  "workspace": "C:\\TiaWorkspace",
  "projectName": "<Name>",
  "plcs": [
    {
      "deviceName": "<DeviceName>",
      "deviceItemName": "<DeviceItemName>",
      "plcName": "<PlcName>",
      "elements": [
        {
          "kind": "Block",
          "name": "tbDTLtoString",
          "path": ["TopCtrl_Func", "Basis"],
          "modifiedDate": "2026-10-21T11:18:00.000Z",
          "files": [
            "TopCtrl_V3_Eng\\PLC_LIB\\Blocks\\TopCtrl_Func\\Basis\\tbDTLtoString.s7dcl",
            "TopCtrl_V3_Eng\\PLC_LIB\\Blocks\\TopCtrl_Func\\Basis\\tbDTLtoString.s7res"
          ]
        },
        {
          "kind": "UDT",
          "name": "typeExample",
          "path": ["Types"],
          "modifiedDate": "2026-10-20T09:00:00.000Z",
          "files": [
            "TopCtrl_V3_Eng\\PLC_LIB\\UDTs\\Types\\typeExample.s7dcl"
          ]
        },
        {
          "kind": "TagTable",
          "name": "Default tag table",
          "path": [],
          "modifiedDate": "2026-10-19T15:30:00.000Z",
          "files": [
            "TopCtrl_V3_Eng\\PLC_LIB\\TagTables\\Default tag table.xml"
          ]
        }
      ]
    }
  ]
}
```

| Field | Meaning |
|---|---|
| `kind` | `"Block"`, `"UDT"`, or `"TagTable"` |
| `name` | TIA object name (`BlockName` / `UdtName` / `TagTableName`) |
| `path` | TIA folder array (empty = PLC root) |
| `modifiedDate` | TIA `ModifiedDate` (UTC ISO-8601) at last successful sync/export of this element |
| `files` | Paths relative to `workspace`, all siblings of that element (`.s7dcl` / `.s7res` / `.xml`) |

Element identity for sync: `(plcName, kind, name, path)`. Know-how-skipped or failed exports get **no** entry (or keep a previous entry only if the local files still exist and the user wants them).

Map a relative file to an element: under `<projectName>\<PlcName>\Blocks|UDTs|TagTables\`, kind = that folder, `path` = folders under the kind root, `name` = file stem. `.s7dcl` and `.s7res` with the same stem are one element.

## Helper scripts (use these — do not hand-edit the catalog)

Location: `scripts/` next to this skill (`update_catalog.py`, `compare_sync.py`, `apply_conflict_choices.py`, `merge_element.py`, `catalog_lib.py`). Stdlib only (+ `git` / optional `code` on PATH). Run with `python` or `python3` from any cwd; pass absolute paths.

Save MCP tool results as JSON files first (full `McpApiResponse` or bare `Data` is fine).

### Build / refresh catalog — `update_catalog.py`

**Abbild** (rebuild all elements for one PLC from list tools + files on disk):

```bash
python scripts/update_catalog.py \
  --catalog "<workspace>\\.tia-workspace.json" \
  --workspace "<workspace>" --project "<projectName>" --plc "<PlcName>" \
  --device-name "<DeviceName>" --device-item-name "<DeviceItemName>" \
  --blocks-list blocks_list.json --udts-list udts_list.json --tagtables-list tagtables_list.json \
  --replace-plc --scan-files
```

**After sync export/import** (update only transferred elements; prefer fresh list JSON + optional batch `Data`):

```bash
python scripts/update_catalog.py \
  --catalog "<workspace>\\.tia-workspace.json" \
  --workspace "<workspace>" --project "<projectName>" --plc "<PlcName>" \
  --blocks-list blocks_list.json --udts-list udts_list.json --tagtables-list tagtables_list.json \
  --touched synced.json --scan-files
```

Optional: `--batch-blocks` / `--batch-udts` / `--batch-tagtables` with get/put dir or file_list responses (uses `Items[].ModifiedDate` and `Items[].Files`).

`synced.json` is a JSON array of `{"kind","name","path"}` (from `apply_conflict_choices.py` export/import outputs, or hand-built).

### Sync comparison — `compare_sync.py`

```bash
python scripts/compare_sync.py \
  --catalog "<workspace>\\.tia-workspace.json" --plc "<PlcName>" \
  --blocks-list blocks_list.json --udts-list udts_list.json --tagtables-list tagtables_list.json \
  --out-json sync_plan.json --out-plan-md sync_plan.md --print-md
```

Produces `tiaOnly` / `fileOnly` / `both` (conflicts) with catalog/TIA/file dates. Show the conflict table to the user; do not transfer conflicts until choices exist.

### Apply conflict answers — `apply_conflict_choices.py`

```bash
python scripts/apply_conflict_choices.py \
  --plan sync_plan.json --choices "1:TIA,2:Skip,3:File,4:Merge" \
  --out-export export_touched.json --out-import import_touched.json \
  --out-skip skip.json --out-merge merge_touched.json
```

Then export `export_touched.json`, import `import_touched.json`, run the Merge pipeline for `merge_touched.json`, and `update_catalog.py --touched` on successfully transferred elements (with fresh list JSON).

### 3-way merge one file — `merge_element.py`

```bash
python scripts/merge_element.py \
  --workspace "<workspace>" --git-ref tia/last-export \
  --rel-file "<project>\\<Plc>\\Blocks\\...\\Name.s7dcl" \
  --tia-temp-file "<workspace>\\.tia-merge-tmp\\...\\Name.s7dcl" \
  --open-editor
```

Writes the merge result into the workspace file. Status JSON on stdout: `clean` | `conflicted` | `error`. With `--open-editor` and `conflicted`, starts **VS Code** (`code --merge base local other output`). OpenCode has no 3-way merge UI.

---

## Workflow 1 — Projekt Abbilden

Complete export into a local directory, fill the element catalog, and create a git baseline.

Checklist:

- [ ] Bootstrap session
- [ ] Choose one `workspace` for the project
- [ ] Export UDTs, tag tables, blocks (per PLC; `directory` = kind root)
- [ ] List TIA objects; write `.tia-workspace.json` with every element's `modifiedDate` + `files`
- [ ] `git init` if needed, first commit, tag `tia/last-export`
- [ ] Report skipped/failed objects

Steps:

1. Create `workspace` if it does not exist.
2. For each target PLC:
   1. `tia_plc_udts_get_dir` with `directory` = `UdtsRoot` (omit `path`, `recursive` true)
   2. `tia_plc_tagtables_get_dir` with `directory` = `TagTablesRoot`
   3. `tia_plc_blocks_get_dir` with `directory` = `BlocksRoot`
3. Inspect `Data.Failed`, `Data.Errors`, `Data.Skipped` (know-how-protected, export-incapable languages, SimaticSdEnabler).
4. Optionally `tia_plc_blocks_list_export_capabilities` if many blocks failed.
5. Build the element catalog with the helper script (do not assemble the JSON by hand):
   1. Save `tia_plc_udts_list`, `tia_plc_tagtables_list`, `tia_plc_blocks_list` responses as JSON files.
   2. Optionally save successful `*_get_dir` responses as `--batch-*` inputs.
   3. Run `update_catalog.py --replace-plc --scan-files` (see Helper scripts).
6. In the git root:

```bash
git init
git add -A
git status
git commit -m "tia-export: baseline of <projectName>"
git tag -f tia/last-export
```

7. Summarize: PLC count, exported UDT/table/block counts, catalog entry count, skipped know-how, failed exports, git root.

Do not close the TIA project. Do not compile.

---

## Workflow 2 — Projekt Sync

Reconcile TIA and the workspace **per element** using the catalog + git. Never run a blind full `*_get_dir` / `*_put_dir` for sync.

TIA timestamps: `ModifiedDate` on list tools (UTC ISO-8601). Blocks/UDTs: Openness `ModifiedDate`. Tag tables: Openness `ModifiedTimeStamp`, exposed as `ModifiedDate`.

Checklist:

- [ ] Bootstrap + read `.tia-workspace.json`
- [ ] Save list-tool JSON; run `compare_sync.py` → `sync_plan.json` + conflict table
- [ ] Present **conflict decision table**; wait for per-row choices (`TIA` / `File` / `Skip` / `Merge`)
- [ ] `apply_conflict_choices.py` → export / import / skip / merge touched lists
- [ ] Export / import as planned; Skip = no transfer; Merge = 3-way pipeline
- [ ] Re-list TIA; `update_catalog.py --touched` for transferred elements
- [ ] Optionally compile, save, update `tia/last-export`

Steps:

1. If no `.tia-workspace.json` with `elements`, or no git repo / tag `tia/last-export`, run workflow 1 first.
2. Call `tia_plc_blocks_list`, `tia_plc_udts_list`, `tia_plc_tagtables_list`; save each response as JSON.
3. Run `compare_sync.py` (see Helper scripts). It builds:
   - **TIA-only** — catalog `modifiedDate` ≠ list `ModifiedDate` (or missing catalog entry), and not file-changed
   - **File-only** — git changes under kind trees mapped to elements, and not TIA-changed
   - **Both** — conflicts
   - **Vanished** — in catalog but missing from TIA lists (report only; do not delete files unless asked)
4. **Conflict decision table** — if `both` is non-empty, **stop transfers** and show the Markdown table from `compare_sync.py` (`--print-md` / `--out-plan-md`) so the user can walk every conflict. Do not start export/import/merge of conflicts until the user answered.

Choice semantics:

| Choice | Meaning |
|---|---|
| `TIA` | Export from TIA into the workspace (overwrite local) |
| `File` | Import local files into TIA |
| `Skip` | Do not sync — leave TIA, files, and catalog unchanged |
| `Merge` | 3-way merge: Base = `tia/last-export`, Local = workspace, Other = TIA temp export → then import merged result to TIA |

Collect answers as `1:TIA, 2:Skip, 3:File, 4:Merge`. Partial answers → ask again for remaining `?` (never treat blank as Skip). `compare_sync.py --out-plan-md` also lists planned TIA-only / File-only rows.

5. Run `apply_conflict_choices.py --plan sync_plan.json --choices "…" --out-merge merge_touched.json`. If it exits with unresolved rows, stop and ask again.
6. Export `export_touched.json` via `*_get_file_list` (`directory` = `KindDir(kind, Path)`). Import `import_touched.json` via put tools in order UDTs → tag tables → blocks (`libraryMode: "InTest"` when needed). Skip list: no transfer.
7. **Merge pipeline** for each element in `merge_touched.json` (requires tag `tia/last-export` and a Base file in that commit; otherwise refuse Merge and ask for another choice):
   1. Create `<workspace>\.tia-merge-tmp\<stamp>\` (gitignored).
   2. Export the TIA object with `*_get_file` into the matching kind/path under that temp root (`directory` = temp `KindDir`).
   3. For each relative file of the element (`.s7dcl` / `.s7res` / `.xml` separately): run `merge_element.py --workspace … --rel-file … --tia-temp-file … --open-editor`.
   4. If status is `conflicted`: wait until the user finishes in **VS Code** (or resolves markers manually). If `code` is missing, tell the user to install VS Code (installer option) and stop that element.
   5. When the workspace file is clean (no conflict markers): `*_put_file` / put list for that element (import merged Local → TIA).
   6. Delete the temp tree when done (or leave it only if the user wants to inspect failures).
   Tag-tables / STL XML often merge poorly — if `merge-file` produces nonsense, fall back to `TIA` / `File` / `Skip` after asking the user.
8. Do **not** `*_put_dir` / `*_get_dir` the whole tree during sync.
9. **Update the catalog** with `update_catalog.py` (not for Skip; include successful Merge imports):
   1. Call list tools again; save JSON.
   2. Build `synced.json` = successfully exported ∪ imported ∪ merged elements.
   3. `update_catalog.py --touched synced.json --scan-files` (optional `--batch-*` from the transfer responses).
10. If the user wants a consistency check: `tia_plc_compile_plc`, then report `State`, `ErrorCount`, `WarningCount`, and error `Messages`.
11. Ask before `tia_projects_save`.
12. After the sync transfers succeed, optionally commit and `git tag -f tia/last-export` when the working tree matches the intended baseline (user decides).

Report: exported from TIA, imported to TIA, merged, skipped conflicts, remaining unanswered (should be none), catalog updates.

---

## Workflow 3 — Funktion konsistent erstellen/anpassen

Edit locally, import, compile in TIA, read errors, fix the local file, repeat until it compiles.

Checklist:

- [ ] Bootstrap
- [ ] Create or edit the local `.s7dcl` (and `.s7res` if comments/IDs change)
- [ ] Import that block
- [ ] `tia_plc_compile_item`
- [ ] Map errors to local lines, fix, repeat
- [ ] Stop when `State` is `Success` / `Information` / `Warning` with `ErrorCount` 0

### Create

1. Pick folder `path` (TIA groups) and a unique `BlockName`.
2. Copy a **similar existing** FC/FB from the workspace as template (preferred over writing SD from scratch).
3. Change the declaration name to `BlockName`. Keep `Format` SimaticData/SD (`.s7dcl`).
4. Place files at `KindDir(Blocks, path)\<BlockName>.s7dcl`.
5. If the template `.s7res` is copied, keep every `{ S7_MLC := "…" }` ID in sync. Adding a comment requires a new unique ID in both files.
6. `tia_plc_blocks_put_file` with `directory` = `KindDir(Blocks, path)` and the same TIA `path`. For a new block TIA assigns a number unless `blockNumber` is set.

### Edit

1. If the file is missing locally, `tia_plc_blocks_get_file` with `directory` = `KindDir(Blocks, path)` and that TIA `path`.
2. Edit the local `.s7dcl`. Do not strip generated SD headers/attributes you do not understand.
3. `tia_plc_blocks_put_file` with the same `directory` and `path`. Prefer non-library blocks, or pass `libraryMode: "InTest"`.

### Compile loop

Max **8** iterations. After that, stop and show remaining errors.

1. `tia_plc_compile_item` with `name` = block name, `itemType` `"Block"` (or `"UDT"`).
2. `IsSuccess` true only means the compile **ran**. Read `Data.State`, `ErrorCount`, `Messages`.
3. On errors:
   - `Messages[].Description` is the TIA text.
   - `Messages[].BlockName` may be a **dependency**, not the file you edited. Fix that object too (export if needed).
   - For **SCL**, the last `Location` element is the SCL line. In `.s7dcl` that maps to: line of the `NETWORK` keyword **plus** that number. See [s7dcl.md](s7dcl.md).
4. Patch the local file (smallest change that addresses the message). Import again. Compile again.
5. Do not import unchanged files. Do not compile the whole PLC each iteration; use `tia_plc_compile_plc` once at the end if callers/interfaces may be affected.
6. A consistent block is not compiled again; previous warnings may disappear. That is normal.

When `ErrorCount` is 0, summarize what changed and whether TIA still has unsaved project changes. Save only if asked.

### Failures to handle explicitly

| Symptom | Action |
|---|---|
| 400 missing `directory` | You forgot the absolute folder; build `KindDir(kind, path)` |
| 409 library type | Retry with `libraryMode` `"InTest"`; do not `Release` unless the user asks |
| Know-how-protected | Stop; cannot overwrite |
| Missing `.s7res` IDs | Import rejected before TIA changes; fix IDs |
| Both `.s7dcl` and `.xml` present | Pass `format` `"SimaticData/SD"` or `"SimaticML/XML"` |
| Batch `Failed` > 0 but `Succeeded` > 0 | Keep successes; report `Errors[]` (often SimaticSdEnabler / pre-V20.4 SD) |
| Compile error in another block/UDT | Export that object, fix or report as missing type/tag |
| Import `Messages` with warnings | Continue to compile; TIA may still accept the block |

---

## Rules

- Always pass `processId`, `projectName`, `deviceName`, `deviceItemName` from list tools. Do not guess names.
- Always build `directory` yourself (`KindRoot` / `KindDir` with `Blocks`, `UDTs`, or `TagTables`). Never expect the API to add project, PLC, or kind folders.
- Prefer file tools over `tia_plc_blocks_get` / `put` (content in the prompt) except for tiny patches.
- Do not delete local files that TIA skipped (know-how). Git should keep the last exported copy if it exists; do not treat skip as delete.
- Do not move UDTs: existing UDTs stay in their TIA folder even if `path` differs.
- Hardware, HMI screens, and "rebuild all" are out of scope (Openness has no rebuild-all).
- After destructive or large imports, say so in the summary (`Created` true vs overwrite).
