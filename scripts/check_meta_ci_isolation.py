#!/usr/bin/env python3
from __future__ import annotations

import re
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
CI = REPO / ".github" / "workflows" / "ci.yml"
REQUIRED_HOSTS = (
    "graph.instagram.com",
    "api.instagram.com",
    "graph.facebook.com",
    "www.instagram.com",
)

def main() -> int:
    text = CI.read_text(encoding="utf-8")
    errors: list[str] = []
    if "Deny live Meta endpoints" not in text:
        errors.append("CI must contain the live-Meta deny step")
    for host in REQUIRED_HOSTS:
        if host not in text:
            errors.append(f"CI live-Meta deny is missing {host}")
    if re.search(r"secrets\.[A-Z0-9_]*(?:META|INSTAGRAM)[A-Z0-9_]*(?:TOKEN|SECRET)", text, re.I):
        errors.append("CI must not consume Meta/Instagram production token secrets")
    if errors:
        print("META CI ISOLATION CHECK FAILED")
        for error in errors:
            print(f"- {error}")
        return 1
    print("META CI ISOLATION CHECK PASSED (official Meta hosts denied; no Meta token secret in CI)")
    return 0

if __name__ == "__main__":
    raise SystemExit(main())
