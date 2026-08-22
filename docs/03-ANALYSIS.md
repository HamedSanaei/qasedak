# Qasedak — Software Analysis Document

## 1. Analysis purpose

This document translates the product vision into problem boundaries before detailed implementation. It focuses on business capabilities, runtime risks and open questions rather than framework classes.

## 2. Core business capabilities

### Identity / Workspace
Owns users, workspace membership, authorization context and later privileged membership/audit behavior. Workspace is the main tenant boundary; no resource is considered safely owned merely because a request contains its workspace ID.

### Instagram Integration
Owns connected-account lifecycle, Meta authorization/token state, external identifiers, webhook receipt/normalization and adapters to supported Instagram APIs. It does not own conversations or automation business rules.

### Conversations
Owns the business projection of conversations/messages and compliant reply use cases. It consumes normalized integration facts rather than raw Meta JSON.

### Automations
Owns definitions, versions, trigger/condition/action semantics and execution records/orchestration. It should remain deterministic and independently testable; transport payloads and HTTP clients are outside its Domain.

### Contacts
Owns workspace-local contact identity and lightweight CRM projections such as tags/notes. It must avoid assuming a global person identity from social identifiers.

### Billing
Owns subscription/entitlement concepts and provider integration boundary when selected.

## 3. Key use-case analysis

### 3.1 Connect Instagram account

Happy path: authorized member initiates → server creates protected correlation/state → user authorizes with Meta → callback is validated → server exchanges/obtains required identity/token information → connection is stored in the Instagram schema → webhook subscription/capability state is validated → UI receives connection health, never raw secrets.

Failure cases include denied authorization, expired/tampered state, missing permissions, unsupported account type, token exchange error, duplicated callback, connection already owned/linked, subscription failure and later revocation. Every failure should leave a recoverable, explainable state.

### 3.2 Receive webhook

Ingress must be intentionally thin: verify request per contract, identify/deduplicate delivery, durably capture required data, acknowledge within platform timing expectations, then normalize/process. Slow automation logic must not make webhook acknowledgment fragile. Duplicate and out-of-order delivery are normal conditions, not exceptional assumptions.

### 3.3 Execute automation

A normalized event identifies a potentially relevant workspace/account. The system selects active compatible automation versions, evaluates conditions deterministically, creates an execution identity, and invokes supported action ports. Side-effect identity/idempotency must survive retries. Execution status must distinguish validation/policy failure, transient external failure and permanent failure.

## 4. Consistency and concurrency analysis

A monolith does not eliminate distributed-system behavior because Meta/webhooks/retries are external. The design must assume at-least-once inputs. Database transactions may protect local state, but external API calls cannot be atomically committed with PostgreSQL. Inbox/outbox/idempotency patterns are therefore candidates and are explicitly scheduled for implementation, not prematurely hidden in the starter.

Optimistic concurrency or version checks should protect mutable automation definitions and other race-sensitive aggregates where demonstrated. Automation execution should bind to an immutable/versioned definition so editing a flow does not change the meaning of an in-flight execution.

## 5. Security analysis

Primary threats include stolen OAuth tokens, tampered OAuth state, forged/replayed webhooks, cross-workspace IDOR, role escalation, token/log leakage, automation abuse, rate-limit amplification and operator privilege misuse. Mitigations belong at multiple boundaries: protected secret storage, webhook validation/idempotency, server-side authorization, least-privilege permissions, audit trails, rate/abuse controls, structured redaction and security tests.

## 6. Failure analysis

- **PostgreSQL unavailable:** readiness should fail; ingress/write operations must fail safely rather than acknowledge work that was not durably accepted.
- **Meta transient failure:** preserve execution state and apply bounded retry/backoff according to operation semantics.
- **Meta permanent/policy failure:** surface actionable non-retry state.
- **Token invalid/revoked:** mark connection unhealthy and prevent blind repeated calls.
- **Duplicate webhook:** dedupe to the same logical processing/effect.
- **Agent/regression error:** architecture/tests/CI/state are designed as executable memory and change alarms.

## 7. UX analysis

Automation tooling must expose state clearly. A polished flow builder without clarity about connected-account health, trigger eligibility, validation errors and execution results is not sufficient. Penpot design work should include non-happy states, responsive behavior and accessibility rather than only ideal desktop frames.

## 8. Open questions assigned to M01+

- Which exact Instagram Login/API path and permission set best matches the MVP at implementation time?
- Which triggers/actions are officially available and under what conversation windows/restrictions?
- What app-review/business-verification steps affect launch sequencing?
- What payload/delivery identifiers are stable enough for webhook deduplication?
- Which user data must be retained for useful automation/inbox behavior, and for how long?
- Which authentication/payment providers best fit deployment/legal requirements?

These are intentionally not guessed in the starter. They become documented decisions backed by current primary-source verification.
