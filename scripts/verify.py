#!/usr/bin/env python3
from __future__ import annotations

import argparse, shutil, subprocess, sys
from pathlib import Path

REPO=Path(__file__).resolve().parents[1]
# Windows: bare "npm" is npm.cmd and CreateProcess cannot resolve it by PATHEXT.
NPM=shutil.which("npm") or "npm"

def run(cmd:list[str],cwd:Path=REPO)->None:
    print("+", " ".join(cmd))
    subprocess.run(cmd,cwd=cwd,check=True)


def main()->int:
    ap=argparse.ArgumentParser();ap.add_argument("--full",action="store_true");args=ap.parse_args()
    for script in ["check_architecture.py","check_docs.py","check_state.py"]:
        run([sys.executable,str(REPO/"scripts"/script)])
    run(["node","--test","tests/repository-contract.test.mjs"],REPO/"frontend/Qasedak.Web")
    if not args.full:
        print("STATIC VERIFY PASSED. Use --full on a workstation/CI with .NET, dependencies and Docker.")
        return 0
    missing=[tool for tool in ["dotnet","npm","docker"] if not shutil.which(tool)]
    if missing:
        print("FULL VERIFY NOT RUN: missing required tools: "+", ".join(missing))
        return 2
    run(["dotnet","restore","Qasedak.slnx"],REPO/"backend")
    run(["dotnet","build","Qasedak.slnx","-c","Release","--no-restore"],REPO/"backend")
    run(["dotnet","format","Qasedak.slnx","--verify-no-changes","--no-restore"],REPO/"backend")
    run(["dotnet","test","Qasedak.slnx","-c","Release","--no-build"],REPO/"backend")
    run([NPM,"install","--no-audit","--no-fund"],REPO/"frontend/Qasedak.Web")
    run([NPM,"run","verify"],REPO/"frontend/Qasedak.Web")
    run(["docker","build","-t","qasedak-api:verify","."],REPO/"backend")
    run(["docker","build","-t","qasedak-web:verify","."],REPO/"frontend/Qasedak.Web")
    print("FULL VERIFY PASSED")
    return 0

if __name__ == "__main__":raise SystemExit(main())
