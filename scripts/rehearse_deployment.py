#!/usr/bin/env python3
"""Local production deployment rehearsal using Docker-only runtime behavior.

This rehearsal intentionally avoids dotnet-ef on the host. It builds the same API/Web
runtime images used by deployment, runs the API image's one-shot --migrate command twice,
then verifies the application and a binary rollback against a fresh isolated PostgreSQL.
It does not claim DNS/TLS, Meta reachability, managed-Postgres, or real secret-store
behavior.
"""

from __future__ import annotations

import base64
import json
import os
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

POSTGRES_IMAGE = "postgres:18-alpine"
API_IMAGE = "qasedak-api:rehearsal"
PREVIOUS_API_IMAGE = "qasedak-api:rehearsal-previous"
WEB_IMAGE = "qasedak-web:rehearsal"
NET = "qasedak-rehearsal-net"
DB_CONTAINER = "qasedak-rehearsal-db"
API_CONTAINER = "qasedak-rehearsal-api"
WEB_CONTAINER = "qasedak-rehearsal-web"
ROLLBACK_API_CONTAINER = "qasedak-rehearsal-rollback-api"
DB = "qasedak"
USER = "qasedak"
PASSWORD = "rehearsal-db-password"
SIGNING_KEY = "deployment-rehearsal-signing-key-0123456789abcdef"
PROTECTION_KEY = base64.b64encode(b"0123456789abcdef0123456789abcdef").decode()
CONNECTION = f"Host={DB_CONTAINER};Port=5432;Database={DB};Username={USER};Password={PASSWORD}"
SCHEMAS = ["identity", "instagram", "conversations", "automations", "contacts", "billing", "audit", "platform"]
ROOT = Path(__file__).resolve().parent.parent


def command(args: list[str], *, cwd: Path = ROOT, check: bool = True, quiet: bool = False) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(
        args,
        cwd=cwd,
        text=True,
        stdout=subprocess.PIPE if quiet else None,
        stderr=subprocess.STDOUT if quiet else None,
    )
    if check and result.returncode != 0:
        if result.stdout:
            print(result.stdout, file=sys.stderr)
        raise SystemExit(f"command failed ({result.returncode}): {' '.join(args)}")
    return result


def docker(*args: str, check: bool = True, quiet: bool = False) -> subprocess.CompletedProcess[str]:
    return command(["docker", *args], check=check, quiet=quiet)


def remove_container(name: str) -> None:
    docker("rm", "-f", name, check=False, quiet=True)


def remove_network() -> None:
    docker("network", "rm", NET, check=False, quiet=True)


def wait_db(timeout_seconds: int = 120) -> None:
    deadline = time.time() + timeout_seconds
    while time.time() < deadline:
        probe = docker("exec", DB_CONTAINER, "pg_isready", "-U", USER, "-d", DB, check=False, quiet=True)
        if probe.returncode == 0:
            return
        time.sleep(2)
    raise SystemExit("database never became healthy")


def wait_http(url: str, timeout_seconds: int = 120) -> None:
    deadline = time.time() + timeout_seconds
    while time.time() < deadline:
        try:
            with urllib.request.urlopen(url, timeout=5) as response:
                if 200 <= response.status < 300:
                    return
        except (urllib.error.URLError, urllib.error.HTTPError, OSError):
            pass
        time.sleep(2)
    raise SystemExit(f"timed out waiting for {url}")


def api_env() -> list[str]:
    values = {
        "ConnectionStrings:Identity": CONNECTION,
        "ConnectionStrings:Instagram": CONNECTION,
        "ConnectionStrings:Conversations": CONNECTION,
        "ConnectionStrings:Automations": CONNECTION,
        "ConnectionStrings:Contacts": CONNECTION,
        "ConnectionStrings:Billing": CONNECTION,
        "ConnectionStrings:Audit": CONNECTION,
        "ConnectionStrings:Platform": CONNECTION,
        "ASPNETCORE_ENVIRONMENT": "Production",
        "Identity:Auth:TokenSigningKey": SIGNING_KEY,
        "Identity:Auth:TokenLifetimeHours": "8",
        "Instagram:Meta:AppSecret": "rehearsal-meta-app-secret",
        "Instagram:Meta:VerifyToken": "rehearsal-verify-token",
        "Instagram:Protection:KeyBase64": PROTECTION_KEY,
        "Cors:AllowedOrigins:0": "http://127.0.0.1:18082",
    }
    result: list[str] = []
    for key, value in values.items():
        result.extend(["-e", f"{key}={value}"])
    return result


