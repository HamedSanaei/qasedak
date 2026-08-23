# Qasedak — Software Requirements Specification (SRS)

**Baseline:** v0.1 engineering scaffold  
**Change policy:** requirements that depend on Meta behavior remain provisional until M01 verifies current official documentation and contracts.

## 1. Scope

Qasedak provides workspace-scoped Instagram automation and conversation management through official integrations. This SRS defines initial functional boundaries and system qualities; it does not claim every requirement is implemented by the starter.

## 2. Actors

- **Visitor:** unauthenticated public-site user.
- **Member:** authenticated workspace participant.
- **Workspace owner/admin:** member allowed to manage sensitive workspace settings/integrations.
- **Operator:** Qasedak operational/support role subject to audited privileged access.
- **Meta platform:** external OAuth, Instagram API and webhook producer.
- **Payment provider:** future external billing adapter.

## 3. Functional requirements

### 3.1 Identity and workspaces

- **FR-ID-001:** The system shall authenticate users using the selected authentication mechanism defined in M02.
- **FR-ID-002:** A user shall operate within an explicit workspace context.
- **FR-ID-003:** Workspace membership and roles shall be enforced by the backend for every protected resource.
- **FR-ID-004:** Sensitive membership/role changes shall be auditable.
- **FR-ID-005:** A client-supplied workspace identifier alone shall never establish authorization.

### 3.2 Instagram connection

- **FR-IG-001:** An authorized workspace member shall initiate the supported Meta/Instagram OAuth connection flow.
- **FR-IG-002:** The backend shall validate returned OAuth state and associate the connection with the initiating workspace.
- **FR-IG-003:** Required tokens/secrets shall be protected at rest and never exposed to the browser after connection.
- **FR-IG-004:** The system shall expose connection health and actionable invalid/revoked states without exposing credentials.
- **FR-IG-005:** An authorized member shall disconnect an Instagram account and terminate future automation for that connection according to the defined lifecycle.

### 3.3 Webhook ingestion

- **FR-WH-001:** The system shall support required webhook endpoint verification/challenge behavior.
- **FR-WH-002:** Incoming webhook authenticity shall be verified according to the current official Meta contract before trusted processing.
- **FR-WH-003:** Accepted events shall be durably recorded before asynchronous/domain projection work where the chosen architecture requires it.
- **FR-WH-004:** Duplicate deliveries shall be detected and shall not produce duplicate intended effects.
- **FR-WH-005:** Processing failures shall be observable and recoverable without requiring callers to resend manually.

### 3.4 Conversations

- **FR-CV-001:** Supported inbound message events shall be projected into workspace-scoped conversations.
- **FR-CV-002:** Members with permission shall list/filter/paginate conversations and inspect supported message history.
- **FR-CV-003:** Supported outgoing replies shall use an infrastructure adapter and comply with current platform restrictions.
- **FR-CV-004:** Failed outgoing messages shall expose a useful state while avoiding accidental duplicate sends.
- **FR-CV-005:** Conversation data shall never cross workspace boundaries.

### 3.5 Automations

- **FR-AU-001:** Authorized members shall create, edit, activate, deactivate and inspect automation definitions.
- **FR-AU-002:** An automation definition shall have a valid trigger and at least one valid action before activation.
- **FR-AU-003:** Conditions/actions shall be validated against the currently supported capability matrix.
- **FR-AU-004:** Execution shall be deterministic for a given automation version and normalized event input.
- **FR-AU-005:** Retries/duplicate inputs shall not repeat side effects beyond the product's documented semantics.
- **FR-AU-006:** Users/operators shall be able to inspect execution outcome/error information sufficient for support without leaking secrets.

### 3.6 Contacts

- **FR-CT-001:** Supported social identities/interactions may create/update a workspace-owned contact projection.
- **FR-CT-002:** Contacts shall be scoped to a workspace and must not be globally merged merely because external identifiers appear similar.
- **FR-CT-003:** Authorized users may manage tags/notes supported by the MVP.
- **FR-CT-004:** Contact projection shall be idempotent under repeated integration events.

