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
3. Every file tool needs an **absolute** `directory` on the machine that runs TIA Portal and the REST API. The API writes/reads files **directly in that folder**. It never appends project or PLC names.

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

```
<workspace>\<projectName>\<PlcName>\<TIA folder…>\<Name>.s7dcl
<workspace>\<projectName>\<PlcName>\<TIA folder…>\<Name>.s7res   (optional)
<workspace>\<projectName>\<PlcName>\<TIA folder…>\<Name>.xml     (STL/GRAPH or tag tables)
```

Build paths with `Safe(name)` = replace invalid filename characters with `_`.

```
PlcRoot   = Join(workspace, Safe(projectName), Safe(plcName))
PlcDir(p) = Join(PlcRoot, Safe(p[0]), Safe(p[1]), …)   // empty p → PlcRoot
```

| Call | `directory` | `path` |
|---|---|---|
| One object (`*_get_file` / `*_put_file`) | `PlcDir(object.Path)` | same TIA folder array |
| File list item | `PlcDir(item.Path)` per item | per item |
| Dir whole PLC | `PlcRoot` | omit |
| Dir one TIA folder | `PlcDir(startPath)` | that start folder |

Recursive Dir: TIA subfolders relative to `path` become **subdirectories of `directory`**. Example: `directory = …\PLC_LIB\TopCtrl_Func\Visu`, `path = ["TopCtrl_Func","Visu"]` writes `tvDummy.s7dcl` into Visu and `Panel\tvAreaIndex.s7dcl` under it — never `…\TopCtrl_V3_Eng\PLC_LIB\…` composed by the API.

- `plcName` comes from `tia_plc_devices_list` (`PlcName`), not `DeviceName`.
- Several PLCs share one `workspace`, separated by `<PlcName>`.
- Blocks, UDTs and tag tables of one PLC share the same tree; Put*Dir tools distinguish kinds by file content.
- Know-how-protected objects are skipped on export and cannot be overwritten.

Prefer **Dir** (`*_get_dir`, `*_put_dir`) or **FileList** over many single-file calls.

Default `workspace` if the user does not specify one: ask once, then store it in `.tia-workspace.json` at the git root.

### Concrete call shapes

Export one FC at `TopCtrl_Func/Basis/tbDTLtoString`:

```
tia_plc_blocks_get_file(
  …, blockName: "tbDTLtoString",
  directory: "C:\\TiaWorkspace\\TopCtrl_V3_Eng\\PLC_LIB\\TopCtrl_Func\\Basis",
  path: ["TopCtrl_Func", "Basis"])
→ Files: …\Basis\tbDTLtoString.s7dcl (+ .s7res)
```

Export several into different folders (`*_get_file_list` / `*_put_file_list`):

```
items: [
  { directory: "…\\PLC_LIB\\TopCtrl_Func\\Basis", blockName: "tbDTLtoString", path: ["TopCtrl_Func","Basis"] },
  { directory: "…\\PLC_LIB\\TopCtrl_Func\\Basis", blockName: "tbUDINTtoStringFormat", path: ["TopCtrl_Func","Basis"] }
]
```

Export/import a folder:

```
tia_plc_blocks_get_dir(…, directory: "…\\PLC_LIB\\TopCtrl_Func\\Basis",
  path: ["TopCtrl_Func","Basis"], recursive: false)
tia_plc_blocks_put_dir(…, directory: same, path: same, recursive: false)
```

Derive `path` from a local file: relative folders under `PlcRoot`, split into a string array. File stem = `BlockName` / `UdtName` / `TagTableName`.

## Git model

