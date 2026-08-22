# Task tracker

This file is the human-readable task source of truth. Every task has an explicit status and suggested commit. Agents must keep this file synchronized with `.agent-state/PROJECT_STATE.json`.

Status values: `TODO`, `IN_PROGRESS`, `BLOCKED`, `DONE`.

## M00-001 — Bootstrap modular repository
**Status:** DONE

**Outcome:** Create backend/frontend boundaries, module projects, Docker/GitHub baseline and guard scripts.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `chore(repo): bootstrap qasedak modular monolith`

## M00-002 — Establish documentation and agent-state baseline
**Status:** DONE

**Outcome:** Create engineering docs, Persian docs, ADRs, milestones, task tracker and multi-agent contract.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `docs(architecture): establish qasedak engineering baseline`

## M00-003 — Initialize and verify Graphify
**Status:** DONE

**Outcome:** Create backend/frontend boundaries, module projects, Docker/GitHub baseline and guard scripts.

**Completion evidence:** Graphify 0.9.26 verified; first real graph created (`graphify . --no-viz --code-only`: 277 nodes/297 edges/43 communities) plus `graphify cluster-only .` for `graphify-out/GRAPH_REPORT.md`; four bounded queries (--budget 1200) run on module dependencies, composition root, Next.js boundary and state system; four healthy evidence rows appended to `.agent-state/GRAPHIFY_EVIDENCE.md`; `agent_preflight.py --task M00-003` passes. code-only mode used because no LLM API key exists in the environment; doc semantic extraction remains unavailable until a key is provided.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `chore(graphify): initialize repository knowledge graph`

## M00-004 — Lock dependencies and green all gates
**Status:** DONE

**Outcome:** Resolve toolchains; generate npm lockfile; restore/build/test .NET; run frontend checks; build both Docker images; make CI green without weakening gates.

**Completion evidence:** `package-lock.json` committed and frontend Dockerfile/CI switched to `npm ci`; TypeScript pinned 7.0.2→6.0.3 (installed typescript-eslint hard-fails on TS≥7); missing `using Xunit;` added to both backend test projects; underscore test-method names renamed to satisfy CA1707 under TreatWarningsAsErrors; minimal `.dockerignore` files added for backend/frontend. Gates on workstation: `dotnet build -c Release` 0 warnings/0 errors; `dotnet format --verify-no-changes` pass; `dotnet test` 3/3 pass; `npm run verify` (lint/typecheck/test/build) pass; `docker build` qasedak-api:verify and qasedak-web:verify pass. `generate_manifest.py` now ignores gitignored runtime artifacts (`cache` dirs, `tsconfig.tsbuildinfo`) so the committed manifest matches fresh CI checkouts.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `chore(ci): lock baseline dependencies and green all gates`

## M01-001 — Define Instagram MVP capability matrix
**Status:** TODO

**Outcome:** Verify desired automations against current official Meta API capabilities, permissions, review requirements and policy constraints.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `docs(product): define instagram automation mvp capability matrix`

## M01-002 — Define Meta OAuth and token lifecycle
**Status:** TODO

**Outcome:** Specify OAuth flow, permission model, token protection, refresh/expiry/revocation and workspace ownership.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `docs(instagram): define meta oauth and token lifecycle contract`

## M01-003 — Spike webhook verification contract
**Status:** TODO

**Outcome:** Implement/test a minimal deterministic webhook verification and fixture contract without introducing feature persistence.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `test(instagram): add webhook verification contract spike`

## M01-004 — Finalize Meta integration ADRs
**Status:** TODO

**Outcome:** Record decisions/constraints learned from feasibility work and update SRS/architecture.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `docs(adr): finalize meta integration decisions for mvp`

## M02-001 — Model workspace membership domain
**Status:** TODO

**Outcome:** Define users/workspaces/memberships/roles and invariants.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(identity): model workspace membership domain`

## M02-002 — Implement authentication use cases
**Status:** TODO

**Outcome:** Implement bounded application use cases and security contracts.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(identity): implement authentication use cases`

## M02-003 — Persist identity/workspace state
**Status:** TODO

**Outcome:** Implement EF Core/PostgreSQL identity schema and integration tests.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(identity): persist users workspaces and memberships`

## M02-004 — Enforce workspace authorization
**Status:** TODO

**Outcome:** Apply workspace authorization policies at server boundaries with negative-path tests.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(identity): enforce workspace authorization policies`

## M03-001 — Implement Meta OAuth adapter
**Status:** TODO

**Outcome:** Create Infrastructure Meta OAuth adapter behind application ports with deterministic contract tests.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(instagram): add meta oauth infrastructure adapter`

## M03-002 — Implement account lifecycle
**Status:** TODO

**Outcome:** Connect/disconnect/inspect Instagram professional accounts as application use cases.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(instagram): add account connection lifecycle`

## M03-003 — Persist accounts and protected tokens
**Status:** TODO

**Outcome:** Create Instagram schema and secure token storage/rotation abstraction.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(instagram): persist connected accounts securely`

## M03-004 — Manage token health/revocation
**Status:** TODO

**Outcome:** Detect unhealthy/revoked access and surface actionable state.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(instagram): manage token health and revocation`

## M04-001 — Verify Meta webhook requests
**Status:** TODO

**Outcome:** Implement endpoint challenge/signature/authenticity rules per verified Meta contract.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(instagram): verify meta webhook requests`

## M04-002 — Create durable idempotent webhook inbox
**Status:** TODO

**Outcome:** Persist event identity/body metadata and guarantee duplicate-safe ingestion.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(instagram): add idempotent webhook inbox`

