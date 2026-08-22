# Qasedak — Software Design Document

## 1. Design approach

Qasedak uses module-first organization and vertical feature/use-case slices inside each module Application layer. The goal is to keep code needed to understand a business capability near that capability while preserving dependency inversion.

A typical future feature may look like:

```text
Modules/Automations/
├── Qasedak.Modules.Automations.Domain/
│   ├── Automations/Automation.cs
│   └── Automations/AutomationVersion.cs
├── Qasedak.Modules.Automations.Application/
│   └── ActivateAutomation/
│       ├── Command.cs
│       ├── Handler.cs
│       └── Validator.cs
└── Qasedak.Modules.Automations.Infrastructure/
    ├── Persistence/
    └── DependencyInjection.cs
```

Do not introduce `Commands/`, `Queries/`, `Handlers/`, `Validators/` global buckets when feature-local structure is clearer.

## 2. Domain design

Domain classes should contain meaningful behavior/invariants when the business concept is complex. Avoid both anemic property bags and ceremonial DDD. A simple read model does not need an aggregate/factory/domain event merely to satisfy a pattern. Complex automation lifecycle and execution semantics likely justify richer models; basic projections may not.

Domain failures should use domain-meaningful result/exception semantics selected consistently during implementation. Domain code receives explicit time/identity/policy inputs where nondeterminism would make tests brittle.

## 3. Application design

Application use cases define input, authorization context, orchestration, ports and output. They should be independently testable without ASP.NET hosting. Interfaces are introduced because an external effect/storage boundary is needed, not because every class needs an interface.

Transactions are owned around use-case consistency requirements. External calls and database commits require explicit retry/idempotency design rather than pretending a database transaction makes them atomic.

## 4. API design

The API remains thin and may use Minimal APIs or bounded endpoint modules as features grow. Endpoints map HTTP → application input and application result → HTTP/problem response. Validation that describes request shape belongs near transport/application boundary; business validity belongs in Domain/Application.

API conventions to define consistently before feature spread include version prefix, Problem Details error shape, pagination/filter syntax, correlation headers, authentication/authorization and idempotency semantics where client-generated requests need them.

## 5. External integration design

Meta types/DTOs are Infrastructure concerns. Adapter boundaries convert them into application/integration contracts so raw vendor JSON does not propagate into Domain. HTTP clients use centrally configured resilience/timeout/log-redaction policies appropriate to each operation. Live provider calls are excluded from ordinary CI tests.

Webhook payload fixtures should be versioned in tests only when legally/operationally appropriate and scrubbed of real user data/secrets.

## 6. Frontend design

The sole frontend is `frontend/Qasedak.Web`, using Next.js and TypeScript. It is organized by product feature rather than a global page/component dump. Generic primitives may be shared; business-specific components stay with their feature.

Penpot is the visual source of truth. Every production screen implementation must record its Penpot frame/reference and state matrix in `docs/design/PENPOT-HANDOFF.md` or a feature-specific derivative. Generated design snippets are references, not production architecture.

Important UI states include loading, empty, partial/stale, permission denied, connection unhealthy, recoverable error, destructive confirmation and success. Responsive and keyboard/focus behavior are part of design acceptance.

## 7. Persistence design

Each module Infrastructure owns its mappings/migrations/repositories. Avoid generic repository abstractions that erase useful EF Core behavior. Create a repository/port when Domain/Application requires a stable persistence capability or when it meaningfully improves testability/ownership. Query-heavy read paths may use dedicated projections/query services without forcing aggregate loading.

## 8. Background processing design

The starter deliberately does not select a queue library. M04/M06 shall choose a mechanism based on durable webhook acknowledgment, retries, scheduling, observability and deployment needs. Business contracts must not depend on a queue vendor. Jobs carry stable identifiers and are idempotent at effects, not just “deduplicated” in memory.

## 9. Error and resilience design

Errors distinguish client validation, authorization, not-found/conflict, provider policy/permanent failure, transient external failure and internal fault. Retry only operations classified safe/retryable. Retries use bounded backoff and an idempotency identity; infinite retry loops are prohibited.

## 10. Observability design

Use structured events with stable names, correlation IDs and business-safe identifiers. Never log tokens/authorization headers or sensitive raw payloads by default. Execution logs should answer: which normalized input, workspace/account, automation version, action attempt and final classification were involved.

## 11. Design review checklist

Before a feature is considered designed: module ownership is clear; dependencies point inward; security/workspace boundary is identified; retry/duplicate/concurrency semantics are explicit; test boundaries are identified; Penpot states exist for UI work; data ownership/migration impact is known; and any architectural trade-off is captured in an ADR.
