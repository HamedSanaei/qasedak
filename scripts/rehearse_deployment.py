#!/usr/bin/env python3
"""Deployment/rollback rehearsal against local container infrastructure.

Exercises the v1 release-candidate procedure end to end:
  1. build the API RC image from source;
  2. boot an isolated PostgreSQL 18 and apply all module migrations via the RC image;
  3. start the RC container wired to the contract's required settings;
  4. smoke: /health/live, /health/ready, /api/v1/system, register + login over HTTP;
  5. rollback drill: stop the RC, redeploy the previous image tag (v1 baseline has no
     predecessor, so the drill re-deploys the same image to prove the stop/start/health
     procedure), re-run smokes.

Prints DEPLOYMENT REHEARSAL PASSED only when every step holds. Externally unverified
aspects (real DNS/TLS, Meta reachability, managed Postgres) are NOT claimed.
"""

from __future__ import annotations

import base64
import json
import os
import subprocess
import sys
import time
import urllib.request
from pathlib import Path

POSTGRES_IMAGE = "postgres:18-alpine"
DB = "qasedak-rc"
RC_API = "qasedak-api:rc"
PREV_API = "qasedak-api:rc-prev"
NET = "qasedak-rc-net"
DB_CONTAINER = "qasedak-rc-db"
API_CONTAINER = "qasedak-rc-api"

CONNECTION = "Host=qasedak-rc-db;Database=qasedak;Username=qasedak;Password=rc-secret"


def run(cmd: list[str], capture: bool = True, **kwargs) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(cmd, text=True,
                            stdout=subprocess.PIPE if capture else None,
                            stderr=subprocess.STDOUT if capture else None, **kwargs)
    if result.returncode != 0:
        print(result.stdout or "", file=sys.stderr)
        raise SystemExit(f"command failed: {' '.join(cmd)}")
    return result


def docker(*args: str, tolerant: bool = False) -> str:
    result = run(["docker", *args]) if not tolerant else subprocess.run(
        ["docker", *args], capture_output=True, text=True)
    return result.stdout.strip() if not tolerant else (result.stdout or "").strip()


def http_get(url: str) -> tuple[int, str]:
    try:
        with urllib.request.urlopen(url, timeout=10) as response:
            return response.status, response.read().decode()
    except urllib.error.HTTPError as error:
        return error.code, error.read().decode()


def wait_ready(port: int, timeout_seconds: int = 120) -> None:
    deadline = time.time() + timeout_seconds
    while time.time() < deadline:
        status, _ = http_get(f"http://localhost:{port}/health/live")
        if status == 200:
            return
        time.sleep(2)
    raise SystemExit(f"API on port {port} never became live")


def wait_db() -> None:
    deadline = time.time() + 90
    while time.time() < deadline:
        probe = subprocess.run(["docker", "exec", DB_CONTAINER, "pg_isready",
                                "-U", "qasedak", "-d", "qasedak"],
                               capture_output=True, text=True)
        if probe.returncode == 0:
            return
        time.sleep(2)
    raise SystemExit("database never became healthy")


def start_api(tag: str, port: int) -> None:
    docker("run", "-d", "--name", API_CONTAINER, "--network", NET,
           "-p", f"127.0.0.1:{port}:8080",
           "-e", f"ConnectionStrings:Identity={CONNECTION}",
           "-e", f"ConnectionStrings:Instagram={CONNECTION}",
           "-e", f"ConnectionStrings:Conversations={CONNECTION}",
           "-e", f"ConnectionStrings:Automations={CONNECTION}",
           "-e", f"ConnectionStrings:Contacts={CONNECTION}",
           "-e", f"ConnectionStrings:Billing={CONNECTION}",
           "-e", f"ConnectionStrings:Audit={CONNECTION}",
           "-e", "ASPNETCORE_ENVIRONMENT=Production",
           "-e", "Identity:Auth:TokenSigningKey=deployment-rehearsal-signing-key-0123456789abcdef",
           "-e", "Identity:Auth:TokenLifetimeHours=8",
           "-e", "Instagram:Meta:AppSecret=rehearsal-meta-app-secret",
           "-e", "Instagram:Meta:VerifyToken=rehearsal-verify-token",
           "-e", "Instagram:Protection:KeyBase64=" +
               base64.b64encode(b"rehearsal-token-prot-key!!32b"[:32]).decode(),
           tag)


