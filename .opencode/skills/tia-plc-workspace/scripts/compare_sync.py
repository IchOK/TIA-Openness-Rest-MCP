#!/usr/bin/env python3
"""Build sync comparison sets from catalog + list-tool JSON + git.

  python compare_sync.py --catalog C:\\TiaWorkspace\\.tia-workspace.json \\
    --plc PLC_LIB \\
    --blocks-list blocks_list.json --udts-list udts_list.json --tagtables-list tagtables_list.json \\
    --out-json sync_plan.json --out-conflicts-md conflicts.md

Writes sync_plan.json with tiaOnly / fileOnly / both / unchanged and prints a
Markdown conflict table (also optional planned TIA-only / File-only tables).
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from catalog_lib import (
    catalog_index,
    element_key,
    git_changed_rel_files,
    list_items_from_payload,
    load_json,
    map_rel_file_to_element,
    markdown_table,
    newest_file_mtime,
    save_json,
    utc_equal,
)


def _load_lists(args) -> list[dict]:
    items: list[dict] = []
    if args.blocks_list:
        items.extend(list_items_from_payload("Block", load_json(args.blocks_list)))
    if args.udts_list:
        items.extend(list_items_from_payload("UDT", load_json(args.udts_list)))
    if args.tagtables_list:
        items.extend(list_items_from_payload("TagTable", load_json(args.tagtables_list)))
    return items


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description="Compare catalog ModifiedDate vs TIA lists and git file changes")
    p.add_argument("--catalog", required=True)
    p.add_argument("--plc", required=True)
    p.add_argument("--workspace", help="Override catalog.workspace")
    p.add_argument("--project", help="Override catalog.projectName")
    p.add_argument("--blocks-list", required=True)
    p.add_argument("--udts-list", required=True)
    p.add_argument("--tagtables-list", required=True)
    p.add_argument("--git-root", help="Git root (default: workspace)")
    p.add_argument("--git-ref", default="tia/last-export")
    p.add_argument("--out-json", required=True, help="Write sync plan JSON")
    p.add_argument("--out-conflicts-md", help="Write Markdown conflict table")
    p.add_argument("--out-plan-md", help="Write Markdown for TIA-only + File-only + Both")
    p.add_argument("--print-md", action="store_true", help="Print Markdown tables to stdout")
    args = p.parse_args(argv)

    catalog = load_json(args.catalog)
    workspace = Path(args.workspace or catalog.get("workspace") or Path(args.catalog).parent)
    project = args.project or catalog.get("projectName")
    if not project:
        print("error: projectName missing", file=sys.stderr)
        return 2

    plc = None
    for candidate in catalog.get("plcs") or []:
        if candidate.get("plcName") == args.plc:
            plc = candidate
            break
    if plc is None:
        print(f"error: PLC '{args.plc}' not in catalog", file=sys.stderr)
        return 2

    cat_idx = catalog_index(plc)
    tia_items = _load_lists(args)
    tia_idx = {element_key(i["kind"], i["name"], i["path"]): i for i in tia_items}

    # TIA-changed: missing in catalog or modifiedDate differs
    tia_changed_keys: set[tuple] = set()
    for key, item in tia_idx.items():
        cat = cat_idx.get(key)
        if cat is None or not utc_equal(cat.get("modifiedDate"), item.get("modifiedDate")):
            tia_changed_keys.add(key)

    # File-changed via git → element keys
    git_root = Path(args.git_root or workspace)
    changed_files = git_changed_rel_files(git_root, args.git_ref)
    file_changed_keys: set[tuple] = set()
    files_by_key: dict[tuple, list[str]] = {}
    for rel in changed_files:
        mapped = map_rel_file_to_element(project, rel)
        if mapped is None or mapped["plcName"] != args.plc:
            continue
        key = element_key(mapped["kind"], mapped["name"], mapped["path"])
        file_changed_keys.add(key)
        files_by_key.setdefault(key, []).append(mapped["file"])

    def row_for(key: tuple) -> dict:
        kind, name, path_t = key
        path = list(path_t)
        cat = cat_idx.get(key)
        tia = tia_idx.get(key)
        files = list(cat.get("files") or []) if cat else []
        # Prefer catalog files; merge git-touched files
        for f in files_by_key.get(key, []):
            if f not in files:
                files.append(f)
        file_date = newest_file_mtime(workspace, files) if files else None
        return {
            "kind": kind,
            "name": name,
            "path": path,
            "catalogDate": (cat or {}).get("modifiedDate"),
            "tiaDate": (tia or {}).get("modifiedDate"),
            "fileDate": file_date,
            "files": files,
        }

    tia_only_keys = sorted(tia_changed_keys - file_changed_keys)
    file_only_keys = sorted(file_changed_keys - tia_changed_keys)
    both_keys = sorted(tia_changed_keys & file_changed_keys)
    unchanged_keys = sorted(set(cat_idx) & set(tia_idx) - tia_changed_keys - file_changed_keys)

    # Elements only in catalog (deleted in TIA)
    vanished = sorted(set(cat_idx) - set(tia_idx))

    plan = {
        "plcName": args.plc,
        "workspace": str(workspace),
        "projectName": project,
        "gitRef": args.git_ref,
        "tiaOnly": [row_for(k) | {"set": "tiaOnly", "choice": "TIA"} for k in tia_only_keys],
        "fileOnly": [row_for(k) | {"set": "fileOnly", "choice": "File"} for k in file_only_keys],
        "both": [row_for(k) | {"set": "both", "choice": "?"} for k in both_keys],
        "unchangedCount": len(unchanged_keys),
        "vanishedFromTia": [
            {
                "kind": k[0],
                "name": k[1],
                "path": list(k[2]),
                "catalogDate": cat_idx[k].get("modifiedDate"),
                "files": cat_idx[k].get("files") or [],
            }
            for k in vanished
        ],
    }

    save_json(args.out_json, plan)

    conflicts_md = markdown_table(plan["both"], args.plc, choice_default="?")
    if args.out_conflicts_md:
        Path(args.out_conflicts_md).write_text(conflicts_md + "\n", encoding="utf-8")

    plan_md_parts = [
        f"## Sync plan — {args.plc}",
        "",
        f"- TIA-only: **{len(plan['tiaOnly'])}** (export)",
        f"- File-only: **{len(plan['fileOnly'])}** (import)",
        f"- Both (conflicts): **{len(plan['both'])}** (choose TIA / File / Skip)",
        f"- Unchanged: **{plan['unchangedCount']}**",
        f"- Vanished in TIA (catalog only): **{len(plan['vanishedFromTia'])}**",
        "",
    ]
    if plan["both"]:
        plan_md_parts += ["### Conflicts (decide per row)", "", conflicts_md, ""]
    if plan["tiaOnly"]:
        plan_md_parts += ["### TIA-only (planned export)", "", markdown_table(plan["tiaOnly"], args.plc, "TIA"), ""]
    if plan["fileOnly"]:
        plan_md_parts += ["### File-only (planned import)", "", markdown_table(plan["fileOnly"], args.plc, "File"), ""]

    plan_md = "\n".join(plan_md_parts)
    if args.out_plan_md:
        Path(args.out_plan_md).write_text(plan_md + "\n", encoding="utf-8")
    if args.print_md or not args.out_plan_md:
        print(plan_md)

    print(
        f"wrote {args.out_json}: "
        f"tiaOnly={len(plan['tiaOnly'])} fileOnly={len(plan['fileOnly'])} both={len(plan['both'])}",
        file=sys.stderr,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