## M04-003 — Normalize integration events
**Status:** TODO

**Outcome:** Translate raw Meta payloads to explicit integration events without leaking transport models into Domain.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(instagram): normalize webhook integration events`

## M04-004 — Instrument webhook processing
**Status:** TODO

**Outcome:** Add correlation, structured logs, metrics and failure/retry visibility.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(observability): instrument webhook processing`

## M05-001 — Model conversations/messages
**Status:** TODO

**Outcome:** Define conversation and message state/identity ownership/invariants.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(conversations): model conversation and message state`

## M05-002 — Project inbound Instagram messages
**Status:** TODO

**Outcome:** Consume normalized events idempotently into conversation state.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(conversations): project inbound instagram messages`

## M05-003 — Expose inbox queries
**Status:** TODO

**Outcome:** Add workspace-scoped pagination/filter/detail APIs.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(conversations): expose workspace inbox queries`

## M05-004 — Send compliant replies
**Status:** TODO

**Outcome:** Implement reply use case and Instagram messaging adapter with policy/error tests.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(conversations): send replies through instagram`

## M06-001 — Model automation aggregate
**Status:** TODO

**Outcome:** Define trigger/conditions/actions/status/version invariants.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(automations): model automation aggregate`

## M06-002 — Persist versioned automations
**Status:** TODO

**Outcome:** Persist definitions without losing execution reproducibility/history.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(automations): persist versioned automation definitions`

## M06-003 — Implement deterministic evaluator
**Status:** TODO

**Outcome:** Evaluate trigger/conditions deterministically with exhaustive unit/property-style cases.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(automations): implement deterministic rule evaluation`

## M06-004 — Orchestrate idempotent actions
**Status:** TODO

**Outcome:** Guarantee at-most-intended-effect under webhook redelivery/retry/concurrency.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(automations): orchestrate idempotent action execution`

## M06-005 — Deliver comment-to-DM flow
**Status:** TODO

**Outcome:** Implement first policy-compliant comment trigger → DM action based on current Meta capability matrix.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(automations): add comment to dm automation flow`

## M07-001 — Model workspace contact identity
**Status:** TODO

**Outcome:** Define contact/social identity ownership and merge invariants.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(contacts): model workspace contact identity`

## M07-002 — Project interactions into contacts
**Status:** TODO

**Outcome:** Idempotently maintain contacts from supported social activity.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(contacts): project social interactions into contacts`

## M07-003 — Add tags, notes and queries
**Status:** TODO

**Outcome:** Implement lightweight CRM behavior and workspace-scoped queries.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(contacts): add lead tags notes and queries`

## M08-001 — Implement Penpot design foundation
**Status:** TODO

**Outcome:** Translate approved tokens/components/layout primitives to reusable Next.js UI foundation.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(web): implement penpot design foundation`

## M08-002 — Implement auth/workspace UI
**Status:** TODO

**Outcome:** Build approved authentication/workspace screens and behavior.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(web): add authentication and workspace flows`

## M08-003 — Implement Instagram account UI
**Status:** TODO

**Outcome:** Build connection/state/revocation management screens.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(web): add instagram account management ui`

## M08-004 — Implement inbox UI
**Status:** TODO

**Outcome:** Build responsive conversation inbox/detail/reply experience.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(web): implement conversation inbox`

## M08-005 — Implement automation builder v1
**Status:** TODO

**Outcome:** Build approved automation list/editor/validation/state UX.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(web): implement automation builder v1`

## M09-001 — Model subscriptions/entitlements
**Status:** TODO

**Outcome:** Define plans, subscription lifecycle and server-owned entitlements.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(billing): model subscriptions and entitlements`

## M09-002 — Integrate payment provider
**Status:** TODO

**Outcome:** Implement provider adapter/webhooks/idempotency after provider selection ADR.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(billing): integrate payment provider`

## M09-003 — Enforce entitlements server-side
**Status:** TODO

**Outcome:** Apply limits/feature access in application/server boundaries with tests.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(billing): enforce server-side entitlements`

## M10-001 — Add structured telemetry/correlation
**Status:** TODO

**Outcome:** Standardize logging, tracing, metrics, correlation and privacy redaction.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(observability): add structured tracing and correlation`

## M10-002 — Add rate limits/abuse controls
**Status:** TODO

**Outcome:** Protect public/authenticated/webhook paths based on risk and external quotas.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(security): enforce rate limits and abuse controls`

## M10-003 — Add sensitive-action audit trail
**Status:** TODO

**Outcome:** Record security/billing/account/automation sensitive actions with immutable intent.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(security): add sensitive action audit trail`

## M10-004 — Validate PostgreSQL backup/restore/migrations
**Status:** TODO

**Outcome:** Document and rehearse backup, restore, migration and rollback-safe procedures.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `ops(postgres): validate backup restore and migrations`

## M10-005 — Add mutation/security/load gates
**Status:** TODO

**Outcome:** Use mutation testing on critical rules, targeted security tests and representative load tests.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `test(hardening): add mutation security and load gates`

## M11-001 — Finalize production environment contract
**Status:** TODO

**Outcome:** Freeze production configuration/secrets/network/storage/probe requirements.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `ops(prod): document production environment contract`

## M11-002 — Rehearse deployment and rollback
**Status:** TODO

**Outcome:** Perform release candidate migration/deploy/smoke/rollback exercise.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `ops(release): rehearse deployment and rollback`

## M11-003 — Prepare v1 release baseline
**Status:** TODO

**Outcome:** Close release checklist, docs/state, image provenance and operational handoff.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `chore(release): prepare qasedak v1 production baseline`