### 3.7 Billing and entitlements

- **FR-BL-001:** Subscription/plan state shall be represented server-side when billing is introduced.
- **FR-BL-002:** Payment-provider webhooks shall be authenticated and idempotent.
- **FR-BL-003:** Product limits/entitlements shall be enforced by backend use cases, not by hidden UI controls alone.

### 3.8 Frontend

- **FR-WEB-001:** The product UI shall be implemented in Next.js from approved Penpot handoffs.
- **FR-WEB-002:** Important screens shall define loading, empty, error, success and permission states.
- **FR-WEB-003:** The frontend shall consume backend APIs through explicit client/contracts and shall not embed backend source/business rules.
- **FR-WEB-004:** Critical user flows shall be usable with keyboard navigation and meet the accessibility targets defined in Design/NFR documents.

## 4. External interface requirements

All business APIs are versioned under a stable API namespace when introduced. Errors use a consistent machine-readable problem format. Pagination/filtering conventions must be consistent across modules. External Meta and payment payloads terminate in Infrastructure and are normalized before domain/application use.

Meta-facing behavior is additionally bound by the contracts verified in M01 and must be kept consistent with them:

- **Capability boundary:** messaging capabilities (inbound DM webhooks, comment-triggered Private Replies, window-bound replies) exist only through the Messenger Platform path with Facebook Login tokens; comment→DM is exactly one Private Reply per comment ID within Meta's documented windows; `comments`/`live_comments` webhooks require Advanced Access, a Live app, and public accounts (`docs/product/instagram-mvp-capability-matrix.md`, ADR-006).
- **Connection/token lifecycle:** Business Login for Instagram authorization, `instagram_business_*` scopes, short-lived → long-lived (60 days) exchange, refresh preconditions, and permanent expiry semantics as specified in `docs/product/meta-oauth-token-lifecycle.md`; tokens are encrypted at rest, workspace-owned through the Instagram module, and never exposed to clients.
- **Webhook authenticity:** event notifications validate HMAC-SHA256 over raw request bytes with constant-time comparison; subscription setup validates `hub.mode`/`hub.verify_token` and echoes `hub.challenge` verbatim (`docs/architecture/ADR-007-webhook-authenticity.md`, deterministic fixtures in `Qasedak.Modules.Instagram.UnitTests`).

## 5. Non-functional requirements

- **NFR-REL-001:** Webhook and automation paths must tolerate duplicate delivery and retry.
- **NFR-SEC-001:** Workspace isolation and least privilege are mandatory and regression-tested.
- **NFR-SEC-002:** Secrets/tokens must not appear in logs, client payloads, repository or error details.
- **NFR-PERF-001:** API latency/load objectives shall be baselined with representative scenarios before production release.
- **NFR-OBS-001:** Correlation and structured telemetry shall permit tracing a webhook through normalization and automation outcome.
- **NFR-MNT-001:** Architecture project-reference rules are automatically checked in CI.
- **NFR-TST-001:** Critical domain/application behavior requires executable regression tests; quality gates may not be silently weakened.
- **NFR-DEP-001:** Deployment artifacts are immutable container images built by CI.
- **NFR-REC-001:** PostgreSQL backup/restore and migration recovery are rehearsed before production launch.

## 6. Privacy and retention

Retention periods for conversation/contact/event data must be explicit before launch. Store only data required for supported product behavior, protect credentials separately from ordinary business data, and document deletion/export obligations applicable to the launch jurisdiction and Meta platform terms during product/legal readiness.

## 7. Acceptance and traceability

Every implementation task should link requirements to tests or an explicit non-test rationale. Requirements affected by an ADR or platform capability change must update this document, the corresponding design/architecture material, and affected tests in the same task.
