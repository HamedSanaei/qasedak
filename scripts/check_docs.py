#!/usr/bin/env python3
from __future__ import annotations

from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
DOCS = REPO / "docs"
ENGLISH = [
    "01-VISION.md", "02-SRS.md", "03-ANALYSIS.md", "04-ARCHITECTURE.md",
    "05-DESIGN.md", "06-DATABASE-DESIGN.md", "07-TEST-PLAN.md", "08-DEPLOYMENT-AND-USER-GUIDE.md",
]
PERSIAN = [
    "01-VISION.fa.html", "02-SRS.fa.html", "03-ANALYSIS.fa.html", "04-ARCHITECTURE.fa.html",
    "05-DESIGN.fa.html", "06-DATABASE-DESIGN.fa.html", "07-TEST-PLAN.fa.html", "08-DEPLOYMENT-AND-USER-GUIDE.fa.html",
]
ARCH_SECTIONS = [
    "Overview", "Goals", "System Context", "High-Level Architecture", "Modules", "Layer Responsibilities",
    "Dependency Rules", "Data Architecture", "Runtime Flows", "Security", "Deployment", "Non-Functional Requirements",
    "Architecture Constraints", "Repository Structure",
]


def main() -> int:
    errors: list[str] = []
    for name in ENGLISH:
        p = DOCS / name
        if not p.exists(): errors.append(f"missing English document: {name}"); continue
        if len(p.read_text(encoding="utf-8")) < 1200: errors.append(f"English document too thin: {name}")
    fa_dir = DOCS / "fa"
    actual_html = sorted(p.name for p in fa_dir.glob("*.html")) if fa_dir.exists() else []
    if actual_html != sorted(PERSIAN):
        errors.append(f"Persian HTML package must contain exactly the canonical 8 files; got {actual_html}")
    for name in PERSIAN:
        p = fa_dir / name
        if not p.exists(): errors.append(f"missing Persian document: {name}"); continue
        text = p.read_text(encoding="utf-8")
        for token in ['dir="rtl"', 'class="toc"', '@media print', 'Vazirmatn']:
            if token not in text: errors.append(f"{name} missing print/RTL token: {token}")
        if len(text) < 3500: errors.append(f"Persian HTML document too thin: {name}")
    arch = DOCS / "04-ARCHITECTURE.md"
    if arch.exists():
        text = arch.read_text(encoding="utf-8")
        for i, title in enumerate(ARCH_SECTIONS, 1):
            if f"## {i}. {title}" not in text:
                errors.append(f"Architecture document missing exact section: {i}. {title}")
    if errors:
        print("DOCUMENT CHECK FAILED")
        for e in errors: print(f"- {e}")
        return 1
    print("DOCUMENT CHECK PASSED (8 English Markdown + exactly 8 Persian RTL HTML documents)")
    return 0


if __name__ == "__main__": raise SystemExit(main())
