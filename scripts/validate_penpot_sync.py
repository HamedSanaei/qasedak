#!/usr/bin/env python3
"""CI wrapper: run the deterministic Penpot sync manifest validation (node --test).

The validation logic itself lives in frontend/Qasedak.Web/tests/penpot-sync.test.mjs so
`npm test` and this wrapper execute the same checks. Offline by design — live Penpot
synchronization happens through MCP during agent design-sync tasks only.
"""
from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]


def main() -> int:
    node = shutil.which("node")
    if not node:
        print("PENPOT SYNC CHECK NOT RUN: node is unavailable")
        return 2
    result = subprocess.run(
        [node, "--test", "tests/penpot-sync.test.mjs"],
        cwd=REPO / "frontend/Qasedak.Web",
    )
    if result.returncode == 0:
        print("PENPOT SYNC CHECK PASSED")
    return result.returncode


if __name__ == "__main__":
    raise SystemExit(main())
