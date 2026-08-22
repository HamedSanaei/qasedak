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

Exit: domain model, inbound projection, query API, outgoing reply adapter and end-to-end integration tests.

Suggested milestone commit: `feat(conversations): deliver instagram inbox v1`

## M06 — Automation Engine v1

**Goal:** deterministic rule/flow automation, initially including comment-to-DM where allowed by Meta policy.

Exit: versioned automation aggregate, evaluator, idempotent execution, first supported flow and comprehensive regression tests.

Suggested milestone commit: `feat(automations): deliver automation engine v1`

## M07 — Contacts & Lightweight CRM

**Goal:** build workspace-owned contact identity/projections, tags and notes from social interaction.

Suggested milestone commit: `feat(contacts): deliver contact management v1`

## M08 — Penpot-driven Next.js Product UI

**Goal:** implement approved Penpot screens as production Next.js features.

Exit: design foundation, identity/workspace flows, Instagram settings, inbox and automation builder with responsive/accessibility tests.

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
