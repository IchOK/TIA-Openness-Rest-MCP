#!/usr/bin/env python3
"""Apply user conflict choices to a sync_plan.json from compare_sync.py.

Choices cover the Both table row numbers (1-based), e.g.:
  1:TIA, 2:Skip, 3:File, 4:Merge

  python apply_conflict_choices.py --plan sync_plan.json --choices "1:TIA,2:Skip,3:File,4:Merge" \\
    --out-export export_touched.json --out-import import_touched.json \\
    --out-skip skip.json --out-merge merge_touched.json

Also merges planned tiaOnly → export and fileOnly → import.
Unresolved (?) rows cause exit code 4 unless --allow-pending.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

from catalog_lib import load_json, save_json

_CHOICE_MAP = {"tia": "TIA", "file": "File", "skip": "Skip", "merge": "Merge"}


def parse_choices(text: str) -> dict[int, str]:
    result: dict[int, str] = {}
    for part in re.split(r"[\s,;]+", text.strip()):
        if not part:
            continue
        m = re.fullmatch(r"(\d+)\s*:\s*(TIA|File|Skip|Merge)", part, flags=re.IGNORECASE)
        if not m:
            raise ValueError(f"Invalid choice '{part}' (expected N:TIA|File|Skip|Merge)")
        result[int(m.group(1))] = _CHOICE_MAP[m.group(2).lower()]
    return result


def normalize_choice(raw: object) -> str:
    if not isinstance(raw, str):
        return "?"
    c = raw.strip()
    key = c.lower()
    if key in _CHOICE_MAP:
        return _CHOICE_MAP[key]
    if c.upper() == "TIA":
        return "TIA"
    return "?"


def touched(row: dict) -> dict:
    return {"kind": row["kind"], "name": row["name"], "path": row.get("path") or []}


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--plan", required=True, help="sync_plan.json from compare_sync.py")
    p.add_argument("--choices", default="", help='e.g. "1:TIA,2:Skip,3:File,4:Merge"')
    p.add_argument("--choices-file", help="Optional file with same content as --choices")
    p.add_argument("--out-export", required=True)
    p.add_argument("--out-import", required=True)
    p.add_argument("--out-skip", help="Optional JSON of skipped conflicts")
    p.add_argument("--out-merge", help="Optional JSON of Merge conflicts")
    p.add_argument("--allow-pending", action="store_true")
    args = p.parse_args(argv)

    plan = load_json(args.plan)
    text = args.choices
    if args.choices_file:
        text = Path(args.choices_file).read_text(encoding="utf-8")
    choices = parse_choices(text) if text.strip() else {}

    both = plan.get("both") or []
    pending = []
    export_rows = list(plan.get("tiaOnly") or [])
    import_rows = list(plan.get("fileOnly") or [])
    skip_rows = []
    merge_rows = []

    for i, row in enumerate(both, start=1):
        choice = normalize_choice(choices.get(i) or row.get("choice") or "?")
        if choice == "?":
            pending.append(i)
            continue
        if choice == "TIA":
            export_rows.append(row)
        elif choice == "File":
            import_rows.append(row)
        elif choice == "Merge":
            merge_rows.append(row)
        else:
            skip_rows.append(row)

    if pending and not args.allow_pending:
        print(f"error: unresolved conflict rows: {pending}", file=sys.stderr)
        return 4

    save_json(args.out_export, [touched(r) for r in export_rows])
    save_json(args.out_import, [touched(r) for r in import_rows])
    if args.out_skip:
        save_json(args.out_skip, [touched(r) for r in skip_rows])
    if args.out_merge:
        save_json(args.out_merge, [touched(r) for r in merge_rows])
    elif merge_rows:
        print("warning: merge rows present but --out-merge not set", file=sys.stderr)

    print(
        f"export={len(export_rows)} import={len(import_rows)} "
        f"merge={len(merge_rows)} skip={len(skip_rows)} pending={len(pending)}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
