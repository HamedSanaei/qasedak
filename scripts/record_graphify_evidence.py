#!/usr/bin/env python3
from __future__ import annotations

import argparse
from datetime import datetime, timezone
from pathlib import Path

REPO=Path(__file__).resolve().parents[1]
LOG=REPO/".agent-state/GRAPHIFY_EVIDENCE.md"


def main() -> int:
    ap=argparse.ArgumentParser()
    ap.add_argument("--task", required=True)
    ap.add_argument("--status", required=True, choices=["healthy","unhealthy","unavailable","bypassed"])
    ap.add_argument("--version", default="unknown")
    ap.add_argument("--command", required=True)
    ap.add_argument("--query", required=True)
    ap.add_argument("--outputs", default="graphify-out/graph.json; GRAPH_REPORT.md")
    ap.add_argument("--notes", default="")
    args=ap.parse_args()
    date=datetime.now(timezone.utc).date().isoformat()
    safe=lambda s:s.replace("|", "\\|").replace("\n"," ")
    line=f"| {date} | {safe(args.task)} | {safe(args.status)} | {safe(args.version)} | {safe(args.command)} | {safe(args.query)} | {safe(args.outputs)} | {safe(args.notes)} |\n"
    if not LOG.exists(): raise SystemExit("Graphify evidence log missing")
    with LOG.open("a",encoding="utf-8") as f:f.write(line)
    print(f"recorded Graphify evidence for {args.task}: {args.status}")
    return 0

if __name__ == "__main__": raise SystemExit(main())
