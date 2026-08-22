#!/usr/bin/env python3
from __future__ import annotations

import argparse, json, subprocess, sys
from pathlib import Path

REPO=Path(__file__).resolve().parents[1]

def run(*args:str)->None:
    subprocess.run([sys.executable,*args],cwd=REPO,check=True)


def main()->int:
    ap=argparse.ArgumentParser();ap.add_argument("--task",required=True);args=ap.parse_args()
    state=json.loads((REPO/".agent-state/PROJECT_STATE.json").read_text(encoding="utf-8"))
    if state.get("lastCompletedTask") != args.task:
        raise SystemExit(f"PROJECT_STATE lastCompletedTask must be {args.task} before finalize; got {state.get('lastCompletedTask')}")
    evidence=(REPO/".agent-state/GRAPHIFY_EVIDENCE.md").read_text(encoding="utf-8")
    if f"| {args.task} |" not in evidence:
        raise SystemExit(f"missing Graphify evidence row for {args.task}")
    run("scripts/check_architecture.py")
    run("scripts/check_docs.py")
    run("scripts/check_state.py")
    run("scripts/generate_manifest.py")
    print(f"agent finalization passed for {args.task}; run scripts/verify.py --full for toolchain gates")
    return 0

if __name__ == "__main__":raise SystemExit(main())
