#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
MANIFEST = REPO / "FILE_MANIFEST.txt"
IGNORED_PARTS = {".git", "node_modules", ".next", "bin", "obj", "TestResults", "coverage", "__pycache__", "cache", "tsconfig.tsbuildinfo"}


def entries() -> list[str]:
    result=[]
    for p in sorted(REPO.rglob("*")):
        if not p.is_file() or p == MANIFEST or any(part in IGNORED_PARTS for part in p.parts): continue
        rel=p.relative_to(REPO).as_posix()
        digest=hashlib.sha256(p.read_bytes()).hexdigest()
        result.append(f"{digest}  {rel}")
    return result


def main() -> int:
    ap=argparse.ArgumentParser(); ap.add_argument("--check", action="store_true"); args=ap.parse_args()
    expected="\n".join(entries())+"\n"
    if args.check:
        actual=MANIFEST.read_text(encoding="utf-8") if MANIFEST.exists() else ""
        if actual != expected:
            print("FILE_MANIFEST.txt is stale; run python scripts/generate_manifest.py")
            return 1
        print("MANIFEST CHECK PASSED")
        return 0
    MANIFEST.write_text(expected,encoding="utf-8")
    print(f"wrote {MANIFEST.relative_to(REPO)} with {len(entries())} files")
    return 0

if __name__ == "__main__": raise SystemExit(main())