The git repo lives at the chosen workspace root (typically `workspace`, which contains `<projectName>\<PlcName>\`).

- Tag or branch `tia/last-export` = last snapshot that came **from TIA**.
- Working tree / commits after that = **local** edits.
- Never `git commit` unless the user asked, except the **first** `git init` baseline of workflow 1, and a `tia/last-export` tag update after a successful export (lightweight, no extra commit required if you can `git tag -f tia/last-export`).
- After import, TIA often **re-formats** SD files. Round-trip is not byte-stable. Treat TIA export as canonical formatting.

`.gitignore` in the workspace:

```
.tia-workspace.json
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
| Selected export (TIA-newer) | `*_get_file_list` |
| Full/folder import | `tia_plc_udts_put_dir`, then `tia_plc_tagtables_put_dir`, then `tia_plc_blocks_put_dir` |
| Selected import | `*_put_file` or `*_put_file_list` |
| Compile one FC/FB/OB/UDT | `tia_plc_compile_item` (`itemType` `"Block"` or `"UDT"`) |
| Compile PLC software (changes) | `tia_plc_compile_plc` |
| Save TIA | `tia_projects_save` |

Import does **not** compile. Library type instances: `put_dir` writes them as in-test; single `put_file` / `put_file_list` need `libraryMode` `"InTest"` or they return 409.

Batch results: `IsSuccess` false with `StatusCode` 400 still has `Data.Succeeded`, `Data.Failed`, `Data.Items`, `Data.Errors`, `Data.Skipped`. Read them. Successful items were written; failed items are not rolled back. Partial export (e.g. SimaticSdEnabler missing for some blocks) is normal — keep the successes and report the errors.

---

## Workflow 1 — Projekt Abbilden

Complete export into a local directory and a git repo for change detection.

Checklist:

- [ ] Bootstrap session
- [ ] Choose one `workspace` for the project
- [ ] Export UDTs, tag tables, blocks (per PLC; `directory` = `PlcRoot`)
- [ ] Write `.tia-workspace.json` including `lastExportUtc`
- [ ] `git init` if needed, first commit, tag `tia/last-export`
- [ ] Report skipped/failed objects

Steps:

1. Create `workspace` if it does not exist.
2. For each target PLC:
   1. `tia_plc_udts_get_dir` with `directory` = `PlcRoot` (omit `path`, `recursive` true)
   2. `tia_plc_tagtables_get_dir` with the same `directory`
   3. `tia_plc_blocks_get_dir` with the same `directory`
3. Inspect `Data.Failed`, `Data.Errors`, `Data.Skipped` (know-how-protected, export-incapable languages, SimaticSdEnabler).
4. Optionally `tia_plc_blocks_list_export_capabilities` if many blocks failed.
5. Write `.tia-workspace.json`:

```json
{
  "workspace": "C:\\TiaWorkspace",
  "projectName": "<Name>",
  "lastExportUtc": "2026-10-21T11:18:00.000Z",
  "plcs": [
    {
      "deviceName": "<DeviceName>",
      "deviceItemName": "<DeviceItemName>",
      "plcName": "<PlcName>"
    }
  ]
}
```

`lastExportUtc` is the latest `ModifiedDate` seen in the export (or the time of the export if none). Used by workflow 2.

6. In the git root:

```bash
git init
git add -A
git status
git commit -m "tia-export: baseline of <projectName>"
git tag -f tia/last-export
```

7. Summarize: PLC count, exported UDT/table/block counts, skipped know-how, failed exports, git root.

Do not close the TIA project. Do not compile.

---

## Workflow 2 — Projekt Sync

Export newer TIA elements into the local tree; import locally changed elements into TIA.

TIA timestamps: `ModifiedDate` on list/get (UTC ISO-8601). Blocks/UDTs: Openness `ModifiedDate`. Tag tables: Openness `ModifiedTimeStamp`, exposed as `ModifiedDate`. **Never export first** — that would overwrite local edits.

Checklist:

- [ ] Bootstrap + read `.tia-workspace.json`
- [ ] Capture local changes vs `tia/last-export`
- [ ] List TIA objects; select those with `ModifiedDate` > `lastExportUtc`
- [ ] Stash overlapping local files
- [ ] Export only TIA-newer objects (`*_get_file_list`)
- [ ] Restore local changes; report conflicts
- [ ] Import only local-changed files
- [ ] Optionally compile and save
- [ ] Update `lastExportUtc` and `tia/last-export`

Steps:

1. If no git repo or no tag `tia/last-export`, run workflow 1 first (or tag HEAD if the current tree is a known clean export).
2. Local set:

```bash
git status --porcelain
git diff --name-only tia/last-export
git ls-files --others --exclude-standard
```

Relevant files: `.s7dcl`, `.s7res`, `.xml` under `<projectName>\<PlcName>\` in the workspace.

3. TIA-newer set: `tia_plc_blocks_list`, `tia_plc_udts_list`, `tia_plc_tagtables_list`. Keep objects whose `ModifiedDate` is greater than `lastExportUtc`. If `lastExportUtc` is missing, treat all as newer (or run workflow 1).
4. Conflicts = files that are both locally modified and TIA-newer. Do not overwrite them; list them and keep the local version unless the user says otherwise.
5. If there are non-conflicting TIA-newer objects, `*_get_file_list` those objects (not a full `*_get_dir`, unless most of the PLC changed). Each item: `directory` = `PlcDir(Path)`, name, `path` = TIA folder array.
6. Import **only** locally changed objects, in this order:
   - UDTs → `tia_plc_udts_put_file_list` (or `put_file`)
   - Tag tables → `tia_plc_tagtables_put_file_list`
   - Blocks → `tia_plc_blocks_put_file_list`
   Derive name from the file stem. Derive `path` from the relative folder under `PlcRoot`. Pass that absolute folder as `directory`. For library instances pass `libraryMode: "InTest"`.
7. Do **not** blindly `*_put_dir` on the whole tree during sync; that would re-import TIA-unchanged files and can open library types for editing.
8. If the user wants a consistency check: `tia_plc_compile_plc`, then report `State`, `ErrorCount`, `WarningCount`, and error `Messages`.
9. Ask before `tia_projects_save`.
10. After a successful TIA→local export:
    - Set `lastExportUtc` to the latest `ModifiedDate` among listed objects (or the export time).
    - Commit TIA-only updates if the user wants a commit.
    - `git tag -f tia/last-export` only on a tree that matches TIA (no unresolved local diffs), **or** tag after committing the export and before re-applying local work.

Report three lists: imported local files, files updated from TIA, conflicts.

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
4. Place files at `PlcDir(path)\<BlockName>.s7dcl`.
5. If the template `.s7res` is copied, keep every `{ S7_MLC := "…" }` ID in sync. Adding a comment requires a new unique ID in both files.
6. `tia_plc_blocks_put_file` with `directory` = `PlcDir(path)` and the same TIA `path`. For a new block TIA assigns a number unless `blockNumber` is set.

### Edit

1. If the file is missing locally, `tia_plc_blocks_get_file` with `directory` = `PlcDir(path)` and that TIA `path`.
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
| 400 missing `directory` | You forgot the absolute folder; build `PlcDir(path)` |
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
- Always build `directory` yourself (`PlcRoot` / `PlcDir`). Never expect the API to add project or PLC folders.
- Prefer file tools over `tia_plc_blocks_get` / `put` (content in the prompt) except for tiny patches.
- Do not delete local files that TIA skipped (know-how). Git should keep the last exported copy if it exists; do not treat skip as delete.
- Do not move UDTs: existing UDTs stay in their TIA folder even if `path` differs.
- Hardware, HMI screens, and "rebuild all" are out of scope (Openness has no rebuild-all).
- After destructive or large imports, say so in the summary (`Created` true vs overwrite).
