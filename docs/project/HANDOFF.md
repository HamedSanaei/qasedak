# Current handoff

## Where we are

All roadmap milestones are closed as of this handoff: M00–M07 fully delivered; **M09 and
M10 complete** (M09-002 BLOCKED); **M11 release baseline prepared** (M11-001..003 DONE);
**M08-001..005 remain BLOCKED**, now as `design-source-verification-pending-human-decision`
(Penpot MCP is reachable again — verified live 2026-08-24). The repository is a v1 production
baseline: `docs/ops/RELEASE_CHECKLIST.md` + `docs/ops/RELEASE_BASELINE.json` +
`docs/ops/sbom/bom.xml` record the freeze.

## Design-source verification (2026-08-24, M08 gate)

- Connected file verified by stable id `c269caa0-e456-818c-8008-85a77340be64`
  (display name `New File 1`; never match this file by name alone) via live
  `penpot.currentFile.id`; 24 pages enumerated with stable ids and persisted in
  `frontend/Qasedak.Web/design/penpot-sync.json` (validator 6/6 green).
- Resume brief expected ~8 pages and a page `Directam Landing` holding the earlier
  13-section landing. Reality: no such page exists; a board
  `Directam Landing — Desktop` (`f6b8d46f-5deb-801d-8008-85ab43d94e44`) sits on
  `Page 1` (`c269caa0-e456-818c-8008-85a77340be65`) with a different internal
  structure (flat layout, different section naming). Nothing was modified or
  recreated in Penpot.
- Full inspection record: `docs/design/sync/M08-001-design-source-verification.md`.

## Completed since last handoff

- **M09 Billing foundation:** provider-neutral `Plan`/`Subscription` domain (fail-closed
  entitlements, period history), repositories, `billing` schema + migration, 6th+7th
  fixture contexts. `EntitlementGate` enforces server-side limits; automation activation
  flows through `IAutomationActivationPolicy` (permissive default overridden by
  composition-root `BillingActivationPolicyAdapter`). M09-002 BLOCKED: needs a human ADR
  selecting the payment provider.
- **M10 reliability:** correlation middleware (`X-Correlation-Id` on every response/log),
  risk-class rate limiting (public/auth/webhook/sensitive budgets, 429+Retry-After),
  append-only audit trail (`audit.audit_entries`; login success/failure with email
  fingerprints only, subscription starts, automation activations/denials) bound via
  `ConnectionStrings:Audit`, PostgreSQL backup/restore/migration-replay rehearsal,
  mutation gate (Stryker on billing rules, 75.73% after boundary hardening),
  security/load gates. Security gate found and FIXED a real gap: workspace endpoints now
  enforce membership uniformly (`workspace-member` policy → 403 for non-members).
- **M11 baseline:** environment contract doc + sync checker script, deployment/rollback
  rehearsal (RC image build → migrate → deploy → smoke → rollback drill, PASSED, honest
  externals listed), CycloneDX SBOM, release checklist, release baseline JSON.

## Verification status at freeze

`python scripts/verify.py --full` was green earlier in the run; backend suites all green
at freeze (API e2e 37/37 incl. security/load/audit/correlation/rate-limit gates;
Billing/Automations/Contacts/Identity/BuildingBlocks unit + integration suites pass).
Re-run `verify.py --full` before any release action.

## Next actions for a human

1. **Decision required — payment provider (unblocks M09-002):** choose the provider and
   record an ADR (legal fit, webhook reachability, pricing model).
2. **Design-source decision (unblocks M08-001..005):** Penpot MCP is connected and the
   file is verified by stable id (see `docs/design/sync/M08-001-design-source-verification.md`).
   Confirm which source is canonical for M08: (a) the current live file as-is —
   `Directam Landing — Desktop` board on `Page 1` becomes the landing mapping target, or
   (b) restore/recreate the 13-section landing page first. Then agents resume M08-001.
3. Review `docs/ops/RELEASE_CHECKLIST.md`; tag `v1.0.0` if satisfied (agents never
   commit/push/tag).

## Next task for an agent

None unblocked in M07–M11. Once the human confirms the canonical design source (human
decision 2): run preflight for M08-001, follow §3.1 of AGENTS.md exactly (live MCP reads
first, manifest updates, sync evidence under `docs/design/sync/`, `npm run verify`,
`validate_penpot_sync.py`), resuming M08-001 → M08-005 in order.
If a provider ADR lands: implement M09-002 adapter behind the existing billing ports.
