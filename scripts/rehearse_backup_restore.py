#!/usr/bin/env python3
"""PostgreSQL backup/restore/migration-replay rehearsal (isolated containers).

Boots two throwaway postgres:18-alpine containers, replays every module migration
against the source, dumps it with pg_dump, restores into the target and verifies the
restored schema matches. Prints REHEARSAL PASSED only when everything holds.

Usage:  python scripts/rehearse_backup_restore.py
"""

from __future__ import annotations

import os
import subprocess
import sys
import time

POSTGRES_IMAGE = "postgres:18-alpine"
SRC = "qasedak-rehearse-src"
DST = "qasedak-rehearse-dst"
DB = "qasedak"
USER = "qasedak"
PASSWORD = "rehearsal"

# (migration project, context, env var) — the API composition root is always the startup
# project because it references every migration assembly.
MODULES = [
    ("backend/Modules/Identity/Qasedak.Modules.Identity.Infrastructure",
     "IdentityDbContext", "QASEDAK_IDENTITY_CONNECTION"),
    ("backend/Modules/Instagram/Qasedak.Modules.Instagram.Infrastructure",
     "InstagramDbContext", "QASEDAK_INSTAGRAM_CONNECTION"),
    ("backend/Modules/Conversations/Qasedak.Modules.Conversations.Infrastructure",
     "ConversationsDbContext", "QASEDAK_CONVERSATIONS_CONNECTION"),
    ("backend/Modules/Automations/Qasedak.Modules.Automations.Infrastructure",
     "AutomationsDbContext", "QASEDAK_AUTOMATIONS_CONNECTION"),
    ("backend/Modules/Contacts/Qasedak.Modules.Contacts.Infrastructure",
     "ContactsDbContext", "QASEDAK_CONTACTS_CONNECTION"),
    ("backend/Modules/Billing/Qasedak.Modules.Billing.Infrastructure",
     "BillingDbContext", "QASEDAK_BILLING_CONNECTION"),
    ("backend/BuildingBlocks/Qasedak.BuildingBlocks.Infrastructure",
     "AuditDbContext", "QASEDAK_AUDIT_CONNECTION"),
    ("backend/BuildingBlocks/Qasedak.BuildingBlocks.Infrastructure",
     "ScheduledWorkDbContext", "QASEDAK_PLATFORM_CONNECTION"),
]

SCHEMAS = ["identity", "instagram", "conversations", "automations", "contacts", "billing", "audit", "platform"]


def run(cmd: list[str], env: dict[str, str] | None = None, capture: bool = True) -> subprocess.CompletedProcess[str]:
    merged = {**os.environ, **(env or {})}
    result = subprocess.run(cmd, env=merged, text=True,
                            stdout=subprocess.PIPE if capture else None,
                            stderr=subprocess.STDOUT if capture else None)
    if result.returncode != 0:
        print(result.stdout or "", file=sys.stderr)
        raise SystemExit(f"command failed: {' '.join(cmd)}")
    return result


def docker(*args: str, env: dict[str, str] | None = None) -> str:
    return run(["docker", *args], env=env).stdout.strip()


def wait_healthy(name: str) -> None:
    deadline = time.time() + 90
    while time.time() < deadline:
        # pg_isready exits non-zero until ready — poll tolerantly.
        probe = subprocess.run(["docker", "exec", name, "pg_isready", "-U", USER, "-d", DB],
                               text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        if probe.returncode == 0 and "accepting connections" in probe.stdout:
            return
        time.sleep(2)
    raise SystemExit(f"container {name} never became healthy")


def psql(name: str, sql: str) -> str:
    return docker("exec", name, "psql", "-U", USER, "-d", DB, "-tAc", sql)


def psql_stdin(name: str, sql: str) -> str:
    """Run SQL via stdin — immune to Windows arg-quoting mangling of double quotes."""
    proc = subprocess.run(["docker", "exec", "-i", name, "psql", "-U", USER, "-d", DB, "-tA"],
                          input=sql + ";", text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    if proc.returncode != 0:
        print(proc.stdout or "", file=sys.stderr)
        raise SystemExit(f"psql failed on {name}: {sql}")
    return proc.stdout.strip()


def main() -> None:
    print("== Qasedak backup/restore rehearsal ==")
    for name in (SRC, DST):
        docker("rm", "-f", name)
    try:
        for name in (SRC, DST):
            docker("run", "-d", "--name", name, "-e", f"POSTGRES_DB={DB}",
                   "-e", f"POSTGRES_USER={USER}", "-e", f"POSTGRES_PASSWORD={PASSWORD}",
                   "-p", "0.0.0.0::5432",
                   POSTGRES_IMAGE)
            wait_healthy(name)

        host_port = docker("port", SRC, "5432/tcp").splitlines()[0].split(":")[-1]
        connection = f"Host=localhost;Port={host_port};Database={DB};Username={USER};Password={PASSWORD}"

        print("-- replaying module migrations against source")
        for project, context, env_var in MODULES:
            run(["dotnet", "ef", "database", "update", "--project", project,
                 "--startup-project", "backend/Qasedak.Api", "--context", context],
                env={env_var: connection})
            print(f"   migrated {context}")

        # Seed a row through SQL so restore verification covers data, not just schema.
        psql(SRC, "INSERT INTO audit.audit_entries "
                  "(\"AuditId\", \"WorkspaceId\", \"ActorUserId\", \"Action\", \"TargetType\", \"TargetId\", \"AtUtc\", \"DetailsJson\") "
                  "VALUES ('11111111-2222-3333-4444-555555555555', NULL, NULL, 'rehearsal.seed', NULL, NULL, now(), NULL)")
        src_rows = int(psql(SRC, "SELECT count(*) FROM audit.audit_entries"))

        print("-- dumping and restoring")
        dump = docker("exec", SRC, "pg_dump", "-U", USER, "-d", DB)
        proc = subprocess.run(["docker", "exec", "-i", DST, "psql", "-U", USER, "-d", DB, "-v", "ON_ERROR_STOP=1"],
                              input=dump, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        if proc.returncode != 0:
            print(proc.stdout, file=sys.stderr)
            raise SystemExit("restore failed")

        dst_rows = int(psql(DST, "SELECT count(*) FROM audit.audit_entries"))
        assert dst_rows == src_rows == 1, f"row counts diverged: src={src_rows} dst={dst_rows}"

        for schema in SCHEMAS:
            src_tables = set(psql(SRC, f"SELECT tablename FROM pg_tables WHERE schemaname='{schema}'").splitlines())
            dst_tables = set(psql(DST, f"SELECT tablename FROM pg_tables WHERE schemaname='{schema}'").splitlines())
            assert src_tables and src_tables == dst_tables, (
                f"schema {schema} mismatch: src={sorted(src_tables)} dst={sorted(dst_tables)}")
            print(f"   verified schema '{schema}' ({len(src_tables)} tables)")

        def history_entries(name: str) -> list[str]:
            out = psql_stdin(name, 'SELECT "MigrationId" FROM identity."__EFMigrationsHistory" ORDER BY 1')
            return [line for line in out.splitlines() if line]

        migrations_src = history_entries(SRC)
        migrations_dst = history_entries(DST)
        assert migrations_src == migrations_dst and migrations_dst, "migration history did not survive restore"
        print(f"   migration history intact across restore ({len(migrations_dst)} identity entries)")

        print("REHEARSAL PASSED")
    finally:
        for name in (SRC, DST):
            subprocess.run(["docker", "rm", "-f", name], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


if __name__ == "__main__":
    main()
