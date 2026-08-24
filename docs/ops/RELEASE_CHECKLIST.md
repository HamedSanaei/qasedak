# Qasedak v1 release checklist

Baseline frozen for the v1 production release. Each item records its evidence and status.
**Verified here** = executed in this repository/session with recorded output.
**External** = requires real-world infrastructure and is deliberately not claimed.

## 0. Scope

- Modules delivered: Identity, Instagram (webhook ingest + token vault), Conversations
  inbox, Automations, Contacts, Billing (provider-neutral), cross-module bridges,
  observability, hardening gates, audit trail.
- Blocked by decision: M09-002 payment provider integration; M08-001..005 Penpot-driven
  screens (Penpot MCP disconnected — retried at every milestone boundary).

## 1. Quality gates

| Gate | Command | Status |
|---|---|---|
| Architecture boundaries | `python scripts/check_architecture.py` | ✅ PASSED (35 projects, 6 business modules) |
| Code style | `dotnet format --verify-no-changes` | ✅ PASSED |
| Backend unit + PostgreSQL integration suites | `dotnet test backend/Qasedak.slnx` | ✅ all green at freeze (see STATUS.md) |
| API end-to-end suite | `dotnet test tests/Qasedak.Api.IntegrationTests` | ✅ 37/37 incl. security + load + audit gates |
| Frontend verify | `npm run verify` (inside `scripts/verify.py --full`) | ✅ via full verify |
| Docker images build | `docker build qasedak-api:verify / qasedak-web:verify` | ✅ via `scripts/verify.py --full` |
| Full toolchain | `python scripts/verify.py --full` → `FULL VERIFY PASSED` | ✅ re-run at release freeze |
| Mutation gate | `dotnet dotnet-stryker` (billing domain) | ✅ runs; score 75.73% (strings-excluded policy) |
| Backup/restore rehearsal | `python scripts/rehearse_backup_restore.py` | ✅ REHEARSAL PASSED |
| Deployment/rollback rehearsal | `python scripts/rehearse_deployment.py` | ✅ DEPLOYMENT REHEARSAL PASSED |
| Environment contract sync | `python scripts/check_environment_contract.py` | ✅ IN SYNC |

## 2. Artifacts

- **API image:** built from this tree during the deployment rehearsal. Digest recorded in
  `docs/ops/RELEASE_BASELINE.json` (`apiImageDigest`). Reproducible via
  `docker build -t qasedak-api:v1 ./backend`.
- **Web image:** `docker build -t qasedak-web:v1 ./frontend/Qasedak.Web` (built during full
  verify).
- **SBOM:** CycloneDX for `Qasedak.Api` at `docs/ops/sbom/bom.xml`.
- **Source provenance:** commit hash at freeze recorded in `RELEASE_BASELINE.json`
  (`sourceCommit`) plus Graphify evidence trail in `.agent-state/GRAPHIFY_EVIDENCE.md`.

## 3. External items deliberately NOT claimed

- DNS/TLS termination at a public reverse proxy.
- Public reachability of webhook endpoints from Meta.
- Managed PostgreSQL service behavior (backups/PITR at the provider).
- Real secret-store injection (Vault/KMS/cloud secret manager).
- Payment processing (M09-002 blocked on provider ADR).
- Penpot-synced UI surfaces (M08 blocked on MCP connection).

Each external item must be checked at first real deployment per
`PRODUCTION_ENVIRONMENT.md`; none are prerequisites for tagging v1.

## 4. Operational handoff

- Runbook: environment contract (`PRODUCTION_ENVIRONMENT.md`), probes wiring, migration +
  rollback procedure, backup/restore rehearsal commands.
- On-call notes: health endpoints, rate-limit classes and their budgets, audit trail
  schema (`audit.audit_entries`, append-only).
- Suggested release tag: `v1.0.0` after human review; agents do not tag or push.
