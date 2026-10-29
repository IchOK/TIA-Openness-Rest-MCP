"""Shared helpers for .tia-workspace.json catalog + sync compare."""

from __future__ import annotations

import json
import os
import subprocess
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable

KIND_FOLDERS = {
    "Block": "Blocks",
    "UDT": "UDTs",
    "TagTable": "TagTables",
}
FOLDER_TO_KIND = {v.lower(): k for k, v in KIND_FOLDERS.items()}
FILE_SUFFIXES = (".s7dcl", ".s7res", ".xml")


def load_json(path: str | Path) -> Any:
    with open(path, "r", encoding="utf-8-sig") as f:
        return json.load(f)


def save_json(path: str | Path, data: Any) -> None:
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)
        f.write("\n")


def unwrap_mcp_data(payload: Any) -> Any:
    """Accept raw tool Data, full McpApiResponse, or a list."""
    if isinstance(payload, dict):
        if "Data" in payload:
            return payload["Data"]
        if "data" in payload:
            return payload["data"]
    return payload


def prop(obj: dict, *names: str, default=None):
    for name in names:
        if name in obj and obj[name] is not None:
            return obj[name]
        lower = name[:1].lower() + name[1:] if name else name
        if lower in obj and obj[lower] is not None:
            return obj[lower]
    return default


def normalize_path(path: Any) -> list[str]:
    if path is None:
        return []
    if isinstance(path, str):
        parts = [p for p in path.replace("\\", "/").split("/") if p]
        return parts
    if isinstance(path, list):
        return [str(p) for p in path if p is not None and str(p) != ""]
    return []


def path_key(path: list[str]) -> tuple[str, ...]:
    return tuple(path)


def element_key(kind: str, name: str, path: list[str]) -> tuple[str, str, tuple[str, ...]]:
    return (kind, name, path_key(path))


def format_path(path: list[str]) -> str:
    return "/".join(path) if path else "(PLC root)"


def parse_utc(value: str | None) -> datetime | None:
    if not value:
        return None
    text = value.strip()
    if text.endswith("Z"):
        text = text[:-1] + "+00:00"
    try:
        dt = datetime.fromisoformat(text)
    except ValueError:
        return None
    if dt.tzinfo is None:
        dt = dt.replace(tzinfo=timezone.utc)
    return dt.astimezone(timezone.utc)


def utc_equal(a: str | None, b: str | None) -> bool:
    da, db = parse_utc(a), parse_utc(b)
    if da is None or db is None:
        return (a or "") == (b or "")
    return da == db


def to_iso_z(dt: datetime) -> str:
    return dt.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.%f")[:-3] + "Z"


def file_mtime_iso(path: Path) -> str | None:
    if not path.is_file():
        return None
    return to_iso_z(datetime.fromtimestamp(path.stat().st_mtime, tz=timezone.utc))


def newest_file_mtime(workspace: Path, rel_files: Iterable[str]) -> str | None:
    newest: datetime | None = None
    for rel in rel_files:
        p = workspace / rel.replace("/", os.sep)
        if not p.is_file():
            continue
        dt = datetime.fromtimestamp(p.stat().st_mtime, tz=timezone.utc)
        if newest is None or dt > newest:
            newest = dt
    return to_iso_z(newest) if newest else None


def rel_to_workspace(workspace: Path, absolute: str | Path) -> str:
    abs_path = Path(absolute).resolve()
    try:
        rel = abs_path.relative_to(workspace.resolve())
    except ValueError:
        # Fall back to as-is normalized relative-looking path
        return str(absolute).replace("/", "\\")
    return str(rel).replace("/", "\\")


def discover_element_files(workspace: Path, project_name: str, plc_name: str, kind: str, name: str, tia_path: list[str]) -> list[str]:
    folder = KIND_FOLDERS[kind]
    kind_dir = workspace / project_name / plc_name / folder
    for part in tia_path:
        kind_dir = kind_dir / part
    files: list[str] = []
    for suffix in FILE_SUFFIXES:
        candidate = kind_dir / f"{name}{suffix}"
        if candidate.is_file():
            files.append(rel_to_workspace(workspace, candidate))
    return files


def map_rel_file_to_element(project_name: str, rel_file: str) -> dict | None:
    """Map workspace-relative file to kind/name/path. Returns None if not a PLC element file."""
    parts = Path(rel_file.replace("\\", "/")).parts
    if len(parts) < 4:
        return None
    if parts[0] != project_name:
        return None
    plc_name = parts[1]
    kind_folder = parts[2]
    kind = FOLDER_TO_KIND.get(kind_folder.lower())
    if kind is None:
        return None
    file_name = parts[-1]
    lower = file_name.lower()
    if not lower.endswith(FILE_SUFFIXES):
        return None
    stem = Path(file_name).stem
    tia_path = list(parts[3:-1])
    return {
        "plcName": plc_name,
        "kind": kind,
        "name": stem,
        "path": tia_path,
        "file": rel_file.replace("/", "\\"),
    }


