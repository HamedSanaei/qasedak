# ADR-012 — Durable scheduled work (platform mechanism)

- Status: Accepted (M13-004)
- Needs: M13-004 task (refresh, snapshots, reconciliation, follow-ups, read fallback)

## Context

Qasedak had no durable scheduled-work mechanism: webhook ingestion is synchronous,
token refresh/media/history/reveal continuations need restart-safe delayed execution
with concurrency control. Copying a Redis/BullMQ topology would add an operational
dependency the modular monolith does not need; PostgreSQL is already the durable
dependency and already proves atomic primitives elsewhere (webhook inbox identity,
automation run ledger).

## Decision

1. **Platform-owned mechanism in BuildingBlocks**: contracts in
   `Qasedak.BuildingBlocks.Application.Scheduling` (`ScheduledWorkItem`,
   `IScheduledWorkStore`, `IScheduledWorkHandler`, `WorkOutcome`,
   `ScheduledWorkOptions`, deterministic `ScheduledWorkBackoff`, enqueue-time
   `ScheduledWorkPayloadGuard`); PostgreSQL implementation in
   `Qasedak.BuildingBlocks.Infrastructure.Scheduling` (`platform` schema,
   `scheduled_jobs` table, `EfScheduledWorkStore`, `ScheduledWorkDispatcher`,
   `ScheduledWorkMetrics`).
2. **Ownership/runtime**: the platform owns rows and transitions; each
   *module* owns its `IScheduledWorkHandler` implementations, registered at the
   Api composition root via `AddScheduledWorkHandler{T}` (resolved per dispatch
   scope — never singletons holding scoped state). Unknown work types
   dead-letter without spinning.
3. **Transaction semantics**: enqueue is insert-or-load on the idempotency unique
   index (one logical job per key, race-safe); claiming is a single
   `UPDATE..WHERE..RETURNING` over (pending ∧ due) ∪ (claimed ∧ lease-expired)
   with `FOR UPDATE SKIP LOCKED` (one winner per row); completion/failure paths
   verify lease ownership and throw `scheduledwork.leaseLost` otherwise.
   Handler faults default to retryable; terminal states are explicit.
   Delivery guarantee is **at-least-once job execution**: a crash between a
   handler's external effect and its settlement can re-execute the handler, so
   consumers own external-effect idempotency (same rule as the automation run
   ledger); only local settlement is single-winner.
4. **Leases**: one owner id per host process; crashed hosts stop renewing and
   their records become claimable after `LeaseSeconds`. Renewal exists but the
   dispatcher favors short bounded handlers; cancellation propagates, and a
   cancelled dispatch leaves the lease to expire (reclaim path, tested).
5. **No-secrets rule**: payloads are Qasedak-owned JSON; the guard rejects known
   token shapes at enqueue (defense in depth, not a boundary); handlers resolve
   protected tokens at execution time from `ConnectedAccountId` and never persist
   them. Secret-leak tests pin enqueue refusal and payload/log cleanliness.
6. **Backoff** is deterministic exponential (`base * 2^(n-1)`, capped, no jitter)
   so retries are reproducible in tests; thundering-herd risk is accepted and
   bounded by per-host batch sizes and naturally spread due times.

## Consequences

- M13-005+ implement only focused handlers + enqueue sites; the mechanism,
  migration and gates land once, here.
- New `platform` schema + `ConnectionStrings:Platform` (same physical database
  allowed); the migrator, rehearsal scripts, compose files, fixture and
  environment contract all cover eight schemas now.
- Rollback: additive table only; the previous image ignores the schema and runs
  unchanged (the dispatcher simply does not exist there).

## Verification

Real-PostgreSQL tests prove unique enqueue + enqueue races, competing-worker
claims, lease reclaim, retry/backoff/dead-letter progression, restart recovery,
cancellation, lost-lease refusal and secret refusal; dispatcher tests prove
success/retry/permanent/fault/unknown-type settlement. No live Meta calls.
