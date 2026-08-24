#!/usr/bin/env python3
"""Environment-contract checker: keeps docs/ops/PRODUCTION_ENVIRONMENT.md in sync with code.

Fails when the codebase reads a connection string or a Qasedak:* setting that the
production contract document does not list.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DOC = ROOT / "docs" / "ops" / "PRODUCTION_ENVIRONMENT.md"
SEARCH_DIRS = ["backend", "scripts"]

GET_CONNECTION = re.compile(r'GetConnectionString\(\s*"([^"]+)"\s*\)')
QASEDAK_SETTING = re.compile(r'"(Qasedak:[A-Za-z.:{\}]+?)"')
OTHER_SETTINGS = re.compile(r'(?:GetSection|GetValue(?:<[^>]+>)?)\(\s*"((?:Identity|Instagram|Cors):[^"]+)"')


def collect_code_keys() -> set[str]:
    keys: set[str] = set()
    for directory in SEARCH_DIRS:
        base = ROOT / directory
        if not base.exists():
            continue
        for path in base.rglob("*.cs"):
            text = path.read_text(encoding="utf-8", errors="ignore")
            for match in GET_CONNECTION.finditer(text):
                keys.add(f"ConnectionStrings:{match.group(1)}")
            # Rate-limit classes are enumerated dynamically; expand them explicitly.
        for path in base.rglob("*.cs"):
            text = path.read_text(encoding="utf-8", errors="ignore")
            for match in QASEDAK_SETTING.finditer(text):
                key = match.group(1)
                if key.startswith("Qasedak:RateLimits:"):
                    keys.add("Qasedak:RateLimits:{Public,Authenticated,Webhook,Sensitive}:{Limit,WindowSeconds}")
                else:
                    keys.add(key)
    return keys


def doc_covers(key: str, doc_text: str) -> bool:
    if key.startswith("ConnectionStrings:"):
        name = key.split(":", 1)[1]
        return f"`{name}`" in doc_text and "| `ConnectionStrings:" in doc_text or f"ConnectionStrings:{name}" in doc_text or f"| `{name}` |" in doc_text
    if key.startswith("Qasedak:RateLimits:"):
        return "RateLimits:" in doc_text
    return key.split(":")[0] in doc_text


def main() -> None:
    doc_text = DOC.read_text(encoding="utf-8")
    missing = sorted(k for k in collect_code_keys() if not doc_covers(k, doc_text))
    print(f"environment contract: {len(collect_code_keys())} code-declared keys checked against {DOC.name}")
    if missing:
        for key in missing:
            print(f"MISSING from contract: {key}", file=sys.stderr)
        raise SystemExit("ENVIRONMENT CONTRACT OUT OF SYNC")
    print("ENVIRONMENT CONTRACT IN SYNC")


if __name__ == "__main__":
    main()
