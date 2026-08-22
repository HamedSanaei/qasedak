#!/usr/bin/env python3
from __future__ import annotations

import argparse, json, shutil, subprocess
from pathlib import Path

REPO=Path(__file__).resolve().parents[1]


def main() -> int:
    ap=argparse.ArgumentParser(); ap.add_argument("--task",required=True); args=ap.parse_args()
    tasks=(REPO/"docs/project/TASKS.md").read_text(encoding="utf-8")
    if f"## {args.task} " not in tasks: raise SystemExit(f"unknown task: {args.task}")
    exe=shutil.which("graphify")
    if not exe:
        print("BLOCKED: Graphify CLI is not installed. Per AGENTS.md, feature edits must stop unless a human records an explicit bypass.")
        return 2
    result=subprocess.run([exe,"--version"],cwd=REPO,text=True,capture_output=True)
    if result.returncode:
        print("BLOCKED: graphify --version failed", result.stderr)
        return 2
    graph_candidates=[REPO/"graphify-out/graph.json", REPO/"graph.json"]
    if not any(p.exists() and p.stat().st_size > 0 for p in graph_candidates):
        print("BLOCKED: no initialized Graphify graph. Run `graphify . --no-viz` (or current-version equivalent) first.")
        return 2
    state=json.loads((REPO/".agent-state/PROJECT_STATE.json").read_text(encoding="utf-8"))
    print(f"preflight ready: task={args.task}, current={state['currentTask']}, graphify={result.stdout.strip() or result.stderr.strip()}")
    print('Next mandatory step: run a bounded `graphify query "..." --budget 1200` and record evidence.')
    return 0

if __name__ == "__main__": raise SystemExit(main())
