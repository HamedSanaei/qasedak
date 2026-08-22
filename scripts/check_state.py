#!/usr/bin/env python3
from __future__ import annotations

import json
import re
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]


def main() -> int:
    errors: list[str] = []
    state = json.loads((REPO / ".agent-state/PROJECT_STATE.json").read_text(encoding="utf-8"))
    tasks = (REPO / "docs/project/TASKS.md").read_text(encoding="utf-8")
    milestones = (REPO / "docs/project/MILESTONES.md").read_text(encoding="utf-8")
    status = (REPO / "docs/project/STATUS.md").read_text(encoding="utf-8")
    current = state["currentTask"]
    if f"## {current} " not in tasks: errors.append(f"current task {current} missing from task tracker")
    if state["currentMilestone"] not in milestones: errors.append("current milestone missing from milestones")
    if current not in status: errors.append("STATUS.md does not identify current task")
    task_blocks = re.split(r"(?=^## M\d{2}-\d{3} )", tasks, flags=re.MULTILINE)[1:]
    for block in task_blocks:
        heading = block.splitlines()[0]
        if "**Status:**" not in block: errors.append(f"task lacks status: {heading}")
        if "**Suggested commit:** `" not in block: errors.append(f"task lacks suggested commit: {heading}")
        if "**Completion contract:**" not in block: errors.append(f"task lacks completion contract: {heading}")
    if not state.get("graphify", {}).get("required"): errors.append("Graphify must remain required in machine state")
    if state.get("graphify", {}).get("status") == "healthy":
        completed = state.get("lastCompletedTask")
        evidence = (REPO / ".agent-state/GRAPHIFY_EVIDENCE.md").read_text(encoding="utf-8")
        if completed and f"| {completed} |" not in evidence:
            errors.append(f"healthy Graphify state requires evidence for last completed task {completed}")
    if errors:
        print("STATE CHECK FAILED")
        for e in errors: print(f"- {e}")
        return 1
    print(f"STATE CHECK PASSED ({len(task_blocks)} tasks, current={current})")
    return 0


if __name__ == "__main__": raise SystemExit(main())