def run_migrations() -> None:
    print("-- first image migration")
    result = docker("run", "--rm", "--network", NET, *api_env(), API_IMAGE, "--migrate", check=False, quiet=True)
    if result.stdout:
        for line in result.stdout.splitlines():
            if "Qasedak.Migrations" in line or "schema" in line.lower() or "migration" in line.lower():
                print(line)
    if result.returncode != 0:
        print(result.stdout or "", file=sys.stderr)
        raise SystemExit(f"first image migration failed with exit {result.returncode}")
    print("first migration exit: 0")

    print("-- second image migration (idempotency)")
    result = docker("run", "--rm", "--network", NET, *api_env(), API_IMAGE, "--migrate", check=False, quiet=True)
    if result.stdout:
        for line in result.stdout.splitlines():
            if "already up to date" in line.lower() or "migration run complete" in line.lower():
                print(line)
    if result.returncode != 0:
        print(result.stdout or "", file=sys.stderr)
        raise SystemExit(f"second image migration failed with exit {result.returncode}")
    print("second migration exit: 0")


def verify_schemas() -> None:
    query = "SELECT schema_name FROM information_schema.schemata WHERE schema_name IN (" + ",".join(f"'{s}'" for s in SCHEMAS) + ") ORDER BY schema_name;"
    result = docker("exec", DB_CONTAINER, "psql", "-U", USER, "-d", DB, "-Atc", query, check=True, quiet=True)
    actual = [line.strip() for line in (result.stdout or "").splitlines() if line.strip()]
    if actual != sorted(SCHEMAS):
        raise SystemExit(f"schema verification failed: {actual}")
    print("eight schemas: " + ", ".join(actual))


def start_api(name: str, image: str, port: int) -> None:
    docker("run", "-d", "--name", name, "--network", NET, "--network-alias", "api",
           "-p", f"127.0.0.1:{port}:8080", *api_env(), image)
    wait_http(f"http://127.0.0.1:{port}/health/live")
    wait_http(f"http://127.0.0.1:{port}/health/ready")
    with urllib.request.urlopen(f"http://127.0.0.1:{port}/api/v1/system", timeout=10) as response:
        payload = json.loads(response.read())
    if payload.get("architecture") != "Modular Monolith":
        raise SystemExit(f"unexpected system response: {payload}")


def start_web(port: int) -> None:
    docker("run", "-d", "--name", WEB_CONTAINER, "--network", NET,
           "-p", f"127.0.0.1:{port}:3000", WEB_IMAGE)
    wait_http(f"http://127.0.0.1:{port}/")


def wait_proxy(port: int, path: str, expected_status: int = 200, timeout_seconds: int = 120) -> str:
    deadline = time.time() + timeout_seconds
    last_status = None
    last_body = ""
    while time.time() < deadline:
        last_status, last_body = public_api_request(port, "GET", path)
        if last_status == expected_status:
            return last_body
        time.sleep(2)
    logs = docker("logs", WEB_CONTAINER, check=False, quiet=True)
    if logs.stdout:
        print(logs.stdout, file=sys.stderr)
    raise SystemExit(f"same-origin proxy {path} returned {last_status}: {last_body[:200]}")


def public_api_request(port: int, method: str, path: str, body: dict[str, str] | None = None) -> tuple[int, str]:
    request = urllib.request.Request(
        f"http://127.0.0.1:{port}{path}",
        method=method,
        data=json.dumps(body).encode() if body is not None else None,
        headers={"Content-Type": "application/json"} if body is not None else {},
    )
    try:
        with urllib.request.urlopen(request, timeout=10) as response:
            return response.status, response.read().decode()
    except urllib.error.HTTPError as error:
        return error.code, error.read().decode()


def smoke_auth(port: int, suffix: str) -> tuple[str, str]:
    email = f"rehearsal-{suffix}-{int(time.time())}@example.com"
    password = "StrongRehearsal123!"
    register = subprocess.run([
        "curl", "-s", "-o", os.devnull, "-w", "%{http_code}", "-X", "POST",
        f"http://127.0.0.1:{port}/api/v1/identity/register",
        "-H", "Content-Type: application/json",
        "-d", json.dumps({"email": email, "displayName": "Rehearsal", "password": password}),
    ], text=True, capture_output=True, check=False)
    if register.stdout != "201":
        raise SystemExit(f"register returned {register.stdout}")
    login = subprocess.run([
        "curl", "-s", "-X", "POST", f"http://127.0.0.1:{port}/api/v1/identity/login",
        "-H", "Content-Type: application/json",
        "-d", json.dumps({"email": email, "password": password}),
    ], text=True, capture_output=True, check=False)
    if login.returncode != 0 or "accessToken" not in login.stdout:
        raise SystemExit("login did not return an access token")
    print("register: 201; login: access token issued")
    return email, password


