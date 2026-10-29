#!/usr/bin/env python3
"""3-way merge one workspace file: Base=git-ref, Local=workspace, Other=TIA temp export.

  python merge_element.py \\
    --workspace C:\\TiaWorkspace --git-ref tia/last-export \\
    --rel-file Project\\PLC\\Blocks\\Folder\\Name.s7dcl \\
    --tia-temp-file C:\\TiaWorkspace\\.tia-merge-tmp\\...\\Name.s7dcl \\
    --open-editor

Prints one JSON object to stdout:
  {"status":"clean"|"conflicted"|"error", "workspaceFile":"...", "message":"...", ...}

On conflicted + --open-editor, launches VS Code: code --merge <base> <local> <other> <output>
"""

from __future__ import annotations

import argparse
import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

CONFLICT_MARKERS = ("<<<<<<<", "=======", ">>>>>>>")


def _run(args: list[str], cwd: Path | None = None) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        args,
        cwd=str(cwd) if cwd else None,
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )


def find_code_exe() -> str | None:
    which = shutil.which("code") or shutil.which("code.cmd")
    if which:
        return which
    candidates = [
        Path(os.environ.get("LOCALAPPDATA", "")) / "Programs" / "Microsoft VS Code" / "bin" / "code.cmd",
        Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Microsoft VS Code" / "bin" / "code.cmd",
        Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Microsoft VS Code" / "bin" / "code.cmd",
    ]
    for c in candidates:
        if c.is_file():
            return str(c)
    return None


def git_show_to_file(git_root: Path, ref: str, rel_file: str, dest: Path) -> str | None:
    """Write blob to dest. Returns error message or None."""
    git_path = rel_file.replace("\\", "/")
    proc = subprocess.run(
        ["git", "show", f"{ref}:{git_path}"],
        cwd=str(git_root),
        capture_output=True,
        check=False,
    )
    if proc.returncode != 0:
        err = proc.stderr.decode("utf-8", errors="replace") or proc.stdout.decode("utf-8", errors="replace")
        return (err or f"git show failed for {ref}:{git_path}").strip()
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_bytes(proc.stdout)
    return None


def has_conflict_markers(path: Path) -> bool:
    try:
        text = path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return False
    return any(m in text for m in CONFLICT_MARKERS)


def emit(result: dict) -> int:
    print(json.dumps(result, ensure_ascii=False))
    status = result.get("status")
    if status == "clean":
        return 0
    if status == "conflicted":
        return 1
    return 2


def main(argv: list[str] | None = None) -> int:
    p = argparse.ArgumentParser(description="3-way merge workspace file vs TIA temp vs git base")
    p.add_argument("--workspace", required=True, help="Git / workspace root")
    p.add_argument("--git-ref", default="tia/last-export")
    p.add_argument("--rel-file", required=True, help="Path relative to workspace (Local)")
    p.add_argument("--tia-temp-file", required=True, help="Absolute path of TIA export (Other)")
    p.add_argument("--open-editor", action="store_true", help="On conflict, launch VS Code merge UI")
    p.add_argument("--keep-work", action="store_true", help="Keep temp base/local/other copies")
    args = p.parse_args(argv)

    workspace = Path(args.workspace).resolve()
    rel = args.rel_file.replace("/", "\\")
    local_path = (workspace / rel).resolve()
    other_path = Path(args.tia_temp_file).resolve()

    if not workspace.is_dir():
        return emit({"status": "error", "message": f"workspace not found: {workspace}"})
    if not local_path.is_file():
        return emit({"status": "error", "message": f"local file missing: {local_path}", "workspaceFile": str(local_path)})
    if not other_path.is_file():
        return emit({"status": "error", "message": f"TIA temp file missing: {other_path}", "workspaceFile": str(local_path)})

    probe = _run(["git", "rev-parse", "--verify", args.git_ref], cwd=workspace)
    if probe.returncode != 0:
        return emit({
            "status": "error",
            "message": f"git ref missing: {args.git_ref}",
            "workspaceFile": str(local_path),
        })

    stage_root = workspace / ".tia-merge-tmp" / "_stage"
    stage_root.mkdir(parents=True, exist_ok=True)
    work = Path(tempfile.mkdtemp(prefix="file-", dir=str(stage_root)))

    base_path = work / "base" / local_path.name
    local_copy = work / "local" / local_path.name
    other_copy = work / "other" / local_path.name
    for d in (base_path.parent, local_copy.parent, other_copy.parent):
        d.mkdir(parents=True, exist_ok=True)

    err = git_show_to_file(workspace, args.git_ref, rel, base_path)
    if err:
        return emit({
            "status": "error",
            "message": f"no base at {args.git_ref}:{rel} — refuse Merge ({err})",
            "workspaceFile": str(local_path),
        })

    shutil.copy2(local_path, local_copy)
    shutil.copy2(other_path, other_copy)

    # merge-file writes into the first file argument (current / local)
    merge_proc = _run([
        "git", "merge-file",
        "-L", "local",
        "-L", "base",
        "-L", "tia",
        str(local_copy),
        str(base_path),
        str(other_copy),
    ])
    # exit 0 = clean, >0 = conflicts (or error). Negative = signal.
    conflicted = merge_proc.returncode > 0 or has_conflict_markers(local_copy)
    if merge_proc.returncode < 0:
        return emit({
            "status": "error",
            "message": f"git merge-file failed: {merge_proc.stderr or merge_proc.stdout}",
            "workspaceFile": str(local_path),
        })

    # Always write merge result to workspace (with markers if conflicted)
    shutil.copy2(local_copy, local_path)

    result = {
        "status": "conflicted" if conflicted else "clean",
        "workspaceFile": str(local_path),
        "relFile": rel,
        "gitRef": args.git_ref,
        "tiaTempFile": str(other_path),
        "baseFile": str(base_path),
        "mergeExitCode": merge_proc.returncode,
        "editorOpened": False,
    }

    if conflicted and args.open_editor:
        code = find_code_exe()
        if not code:
            result["message"] = (
                "Merge conflict; VS Code (code) not found. "
                "Install VS Code (installer option) or resolve conflict markers manually."
            )
        else:
            # code --merge <base> <local> <remote> <result>
            # VS Code docs: base, current(local), incoming(other), merge result path
            ed = _run([
                code, "--wait", "--merge",
                str(base_path),
                str(local_path),
                str(other_copy),
                str(local_path),
            ])
            result["editorOpened"] = True
            result["editorExitCode"] = ed.returncode
            if has_conflict_markers(local_path):
                result["status"] = "conflicted"
                result["message"] = "VS Code closed but conflict markers remain"
            else:
                result["status"] = "clean"
                result["message"] = "Resolved in VS Code"

    if not args.keep_work and result["status"] == "clean":
        shutil.rmtree(work, ignore_errors=True)
    else:
        result["workDir"] = str(work)

    return emit(result)


if __name__ == "__main__":
    raise SystemExit(main())
