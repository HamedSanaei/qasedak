#!/usr/bin/env python3
from __future__ import annotations

import re
import sys
from pathlib import Path
import xml.etree.ElementTree as ET

REPO = Path(__file__).resolve().parents[1]
BACKEND = REPO / "backend"


def project_kind(path: Path) -> tuple[str, str | None]:
    s = path.as_posix()
    name = path.stem
    if "/Qasedak.Api/" in s:
        return "api", None
    if "/BuildingBlocks/" in s:
        if ".Domain" in name:
            return "bb-domain", None
        if ".Application" in name:
            return "bb-application", None
        if ".Infrastructure" in name:
            return "bb-infrastructure", None
    m = re.search(r"/Modules/([^/]+)/Qasedak\.Modules\.([^.]+)\.(Domain|Application|Infrastructure)/", s)
    if m:
        folder, project_module, layer = m.groups()
        if folder != project_module:
            raise AssertionError(f"module folder/project mismatch: {path}")
        return layer.lower(), folder
    if "/tests/" in s:
        return "test", None
    return "other", None


def resolve_reference(project: Path, include: str) -> Path:
    return (project.parent / include.replace("\\", "/")).resolve()


def main() -> int:
    projects = {p.resolve(): p for p in BACKEND.rglob("*.csproj")}
    errors: list[str] = []
    business_modules: set[str] = set()

    for p in projects:
        kind, module = project_kind(p)
        if module:
            business_modules.add(module)
        tree = ET.parse(p)
        for node in tree.findall(".//ProjectReference"):
            include = node.attrib.get("Include", "")
            target = resolve_reference(p, include)
            if target not in projects:
                errors.append(f"{p.relative_to(REPO)} references missing project {include}")
                continue
            target_kind, target_module = project_kind(target)

            # Cross-business-module project references are forbidden.
            if module and target_module and module != target_module:
                errors.append(f"cross-module reference forbidden: {p.name} -> {target.name}")

            if kind == "domain" and target_kind not in {"bb-domain"}:
                errors.append(f"Domain may only reference BuildingBlocks.Domain: {p.name} -> {target.name}")
            elif kind == "application":
                allowed = {"domain", "bb-domain", "bb-application"}
                if target_kind not in allowed:
                    errors.append(f"Application dependency points outward: {p.name} -> {target.name}")
            elif kind == "infrastructure":
                allowed = {"application", "domain", "bb-domain", "bb-application", "bb-infrastructure"}
                if target_kind not in allowed:
                    errors.append(f"Infrastructure invalid reference: {p.name} -> {target.name}")
            elif kind == "bb-domain":
                errors.append(f"BuildingBlocks.Domain must be dependency-free: {p.name} -> {target.name}")
            elif kind == "bb-application" and target_kind != "bb-domain":
                errors.append(f"BuildingBlocks.Application may only reference BuildingBlocks.Domain: {p.name} -> {target.name}")
            elif kind == "bb-infrastructure" and target_kind not in {"bb-domain", "bb-application"}:
                errors.append(f"BuildingBlocks.Infrastructure invalid reference: {p.name} -> {target.name}")

    expected = {"Identity", "Instagram", "Automations", "Conversations", "Contacts", "Billing"}
    if business_modules != expected:
        errors.append(f"business module set changed without architecture update: {sorted(business_modules)}")

    # Frontend/backend source isolation.
    frontend = REPO / "frontend" / "Qasedak.Web"
    for p in frontend.rglob("*"):
        if not p.is_file() or "node_modules" in p.parts or ".next" in p.parts:
            continue
        try:
            text = p.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            continue
        if "ProjectReference" in text or "Qasedak.Modules." in text:
            errors.append(f"frontend leaks backend project dependency: {p.relative_to(REPO)}")

    if errors:
        print("ARCHITECTURE CHECK FAILED")
        for e in errors:
            print(f"- {e}")
        return 1
    print(f"ARCHITECTURE CHECK PASSED ({len(projects)} projects, {len(business_modules)} business modules)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