def login_existing(port: int, email: str, password: str) -> None:
    login = subprocess.run([
        "curl", "-s", "-X", "POST", f"http://127.0.0.1:{port}/api/v1/identity/login",
        "-H", "Content-Type: application/json",
        "-d", json.dumps({"email": email, "password": password}),
    ], text=True, capture_output=True, check=False)
    if login.returncode != 0 or "accessToken" not in login.stdout:
        raise SystemExit("persisted user could not log in after API recreation")
    print("persisted user login after API recreation: PASS")


def main() -> None:
    print("== Qasedak Docker deployment rehearsal ==")
    for name in (DB_CONTAINER, API_CONTAINER, WEB_CONTAINER, ROLLBACK_API_CONTAINER):
        remove_container(name)
    remove_network()
    try:
        print("-- build release runtime images")
        command(["docker", "build", "-t", API_IMAGE, "."], cwd=ROOT / "backend")
        command(["docker", "build", "-t", WEB_IMAGE, "."], cwd=ROOT / "frontend" / "Qasedak.Web")
        docker("tag", API_IMAGE, PREVIOUS_API_IMAGE)
        docker("network", "create", NET)

        print("-- fresh PostgreSQL")
        docker("run", "-d", "--name", DB_CONTAINER, "--network", NET,
               "-e", f"POSTGRES_DB={DB}", "-e", f"POSTGRES_USER={USER}",
               "-e", f"POSTGRES_PASSWORD={PASSWORD}", POSTGRES_IMAGE)
        wait_db()
        run_migrations()
        verify_schemas()

        print("-- release candidate API/Web")
        start_api(API_CONTAINER, API_IMAGE, 18080)
        # The standalone image resolves rewrites from next.config.ts at build time.
        # Build the image with the isolated Docker service hostname, matching production.
        start_web(18082)
        web_status, _ = public_api_request(18082, "GET", "/")
        if web_status < 200 or web_status >= 400:
            raise SystemExit(f"web-facing root returned {web_status}")
        system_body = wait_proxy(18082, "/api/v1/system")
        if json.loads(system_body).get("architecture") != "Modular Monolith":
            raise SystemExit("same-origin system proxy returned an unexpected payload")
        initial_email, initial_password = smoke_auth(18080, "initial")
        public_email = f"rehearsal-public-{int(time.time())}@example.com"
        public_password = "StrongRehearsal123!"
        register_status, _ = public_api_request(18082, "POST", "/api/v1/identity/register", {
            "email": public_email, "displayName": "Public rehearsal", "password": public_password,
        })
        if register_status != 201:
            raise SystemExit(f"same-origin register returned {register_status}")
        login_status, login_body = public_api_request(18082, "POST", "/api/v1/identity/login", {
            "email": public_email, "password": public_password,
        })
        if login_status != 200 or "accessToken" not in login_body:
            raise SystemExit(f"same-origin login returned {login_status}")
        print("health/live: 200; health/ready: 200; same-origin system: 200; same-origin register: 201; same-origin login: 200; web: 200")

        print("-- application container recreation / data persistence")
        remove_container(API_CONTAINER)
        start_api(API_CONTAINER, API_IMAGE, 18080)
        login_existing(18080, initial_email, initial_password)
        print("PostgreSQL data survived API recreation")

        print("-- binary rollback rehearsal")
        remove_container(API_CONTAINER)
        start_api(ROLLBACK_API_CONTAINER, PREVIOUS_API_IMAGE, 18081)
        with urllib.request.urlopen("http://127.0.0.1:18081/api/v1/system", timeout=10) as response:
            if response.status != 200:
                raise SystemExit("rollback system smoke failed")
        print("rollback health/smoke: PASS")
        print("DEPLOYMENT REHEARSAL PASSED")
        print("NOT VERIFIED: public DNS/TLS, Meta reachability, managed PostgreSQL, real secret store")
    finally:
        for name in (DB_CONTAINER, API_CONTAINER, WEB_CONTAINER, ROLLBACK_API_CONTAINER):
            remove_container(name)
        remove_network()


if __name__ == "__main__":
    main()