def list_items_from_payload(kind: str, payload: Any) -> list[dict]:
    data = unwrap_mcp_data(payload)
    if isinstance(data, dict) and "Items" in data:
        data = data["Items"]
    if not isinstance(data, list):
        raise ValueError(f"Expected list Data for {kind}, got {type(data).__name__}")
    items: list[dict] = []
    for raw in data:
        if not isinstance(raw, dict):
            continue
        if kind == "Block":
            name = prop(raw, "BlockName", "Name")
        elif kind == "UDT":
            name = prop(raw, "UdtName", "Name")
        else:
            name = prop(raw, "TagTableName", "Name")
        if not name:
            continue
        items.append({
            "kind": kind,
            "name": str(name),
            "path": normalize_path(prop(raw, "Path")),
            "modifiedDate": prop(raw, "ModifiedDate"),
            "raw": raw,
        })
    return items


def batch_items_from_payload(kind: str, payload: Any) -> list[dict]:
    """Parse get_dir / get_file_list / put_* Data.Items (successes)."""
    data = unwrap_mcp_data(payload)
    if not isinstance(data, dict):
        raise ValueError("Batch payload Data must be an object with Items")
    items_raw = prop(data, "Items") or []
    out: list[dict] = []
    for raw in items_raw:
        if not isinstance(raw, dict):
            continue
        if kind == "Block":
            name = prop(raw, "BlockName", "Name")
        elif kind == "UDT":
            name = prop(raw, "UdtName", "Name")
        else:
            name = prop(raw, "TagTableName", "Name")
        if not name:
            continue
        files = prop(raw, "Files") or []
        if isinstance(files, str):
            files = [files]
        out.append({
            "kind": kind,
            "name": str(name),
            "path": normalize_path(prop(raw, "Path")),
            "modifiedDate": prop(raw, "ModifiedDate"),
            "files": [str(f) for f in files],
            "raw": raw,
        })
    return out


def ensure_plc(catalog: dict, plc_name: str, device_name: str | None = None, device_item_name: str | None = None) -> dict:
    plcs = catalog.setdefault("plcs", [])
    for plc in plcs:
        if plc.get("plcName") == plc_name:
            if device_name:
                plc["deviceName"] = device_name
            if device_item_name:
                plc["deviceItemName"] = device_item_name
            plc.setdefault("elements", [])
            return plc
    plc = {
        "deviceName": device_name or "",
        "deviceItemName": device_item_name or "",
        "plcName": plc_name,
        "elements": [],
    }
    plcs.append(plc)
    return plc


def upsert_element(plc: dict, element: dict) -> None:
    elements = plc.setdefault("elements", [])
    key = element_key(element["kind"], element["name"], element.get("path") or [])
    for i, existing in enumerate(elements):
        if element_key(existing["kind"], existing["name"], existing.get("path") or []) == key:
            elements[i] = element
            return
    elements.append(element)


def catalog_index(plc: dict) -> dict[tuple, dict]:
    index = {}
    for el in plc.get("elements") or []:
        index[element_key(el["kind"], el["name"], el.get("path") or [])] = el
    return index


def git_changed_rel_files(git_root: Path, ref: str = "tia/last-export") -> list[str]:
    files: set[str] = set()

    def run(*args: str) -> list[str]:
        try:
            proc = subprocess.run(
                ["git", *args],
                cwd=str(git_root),
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                check=False,
            )
        except FileNotFoundError:
            return []
        if proc.returncode != 0:
            return []
        return [line.strip().replace("/", "\\") for line in proc.stdout.splitlines() if line.strip()]

    # Exists check for ref
    probe = subprocess.run(
        ["git", "rev-parse", "--verify", ref],
        cwd=str(git_root),
        capture_output=True,
        check=False,
    )
    if probe.returncode == 0:
        files.update(run("diff", "--name-only", ref))
    files.update(run("ls-files", "--others", "--exclude-standard"))
    # Also unstaged/staged vs HEAD for safety when tag equals HEAD but working tree dirty
    files.update(run("diff", "--name-only"))
    files.update(run("diff", "--name-only", "--cached"))
    return sorted(files)


def markdown_table(rows: list[dict], plc_name: str, choice_default: str = "?") -> str:
    lines = [
        "| # | PLC | Kind | Name | Path | Catalog date | TIA date | File date | Files | Choice |",
        "|---|---|---|---|---|---|---|---|---|---|",
    ]
    for i, row in enumerate(rows, start=1):
        files = ", ".join(f"`{f}`" for f in (row.get("files") or [])[:4])
        if row.get("files") and len(row["files"]) > 4:
            files += f", +{len(row['files']) - 4} more"
        choice = row.get("choice", choice_default)
        lines.append(
            "| {num} | {plc} | {kind} | {name} | {path} | {cat} | {tia} | {file} | {files} | {choice} |".format(
                num=i,
                plc=plc_name,
                kind=row.get("kind", ""),
                name=row.get("name", ""),
                path=format_path(row.get("path") or []),
                cat=row.get("catalogDate") or "—",
                tia=row.get("tiaDate") or "—",
                file=row.get("fileDate") or "—",
                files=files or "—",
                choice=choice,
            )
        )
    return "\n".join(lines)
