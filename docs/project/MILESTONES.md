# Qasedak milestones

Each milestone ends only when its acceptance gates, tests, state updates and handoff are complete. The suggested milestone commit is provided even when the human chooses to squash individual task commits.

## M00 — Engineering Foundation

**Goal:** establish repository architecture, AI-agent protocol, documentation, Graphify context, deterministic dependencies and green baseline CI.

Exit: Graphify healthy, dependency lockfiles committed, backend/frontend tests green, Docker images build, all repository guardrails pass.

Suggested milestone commit: `chore(foundations): establish qasedak engineering baseline`

## M01 — Product & Meta Feasibility

**Goal:** freeze MVP capability matrix against current official Meta capabilities/limitations before product code hardens assumptions.

Exit: OAuth/token lifecycle, permissions, webhook contracts, messaging/comment constraints and relevant ADRs documented and contract-spiked.

Suggested milestone commit: `docs(meta): establish instagram integration feasibility baseline`

## M02 — Identity & Workspaces

**Goal:** users, workspaces, membership and authorization boundaries.

Exit: authenticated workspace-scoped API, persistence and authorization integration tests.

Suggested milestone commit: `feat(identity): deliver workspace identity foundation`

## M03 — Instagram Account Connection

**Goal:** securely connect, inspect, disconnect and monitor Instagram professional accounts.

Exit: OAuth adapter, protected token persistence, account lifecycle and failure/revocation tests.

Suggested milestone commit: `feat(instagram): deliver account connection lifecycle`

## M04 — Durable Webhook Ingestion

**Goal:** accept Meta events safely under retries, duplicates and failures.

Exit: verification, durable inbox/idempotency, normalization and observability with replay/concurrency tests.

Suggested milestone commit: `feat(instagram): deliver durable webhook ingestion`

## M05 — Conversations & Inbox

**Goal:** project messages into workspace conversations and support compliant replies.

Exit: domain model, inbound projection, query API, outgoing reply adapter, end-to-end integration tests and the Penpot ↔ Next.js design-sync foundation (M05-005) establishing Penpot as the approved visual source for all later UI milestones.

Suggested milestone commit: `feat(conversations): deliver instagram inbox v1`

## M06 — Automation Engine v1

**Goal:** deterministic rule/flow automation, initially including comment-to-DM where allowed by Meta policy.

Exit: versioned automation aggregate, evaluator, idempotent execution, first supported flow and comprehensive regression tests.

Suggested milestone commit: `feat(automations): deliver automation engine v1`

## M07 — Contacts & Lightweight CRM

**Goal:** build workspace-owned contact identity/projections, tags and notes from social interaction.

Suggested milestone commit: `feat(contacts): deliver contact management v1`

## M08 — Penpot-driven Next.js Product UI

**Goal:** implement approved Penpot screens as production Next.js features through the M05-005 synchronization contract: every task fetches the latest mapped Penpot designs via MCP before implementing or updating, records sync evidence, and keeps behavior/API/state application-owned.

Exit: design foundation extended from the sync system, identity/workspace flows, Instagram settings, inbox and automation builder with responsive/accessibility tests — all mappings current in `penpot-sync.json`.

Suggested milestone commit: `feat(web): deliver penpot-driven product ui v1`

## M09 — Billing & Entitlements

**Goal:** subscription model, payment provider integration and server-side entitlement enforcement.

Suggested milestone commit: `feat(billing): deliver subscription and entitlement foundation`

## M10 — Hardening, Security & Observability

**Goal:** production-grade telemetry, abuse/rate controls, auditability, backup/restore, security/performance/mutation gates.

Suggested milestone commit: `chore(hardening): complete production reliability gates`

## M11 — Production Release

**Goal:** production contract, migration/deployment/rollback rehearsal and release baseline.

Suggested milestone commit: `chore(release): prepare qasedak v1 production baseline`

## M12 — v2 Product Features

**Goal:** retire the v1 deferrals that no longer have blockers. M12-001 ships the
server-side inbox search the approved Penpot design explicitly marked as pending the
backend query; M12-002 enables the inbox thread context panel (contacts/tags/notes) now
that M07 shipped the CRM surface; M12-003 delivers the workspace dashboard content once a
Qasedak-native design is approved. UI tasks keep following the Penpot sync contract:
approved mappings in `penpot-sync.json` are the visual source; divergences are recorded
in sync evidence, never invented.

Suggested milestone commit: `feat(v2): deliver inbox search and product v2 features`

## M13 — Instagram OpenReply Parity & Production Integration

**Goal:** complete the Qasedak backend architecture required to provide the Instagram
capabilities demonstrated by OpenReply that are supported by the current official Meta
contract and intentionally included in Qasedak scope, while correcting Qasedak's current
connected-account routing and comment Private Reply semantics. OpenReply is a product-
behavior reference only; current official Meta documentation and Qasedak's modular Clean
Architecture remain authoritative. Unsupported provider behavior is documented and
excluded truthfully rather than simulated or allowed to block independent capabilities.

M13 preserves the completed M01–M08 foundations: OAuth exchange and protected token
storage, account health primitives, verified/durable webhook ingestion, transport-free
normalization, Conversations/Contacts projections, versioned deterministic automations,
the execution ledger, and the existing Next.js/Penpot sync contract. Work proceeds in
this order: current Meta contract reconciliation → exact channel-account identity →
versioned Graph transport and durable work → provider primitives → automation
orchestration → Penpot-governed frontend integration → production/compliance gate.

Exit: all M13 tasks are complete for the supported, intentionally scoped capability set;
every investigated behavior is classified in the final compliance matrix and every Meta
adapter has deterministic current-contract coverage; two connected Instagram accounts in
one workspace remain isolated end to end; comment-triggered first contact uses a globally
claimed Private Reply rather than the normal conversation send path; scheduled work is
restart-safe; no secret reaches a job, log, browser or foreign workspace; and the full
repository/production-parity gates pass without live Meta calls in CI. A provider-
unsupported follow-status lookup does not block the supported opening/postback/reveal or
independent automation-parity exit criteria.

Suggested milestone commit: `feat(instagram): deliver openreply parity integration`
