#!/usr/bin/env python3
"""Update .tia-workspace.json element modifiedDate/files from MCP tool JSON dumps.

Abbild (full rebuild for one PLC from list tools + filesystem):
  python update_catalog.py --catalog C:\\TiaWorkspace\\.tia-workspace.json \\
    --workspace C:\\TiaWorkspace --project TopCtrl_V3_Eng --plc PLC_LIB \\
    --blocks-list blocks_list.json --udts-list udts_list.json --tagtables-list tagtables_list.json \\
    --replace-plc --scan-files

Sync (refresh only transferred elements; dates from list and/or batch Items):
  python update_catalog.py --catalog ... --workspace ... --project ... --plc ... \\
    --blocks-list blocks_list.json --udts-list udts_list.json --tagtables-list tagtables_list.json \\
    --touched synced.json --scan-files

  synced.json = [{"kind":"Block","name":"x","path":["A","B"]}, ...]
  or pass --batch-blocks export_or_import.json (uses Data.Items; kind inferred by flag)

Also accepts ModifiedDate/Files directly from --batch-* without requiring those keys in --touched.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

from catalog_lib import (
    batch_items_from_payload,
    discover_element_files,
    ensure_plc,
    list_items_from_payload,
    load_json,
    normalize_path,
    prop,
    rel_to_workspace,
    save_json,
    upsert_element,
)


def _load_optional(path: str | None):
    if not path:
        return None
    return load_json(path)


def _index_lists(*groups: list[dict]) -> dict[tuple, dict]:
    index = {}
    for group in groups:
        for item in group:
            key = (item["kind"], item["name"], tuple(item["path"]))
            index[key] = item
    return index


def _element_from_sources(
    workspace: Path,
    project: str,
    plc_name: str,
    kind: str,
    name: str,
    path: list[str],
    list_item: dict | None,
    batch_item: dict | None,
    scan_files: bool,
) -> dict:
    modified = None
    files_abs: list[str] = []
    if batch_item:
        modified = batch_item.get("modifiedDate") or modified
        files_abs = list(batch_item.get("files") or [])
    if list_item:
        modified = list_item.get("modifiedDate") or modified

    files_rel: list[str] = []
    if files_abs:
        files_rel = [rel_to_workspace(workspace, f) for f in files_abs]
    elif scan_files:
        files_rel = discover_element_files(workspace, project, plc_name, kind, name, path)

    # Prefer unique sorted for stability
    files_rel = sorted(dict.fromkeys(files_rel))

    return {
        "kind": kind,
        "name": name,
        "path": path,
        "modifiedDate": modified,
        "files": files_rel,
    }


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description="Update .tia-workspace.json from MCP list/batch JSON")
    p.add_argument("--catalog", required=True, help="Path to .tia-workspace.json")
    p.add_argument("--workspace", help="Absolute workspace root (default: catalog.workspace)")
    p.add_argument("--project", help="Project name (default: catalog.projectName)")
    p.add_argument("--plc", required=True, help="PlcName")
    p.add_argument("--device-name", default=None)
    p.add_argument("--device-item-name", default=None)
    p.add_argument("--blocks-list", help="JSON dump of tia_plc_blocks_list response")
    p.add_argument("--udts-list", help="JSON dump of tia_plc_udts_list response")
    p.add_argument("--tagtables-list", help="JSON dump of tia_plc_tagtables_list response")
    p.add_argument("--batch-blocks", help="JSON dump of blocks get/put dir or file_list Data")
    p.add_argument("--batch-udts", help="JSON dump of udts get/put dir or file_list Data")
    p.add_argument("--batch-tagtables", help="JSON dump of tagtables get/put dir or file_list Data")
    p.add_argument(
        "--touched",
        help='JSON array of {"kind","name","path"} to update (sync). Ignored with --replace-plc.',
    )
    p.add_argument(
        "--replace-plc",
        action="store_true",
        help="Rebuild all elements for this PLC from list tools (Abbild).",
    )
    p.add_argument(
        "--scan-files",
        action="store_true",
        help="If batch Items lack Files, discover .s7dcl/.s7res/.xml under kind roots.",
    )
    p.add_argument(
        "--require-modified-date",
        action="store_true",
        help="Fail if an updated element has no ModifiedDate from list/batch.",
    )
    args = p.parse_args(argv)

    catalog_path = Path(args.catalog)
    if catalog_path.is_file():
        catalog = load_json(catalog_path)
    else:
        catalog = {}

    workspace = Path(args.workspace or catalog.get("workspace") or catalog_path.parent)
    project = args.project or catalog.get("projectName")
    if not project:
        print("error: --project or catalog.projectName required", file=sys.stderr)
        return 2

    catalog["workspace"] = str(workspace)
    catalog["projectName"] = project

    blocks_list = list_items_from_payload("Block", _load_optional(args.blocks_list)) if args.blocks_list else []
    udts_list = list_items_from_payload("UDT", _load_optional(args.udts_list)) if args.udts_list else []
    tables_list = list_items_from_payload("TagTable", _load_optional(args.tagtables_list)) if args.tagtables_list else []
    list_index = _index_lists(blocks_list, udts_list, tables_list)

    batch_index: dict[tuple, dict] = {}
    if args.batch_blocks:
        for item in batch_items_from_payload("Block", load_json(args.batch_blocks)):
            batch_index[(item["kind"], item["name"], tuple(item["path"]))] = item
    if args.batch_udts:
        for item in batch_items_from_payload("UDT", load_json(args.batch_udts)):
            batch_index[(item["kind"], item["name"], tuple(item["path"]))] = item
    if args.batch_tagtables:
        for item in batch_items_from_payload("TagTable", load_json(args.batch_tagtables)):
            batch_index[(item["kind"], item["name"], tuple(item["path"]))] = item

    plc = ensure_plc(catalog, args.plc, args.device_name, args.device_item_name)

    targets: list[tuple[str, str, list[str]]] = []
    if args.replace_plc:
        if not list_index:
            print("error: --replace-plc needs at least one --*-list JSON", file=sys.stderr)
            return 2
        # Only keep objects that appear in lists; prefer those with batch success or existing files
        for key, item in list_index.items():
            targets.append((item["kind"], item["name"], item["path"]))
        plc["elements"] = []
    else:
        if args.touched:
            for raw in load_json(args.touched):
                kind = prop(raw, "kind")
                name = prop(raw, "name")
                path = normalize_path(prop(raw, "path"))
                if not kind or not name:
                    print(f"error: invalid touched entry: {raw}", file=sys.stderr)
                    return 2
                targets.append((str(kind), str(name), path))
        # Always include batch successes
        for key, item in batch_index.items():
            targets.append((item["kind"], item["name"], item["path"]))
        # Dedupe preserving order
        seen = set()
        uniq = []
        for t in targets:
            k = (t[0], t[1], tuple(t[2]))
            if k in seen:
                continue
            seen.add(k)
            uniq.append(t)
        targets = uniq
        if not targets:
            print("error: provide --touched and/or --batch-* (or use --replace-plc)", file=sys.stderr)
            return 2

    updated = 0
    missing_date = []
    for kind, name, path in targets:
        key = (kind, name, tuple(path))
        element = _element_from_sources(
            workspace,
            project,
            args.plc,
            kind,
            name,
            path,
            list_index.get(key),
            batch_index.get(key),
            args.scan_files or args.replace_plc,
        )
        if not element.get("modifiedDate"):
            missing_date.append(key)
            if args.require_modified_date:
                continue
        # On replace-plc, skip elements with neither files nor batch (failed export)
        if args.replace_plc and not element["files"] and key not in batch_index:
            # Still record if list says it exists but export failed? Skill says no entry for failed.
            continue
        if args.replace_plc and not element["files"] and key in batch_index and not batch_index[key].get("files"):
            # batch without files — still keep if scan found nothing; allow date-only
            pass
        upsert_element(plc, element)
        updated += 1

    if args.require_modified_date and missing_date:
        print("error: missing ModifiedDate for:", file=sys.stderr)
        for kind, name, path_t in missing_date:
            print(f"  {kind} {name} {list(path_t)}", file=sys.stderr)
        return 3

    # Stable sort for readability
    plc["elements"] = sorted(
        plc.get("elements") or [],
        key=lambda e: (e.get("kind", ""), "/".join(e.get("path") or []), e.get("name", "")),
    )

    save_json(catalog_path, catalog)
    print(f"updated {updated} element(s) → {catalog_path}")
    print(f"plc {args.plc}: {len(plc.get('elements') or [])} catalog entries")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
