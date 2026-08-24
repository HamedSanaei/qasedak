# Current handoff

## Where we are

**M09-002 executable scope is complete (2026-08-24), and the final Qasedak Penpot
designs are reconciled into the app.** All roadmap tasks are DONE; M09-002 is recorded
DONE-PARTIAL (Zarinpal production-capable per the current official v4 REST docs; Bank
Behpardakht Mellat live transport is externally blocked pending the verified current official merchant technical
documents). Nothing is committed — working tree only, per contract.

## What this run delivered

### Payments (backend, M09-002)
- `PaymentAttempt` aggregate (Pending→Verified|Failed) with xmin optimistic concurrency
  and a unique filtered Authority index (anti-replay). Verified payments extend the
  entitlement exactly once under concurrent/duplicate callbacks; callback query values
  alone can never activate anything.
- Provider-neutral `IPaymentGateway` (Application); Infrastructure owns protocols:
  `ZarinpalPaymentGateway` = CURRENT official v4 REST (request/verify JSON, 100/101,
  StartPay redirect) over typed HttpClient; typed options; server-side secrets only;
  merchant id/secrets/raw payloads/card PAN never logged. Per the same-day provider decision (ADR-009) Bank Melli/SADAD was cancelled and replaced by **Behpardakht Mellat**: `BehpardakhtMellatPaymentGateway` (`providerId="mellat"`) =
  fail-closed boundary naming exactly which CURRENT official Behpardakht documents are
  required before live transport can exist.
- Endpoints: `GET /api/v1/billing/plans`, workspace `subscription` / `checkout` (202 +
  server-owned redirect URL) / `payments/{attemptId}` / `payments` history; public
  provider callback redirects to `/dashboard/billing/result?state=…&attempt=…`.
- Migration `AddPaymentsAndPlanPrices` (+`Plan.AmountIrr`, canonical IRR per ADR-008);
  env contracts in `.env.example`, docker-compose passthroughs, deployment guide §6;
  **ADR-008 accepted**.
- Tests: Billing unit 60/60; Billing integration (Testcontainers) 9/9 incl. concurrent
  verify exactly-once; full Api.IntegrationTests 46/46 incl. 9 billing e2e.

### Design reconciliation (frontend, this run)
- Codex finalized four new `Qasedak ·` pages in the canonical file; every relevant
  board was live-inspected via MCP (no screenshots, no invented values). Extracted
  contract: `docs/design/sync/2026-08-24-qasedak-final-designs.md`; sync record:
  `docs/design/sync/2026-08-24-qasedak-final-sync-record.md`.
- `identity.auth`: **draft → approved** on `Qasedak · Identity & Workspace`
  (Login/Register Desktop+Mobile + states board). Auth screens visually reconciled with
  email+password behavior/validation/tests untouched. Old GetCode OTP boards are now
  non-authoritative reference only.
- NEW `inbox.conversations`: **approved** on `Qasedak · Inbox & Conversations` — removes
  the historical M08-004 "no design exists" blocker (historical evidence preserved).
  Inbox visually reconciled; search renders DISABLED BY DESIGN until a backend query
  capability ships.
- NEW `billing.payment`: **approved** across the five `Qasedak · Billing & Payments`
  boards. New UI: `/dashboard/billing` (plans + subscription summary),
  `/dashboard/billing/checkout?plan=…` (provider radios زرین‌پال/به‌پرداخت ملت — Penpot labels updated in-file per ADR-009; Mellat disabled until its
  verified contract lands), `/dashboard/billing/result?state=…&attempt=…` (bounded
  polling of the server status endpoint; callback hints never claim success alone).
  Amounts render exactly as received from the API (IRR grouping + ریال, no conversion).
- Manifest updated + validator green (6/6): `penpot-sync.json`; screen roll-up:
  `docs/design/SCREEN-INVENTORY.md`. New tests: `tests/billing.test.mjs`.

## Verification status

- Frontend: `npm run verify` green (lint max-warnings 0, tsc clean, node --test incl.
  new billing suite, production build prerenders `/dashboard/billing*` routes).
- Backend: solution builds Release clean; billing unit/integration suites green as
  listed above; `dotnet format` to be confirmed by the final `verify.py --full` pass.
- Gates: `validate_penpot_sync.py` PASSED, `check_architecture.py` PASSED
  (35 projects / 6 modules). Graphify evidence recorded for M09-002 (healthy; refresh
  ran `--code-only` because no LLM API key exists on this machine for doc semantic
  extraction).

## Next actions for a human

1. **Provide the CURRENT official Behpardakht Mellat merchant technical documents** (service endpoints/WSDL,
   signing/encryption algorithm spec, terminal/merchant credential contract, callback
   response-code table, callback field schema) → lifts the Mellat boundary to a real adapter and makes M09-002 fully
   complete.
2. Optional: run a staging Zarinpal smoke test with real merchant credentials (never in
   CI).

## Next task for an agent

None actionable in TASKS.md — every task is DONE or DONE-PARTIAL with the residual
explicitly owned by the human decision above. Do not commit/push/tag unless explicitly
asked; suggested commits are recorded per task in TASKS.md.
