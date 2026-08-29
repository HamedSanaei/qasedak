#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import subprocess
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
MANIFEST = REPO / "FILE_MANIFEST.txt"


def tracked_files(repo: Path) -> list[str]:
    """Return the sorted list of git-tracked files, relative to the repo root.

    Discovery relies on `git ls-files`, so local/untracked/generated artifacts
    (e.g. .env, StrykerOutput, graphify-out/graph.html, node_modules, bin/obj)
    can never leak into the manifest. A clean CI checkout of the same commit
    therefore produces the identical manifest as a developer worktree.
    """
    proc = subprocess.run(
        ["git", "ls-files", "-z"],
        cwd=repo,
        check=True,
        text=True,
        capture_output=True,
    )
    paths = proc.stdout.split("\0")
    return sorted(p for p in paths if p and p != MANIFEST.name)


def _canonical_bytes(path: Path) -> bytes:
    """Return the file bytes in git's canonical form.

    The repository normalizes text to LF (see .gitattributes: `* text=auto
    eol=lf`). Working trees on Windows may still carry CRLF (checkout filters
    or tools writing locally). Hashing raw working-tree bytes would make the
    manifest differ between a Linux CI checkout (LF) and a Windows developer
    worktree (CRLF), so we normalize CRLF->LF before hashing. This keeps a
    clean checkout and any developer environment byte-identical for --check.
    """
    return path.read_bytes().replace(b"\r\n", b"\n")


def entries() -> list[str]:
    result: list[str] = []
    for rel in tracked_files(REPO):
        digest = hashlib.sha256(_canonical_bytes(REPO / rel)).hexdigest()
        result.append(f"{digest}  {rel}")
    return result


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--check", action="store_true")
    args = ap.parse_args()

    expected = "\n".join(entries()) + "\n"
    if args.check:
        actual = MANIFEST.read_text(encoding="utf-8") if MANIFEST.exists() else ""
        if actual != expected:
            print("FILE_MANIFEST.txt is stale; run python scripts/generate_manifest.py")
            return 1
        print("MANIFEST CHECK PASSED")
        return 0

    MANIFEST.write_text(expected, encoding="utf-8")
    print(f"wrote {MANIFEST.relative_to(REPO)} with {len(entries())} files")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())