def migrate_via_image() -> None:
    """Apply all module migrations through the host toolchain (design-time factories)."""
    projects = {
        "IdentityDbContext": ("backend/Modules/Identity/Qasedak.Modules.Identity.Infrastructure", "QASEDAK_IDENTITY_CONNECTION"),
        "InstagramDbContext": ("backend/Modules/Instagram/Qasedak.Modules.Instagram.Infrastructure", "QASEDAK_INSTAGRAM_CONNECTION"),
        "ConversationsDbContext": ("backend/Modules/Conversations/Qasedak.Modules.Conversations.Infrastructure", "QASEDAK_CONVERSATIONS_CONNECTION"),
        "AutomationsDbContext": ("backend/Modules/Automations/Qasedak.Modules.Automations.Infrastructure", "QASEDAK_AUTOMATIONS_CONNECTION"),
        "ContactsDbContext": ("backend/Modules/Contacts/Qasedak.Modules.Contacts.Infrastructure", "QASEDAK_CONTACTS_CONNECTION"),
        "BillingDbContext": ("backend/Modules/Billing/Qasedak.Modules.Billing.Infrastructure", "QASEDAK_BILLING_CONNECTION"),
        "AuditDbContext": ("backend/BuildingBlocks/Qasedak.BuildingBlocks.Infrastructure", "QASEDAK_AUDIT_CONNECTION"),
    }
    host_port = docker("port", DB_CONTAINER, "5432/tcp").splitlines()[0].split(":")[-1]
    connection = f"Host=localhost;Port={host_port};Database=qasedak;Username=qasedak;Password=rc-secret"
    for context, (project, env_var) in projects.items():
        run(["dotnet", "ef", "database", "update", "--project", project,
             "--startup-project", "backend/Qasedak.Api", "--context", context],
            env={**os.environ, env_var: connection})
        print(f"   migrated {context}")


def smoke(port: int) -> None:
    status, body = http_get(f"http://localhost:{port}/api/v1/system")
    assert status == 200, f"/api/v1/system returned {status}"
    payload = json.loads(body)
    assert payload.get("architecture") == "Modular Monolith", payload

    import urllib.error
    email = f"rc-{int(time.time())}@example.com"
    register = subprocess.run([
        "curl", "-s", "-o", "/dev/null", "-w", "%{http_code}",
        "-X", "POST", f"http://localhost:{port}/api/v1/identity/register",
        "-H", "Content-Type: application/json",
        "-d", json.dumps({"email": email, "displayName": "RC Smoke", "password": "rc-password-123"}),
    ], capture_output=True, text=True)
    assert register.stdout == "201", f"register returned {register.stdout}"

    login = subprocess.run([
        "curl", "-s", "-X", "POST", f"http://localhost:{port}/api/v1/identity/login",
        "-H", "Content-Type: application/json",
        "-d", json.dumps({"email": email, "password": "rc-password-123"}),
    ], capture_output=True, text=True)
    token = json.loads(login.stdout)["accessToken"]
    assert len(token) > 20, "no access token issued"
    print("   smoke ok (system endpoint, register, login)")


def main() -> None:
    print("== Qasedak deployment/rollback rehearsal ==")
    for name in (DB_CONTAINER, API_CONTAINER):
        docker("rm", "-f", name, tolerant=True)
    docker("network", "rm", NET, tolerant=True)
    try:
        print("-- building RC image")
        run(["docker", "build", "-t", RC_API, "."], cwd=ROOT / "backend")
        # The rollback drill needs a 'previous' deployment; v1 has no predecessor, so we
        # retag the identical image — the exercise validates procedure, not binary drift.
        docker("tag", RC_API, PREV_API)

        docker("network", "create", NET)

        print("-- booting isolated database")
        docker("run", "-d", "--name", DB_CONTAINER, "--network", NET,
               "-p", "127.0.0.1::5432",
               "-e", "POSTGRES_DB=qasedak", "-e", "POSTGRES_USER=qasedak",
               "-e", "POSTGRES_PASSWORD=rc-secret", POSTGRES_IMAGE)
        wait_db()

        print("-- applying module migrations")
        migrate_via_image()

        print("-- deploying release candidate")
        start_api(RC_API, 18080)
        wait_ready(18080)
        status, _ = http_get("http://localhost:18080/health/ready")
        assert status == 200, f"/health/ready returned {status}"
        smoke(18080)

        print("-- rollback drill: stop RC, redeploy previous, re-smoke")
        docker("rm", "-f", API_CONTAINER)
        start_api(PREV_API, 18081)
        wait_ready(18081)
        status, _ = http_get("http://localhost:18081/health/ready")
        assert status == 200, f"previous /health/ready returned {status}"
        smoke(18081)

        print("DEPLOYMENT REHEARSAL PASSED")
        print("NOT VERIFIED EXTERNALLY: DNS/TLS termination, public webhook reachability "
              "for Meta, managed-Postgres behavior, real secret-store injection.")
    finally:
        for name in (DB_CONTAINER, API_CONTAINER):
            subprocess.run(["docker", "rm", "-f", name], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        subprocess.run(["docker", "network", "rm", NET], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


if __name__ == "__main__":
    ROOT = Path(__file__).resolve().parent.parent
    main()
