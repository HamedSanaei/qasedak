# Automation operations and recovery contract

**Task:** M14-001
**Status:** normative for M14-002, M14-003, M14-004 and the future operations console
**Contract date:** 2026-09-11
**Executable form:** `backend/Modules/Automations/Qasedak.Modules.Automations.Application/AutomationOperationsPolicy.cs`
**Executable-form tests:** `backend/tests/Qasedak.Modules.Automations.UnitTests/AutomationOperationsPolicyTests.cs`

---

## 0. Purpose and authority

This contract freezes the operator-facing automation execution and recovery semantics of Qasedak
before any execution-history API, operations console, or recovery UI exists (M14-002 … M14-008).
It is derived from the actual persisted state machines in the current codebase, not from an
idealized model:

- Run/action lifecycle authority: `backend/Modules/Automations/Qasedak.Modules.Automations.Domain/AutomationRun.cs`
  (`AutomationRunStatus`, `AutomationActionStatus`, `AutomationRun`, `AutomationActionExecution`).
- Scheduled-work lifecycle authority: `backend/BuildingBlocks/Qasedak.BuildingBlocks.Application/Scheduling/ScheduledWork.cs`
  (`ScheduledWorkStatus`, `ScheduledWorkItem`, `IScheduledWorkStore`, `WorkOutcome`).
- Operator projection (this contract, executable): `AutomationOperationsPolicy` — pure,
  deterministic, I/O-free, in the Automations Application layer (no Infrastructure, no ASP.NET Core
  dependency).

This document does not rename or renumber persisted enums, does not rewrite historical rows, and
adds no persistence, endpoints, or UI. Where document and code disagree, the code is authoritative
and this document must be corrected; the executable policy tests pin both sides together.

The safety invariant is absolute and inherited from M13 (M13-009 effect ledger, M13-012 durable
attempt markers, M13-004 scheduled-work authority):

> Once an external provider mutation may have started, Qasedak never infers that a retry or resend
> of that effect is safe. A timeout, connection loss, process crash, HTTP 5xx, unknown provider
> result, or missing response is never, by itself, proof that the provider mutation did not happen.

`Attempting` and `Uncertain` are visibility/reconciliation states, never resend commands.

### Non-goals (explicitly out of M14-001 scope)

- No run/effect-history endpoints (M14-002).
- No scheduled-work diagnostics endpoints or generic scheduler console (M14-003).
- No disposition persistence/API (M14-004).
- No frontend, Penpot, or console UI work (M14-006/M14-007).
- No new trigger/action/provider capability, Ads, publishing, tagging, Human Agent.
- No automatic provider resend, no provider probing, no live Meta verification dependency.
- No database migration and no persistence change.

---

## 1. Execution taxonomy

### 1.1 Run states (`AutomationRunStatus`, owner: Automations Domain)

| Canonical name | Owner | Meaning | Provider mutation may have occurred? | Terminal? | Operator action meaningful? | Safe exposed status |
|---|---|---|---|---|---|---|
| `Running` | Automations Domain | At least one action slot is not yet terminal. | Per-slot only | No | Read-only; execution is progressing | `in-progress` |
| `Completed` | Automations Domain | Every action succeeded. | Yes (delivered) | Yes (immutable) | No | `completed-successfully` |
| `Failed` | Automations Domain | At least one slot is `Failed` (retriable local/channel failure). Closed to new recordings but resumable via `ReopenForRetry` into a new execution pass. | No (every failed slot is proven pre-provider) | No | Yes — resume is engine-owned | `retriable-local-failure` |
| `Refused` | Automations Domain | Evaluation matched but the source automation was stale/disabled mid-run. | No | Yes (immutable) | No | `refused-before-execution` |
| `Finished` | Automations Domain | All slots terminal but at least one was not delivered (`Suppressed` / `Uncertain` / `TerminalFailed`). Immutable; resuming a `Finished` run never re-dispatches anything. | Per-slot | Yes (immutable) | Read-only; per-slot dispositions may apply | `finished-with-non-delivery` |
| (unknown value) | — | Unrecognized future value read from persistence. | Unknown | Fail-closed | No | `unknown-unsupported` |

**Immutability rule.** `Completed`, `Refused` and `Finished` runs are immutable historical facts.
The domain throws `run.immutable` / `run.alreadyRecorded` on any write into them, and the operator
contract inherits that: no operator disposition rewrites run status, slot status, attempt
timestamps, or provider outcome fields. Dispositions are a separate, future, additive operator
layer (M14-004); they never mutate execution truth.

### 1.2 Action-slot states (`AutomationActionStatus`, owner: Automations Domain)

| Canonical name | Owner | Meaning | Provider mutation may have occurred? | Terminal? | Operator action meaningful? | Safe exposed status |
|---|---|---|---|---|---|---|
| `Pending` | Automations Domain | Awaiting its strict-index execution pass; no attempt began. | No (proven) | No | None (engine-owned) | `not-started` |
| `Succeeded` | Automations Domain | The provider mutation was confirmed delivered; the one-shot effect claim was consumed exactly once. | Yes (confirmed) | Yes (immutable) | No | `delivered` |
| `Failed` | Automations Domain | Retriable local/channel failure recorded before any provider I/O (proven pre-provider). | No (proven) | No | Yes — engine resume; operator may see retry eligibility | `retriable-local-failure` |
| `Suppressed` | Automations Domain | Terminal: the one-shot effect for this (run, slot) was already claimed by another operation (webhook redelivery race). This slot performed no provider call. | Not by this slot (possibly by the other claimant, whose own effect claim governs it) | Yes | No | `suppressed` |
| `Uncertain` | Automations Domain | Terminal: an attempt began but the provider outcome is unknown; never re-attempted. | May have occurred (unprovable either way) | Yes | Disposition-only | `delivery-uncertain` |
| `TerminalFailed` | Automations Domain | Terminal: the provider/effect failed deterministically; never re-attempted. | Not provable from the slot alone | Yes | Disposition-only | `deterministic-terminal-failure` |
| `Attempting` | Automations Domain | Durable in-flight marker persisted before any non-repeatable provider mutation (M13-012). After it exists, a crash/restart recovers the slot as `Uncertain` — never another attempt. | May have occurred (treat as may-have-executed; fail closed) | No | Acknowledge only; never retry, never cancel | `external-attempt-in-progress` |
| `Scheduled` | Automations Domain | Durably accepted for later execution (delayed follow-up); settled by the follow-up handler at due time. | Not from this slot — authority is the correlated scheduled-work row | No | Only with scheduled-work authority (§3.2) | `scheduled-for-later` |
| `ContinuationStarted` | Automations Domain | Durably started a continuation outliving the dispatch (reveal flow awaiting user interaction); the continuation store is authoritative; never re-dispatched. | Possibly by the continuation, not by this slot | No | Only with continuation authority (§3.2) | `continuation-started` |
| (unknown value) | — | Unrecognized future value. | Unknown | Fail-closed | No | `unknown-unsupported` |

**Slot-transition authority.** Slot states change only through the Automations domain methods
(`RecordAttempt`, `RecordScheduled`, `RecordContinuationStarted`, `RecordSuccess`,
`RecordFailure`, `RecordTerminal`, `ReopenForRetry`) under its mutability rules. The operator
contract exposes these states; it never writes them.

### 1.3 Scheduled-work states (`ScheduledWorkStatus`, owner: BuildingBlocks.Application.Scheduling)

The operator-visible scheduled-work projection is `OperatorScheduledWorkState`, derived by
`AutomationOperationsPolicy.ClassifyScheduledWork(item, observedAtUtc)` from the actual row plus
observation time:

| Operator projection | Derived from | Meaning | Provider mutation may have occurred? | Terminal? | Operator action meaningful? | Safe exposed status |
|---|---|---|---|---|---|---|
| `Pending` | `Pending` with `Attempts == 0` | Durable, awaiting due time; the handler never ran. | No (proven) | No | Cancellable only while not claimed (§3.2) | `pending` |
| `Retrying` | `Pending` with `Attempts > 0` | Handler returned `Retryable`; rescheduled with backoff. Each attempt is a fresh handler execution with its own effect claim; the same mutation is never blindly re-issued. | Possibly by earlier attempts; each attempt is separately claimed | No | Cancellable only while not claimed (§3.2) | `retrying` |
| `Claimed` | `Claimed` with unexpired lease | One worker holds the lease and may be executing. | Possibly, right now | No | None (execution in flight) | `claimed` |
| `LeaseExpiredReclaimable` | `Claimed` with `LeaseExpiresAtUtc <= observedAtUtc` | The owning worker crashed or stalled; the store will reclaim it. | Possibly (unknown) | No | None — reclamation is store-owned, not operator-owned | `lease-expired-reclaimable` |
| `Succeeded` | `Succeeded` | Handler reported success. | Yes (by that handler execution's own claim) | Yes | No | `succeeded` |
| `PermanentFailed` | `Failed` | Handler reported permanent failure; terminal, kept for inspection. | Not provable generically | Yes | Disposition/inspect only, with scheduled-work authority | `permanent-failed` |
| `DeadLettered` | `DeadLettered` | Retries exhausted or record unroutable; terminal, kept for inspection. | Possibly by earlier attempts | Yes | Inspect; resolution follows the disposition rules (§4) | `dead-lettered` |
| `Cancelled` | `Cancelled` | Operator-cancelled; never claimed again. | Not by future runs of this record | Yes | No | `cancelled` |
| `UnknownUnsupported` | anything else | Unrecognized future value. | Unknown | Fail-closed | No | `unknown-unsupported` |

**Join rule (never project a slot alone).** A `Scheduled` automation slot must never be presented
with the scheduled-work row's state implied. The two stores are joined only through their exact
correlation identifiers (§5), and only the scheduled-work row decides whether pending work may be
cancelled.

---

## 2. Provider-effect safety model

### 2.1 Where external-attempt certainty becomes irreversible

1. Before dispatching any non-repeatable provider mutation, the engine persists the durable
   `Attempting` marker for the slot (M13-012). From that instant, certainty is lost: a crash,
   restart, timeout, or 5xx can never prove the mutation did not happen.
2. If the provider outcome is confirmed, the slot becomes `Succeeded` (with safe provider
   reference ids where policy permits, §5) or `TerminalFailed` (deterministic failure code).
3. If the outcome cannot be proven, recovery classifies the slot as `Uncertain` — terminal,
   never re-attempted. The M13-009 one-shot effect claim makes any second dispatch impossible at
   the persistence layer regardless of what an operator requests.
4. Scheduled and continuation effects delegate their certainty to their own authorities
   (`IScheduledWorkStore` leases/verdicts; the reveal continuation store). A slot never speaks for
   an authority it does not own.

### 2.2 Consequence for operators

- No operator action may cause a second provider mutation for an effect whose first attempt is
  `Attempting`, `Uncertain`, or otherwise unproven. This holds even if a human confirms the
  provider "probably" did not receive it; confirmation is recorded as a disposition (§4), never as
  a resend.
- The one-shot effect claims and idempotency keys of the existing engine remain the hard technical
  backstop; this contract adds no bypass.

---

## 3. Recovery/action matrix

### 3.1 Allowed operator actions per action-slot state

`OperatorRecoveryAction` is deliberately closed. There is no generic Retry, Resend, or ForceSend
verb; retry exists only as `RetrySafeLocalFailure` and is offered exclusively for `Failed` slots.

| Slot state | Inspect (read) | Acknowledge | Resolve-without-resend | Confirm externally delivered | Confirm externally not delivered | Cancel pending work | Retry (safe local) | Resend original effect |
|---|---|---|---|---|---|---|---|---|
| `Pending` | yes | no | no | no | no | no (engine-owned) | no | **forbidden** |
| `Succeeded` | yes | no | no | no | no | no | no | **forbidden** |
| `Failed` | yes | yes | no | no | no | no | **yes** | **forbidden** |
| `Suppressed` | yes | yes | yes | no | no | no | no | **forbidden** |
| `Uncertain` | yes | yes | yes | yes | yes | no | no | **forbidden** |
| `TerminalFailed` | yes | yes | yes | no | no | no | no | **forbidden** |
| `Attempting` | yes | yes | no | no | no | no | no | **forbidden** |
| `Scheduled` | yes (join required) | no | no | no | no | conditional (§3.2) | no | **forbidden** |
| `ContinuationStarted` | yes | no | no | no | no | no | no | **forbidden** |
| unknown value | fail-closed | no | no | no | no | no | no | **forbidden** |

### 3.2 Scheduled-work authority conditions

`CanCancelPendingWork(actionStatus, scheduledWorkState)` is true only when **both** hold:

- the automation slot is `Scheduled`, and
- the exactly-correlated scheduled-work row projects as `Pending` or `Retrying`
  (i.e. not currently claimed; lease-expired rows are store-reclaimed, not operator-cancelled).

A slot alone never authorizes cancellation, and a scheduled-work row never authorizes mutation of
the run or slot.

### 3.3 Forbidden actions (absolute)

- Resending, re-dispatching, or force-sending an `Attempting`, `Uncertain`, `Succeeded`,
  `Suppressed`, or `TerminalFailed` effect — for any reason, including operator request.
- Retrying anything except a `Failed` (proven pre-provider) slot.
- Any write into an immutable run (`Completed`, `Refused`, `Finished`) or a terminal slot.
- Cancelling work from a slot projection without the correlated scheduled-work row; cancelling
  `Claimed` or `LeaseExpiredReclaimable` work.
- Any operator path that bypasses the engine's effect claims or idempotency keys.

---

## 4. Retry classification and disposition semantics

### 4.1 Safe retry

Allowed only when Qasedak can **prove** the provider mutation has not started:

- slot is `Failed` (local/channel failure recorded before provider I/O);
- retry preserves the existing engine guarantees: strict-index execution, one-shot effect claims,
  idempotency keys, and PostgreSQL as the concurrency authority (no transaction spans provider
  HTTP);
- retry is expressed as `RetrySafeLocalFailure` and resumes through the engine's own
  `ReopenForRetry` mechanics — never as a raw re-send of the historical effect.

### 4.2 Unsafe/ambiguous retry — forbidden

Forbidden whenever the previous attempt may have reached the provider: `Attempting`, `Uncertain`,
and any unknown/fail-closed classification. A future operator-initiated *new business action*
(deliberately sending a fresh message rather than retrying the historical effect) must be a
separately designed M14+ feature with its own effect claim; it must not silently masquerade as
retrying the historical effect and is **not** implemented in M14-001.

### 4.3 Disposition semantics (future M14-004; frozen here)

Dispositions are the operator verbs that never rewrite execution truth:
`Acknowledge`, `ResolveWithoutResend`, `ConfirmExternallyDelivered`,
`ConfirmExternallyNotDelivered` (plus `CancelPendingWork` and `RetrySafeLocalFailure`, which are
operational rather than disposition verbs).

- A disposition records an operator judgment (e.g. "provider confirmed delivery"), stored
  additively next to the immutable execution record.
- Dispositions are idempotent per target: replaying the same disposition is a no-op; a different
  disposition on an already-dispositioned target is a `Conflict`; if the underlying action
  revision (status + timestamps) has changed since the operator loaded it, the outcome is
  `StaleTarget` and the request fails closed. (`EvaluateDispositionConcurrency`.)
- Dispositions never transition `Attempting`/`Uncertain` into "delivered" in the execution store;
  they annotate operator truth beside it.

---

## 5. Correlation identifiers and field exposure

`AutomationOperationsField` / `OperatorFieldExposure` classify every field the future operations
surface may touch. Unknown future fields are `UnknownFailClosed`.

| Exposure class | Fields | Rule |
|---|---|---|
| `SafeList` (may appear in list views) | `AutomationId`, `AutomationRunId`, `AutomationVersionNumber`, `ActionIndex`, `FailureCode` | Stable, low-sensitivity correlation data |
| `SafeDetailOnly` (detail views only) | `WorkspaceId`, `ChannelAccountId`, `ScheduledWorkId` | Workspace/account-scoped correlation; fine in detail, unnecessary in lists |
| `InternalOnly` (never operator-facing; correlation keys for server-side joins only) | `TriggerEventId`, `IdempotencyKey`, `ProviderRecipientId`, `ProviderMessageId` | Internal identifiers; provider ids may be joined server-side but are not presented as operator data |
| `NeverExpose` | `AccessToken`, `RawProviderPayload`, `CustomerMessageBody`, `RawScheduledPayload`, `ProviderErrorBody` | Secrets, raw provider payloads, arbitrary customer message/comment text, raw provider errors — absolutely excluded from operator surfaces |

Additional rules:

- No token, App Secret, OAuth state, raw provider cursor, or provider error prose may appear in any
  M14 operations API response, log surfaced to operators, or diagnostic payload.
- Customer message/comment text is never copied into operator diagnostics; operators inspect
  execution mechanics, not conversation content.
- `ProviderMessageId`/`ProviderRecipientId` may later be promoted to `SafeDetailOnly` by a future
  contract revision with explicit justification; the default is internal-only (fail-closed).

---

## 6. Failure taxonomy

- The only operator-facing failure text is the **stable failure code** already persisted on the
  slot (`AutomationActionExecution.FailureCode`) and the stable scheduled-work
  `LastFailureCode` / `WorkOutcome` codes (e.g. `scheduledwork.leaseLost`,
  `scheduledwork.unknownWorkType`, `scheduledwork.secretMaterial`).
- Failure codes are opaque strings for operators: they correlate and categorize; they never
  control retryability. Retryability comes exclusively from durable state
  (`DescribeFailure` derives category/retryability/certainty from the slot status, never from code
  text — pinned by `FailureCodeIsOpaqueAndNeverControlsRetryability`).
- Low-cardinality operator categories are fixed by `OperatorFailureCategory`: `None`,
  `LocalPreProviderFailure`, `Suppressed`, `AmbiguousExternalOutcome`,
  `DeterministicTerminalFailure`, `ScheduledInfrastructure`, `ContinuationOwned`, `Unknown`.
- Unknown future failure codes are handled fail-closed: they map to `Unknown` category with the
  state-derived retryability, and are never rendered as provider prose.
- Raw exception messages, stack traces, HTTP bodies, and Meta error payloads are never part of the
  operator contract.

---

## 7. Authorization contract

All future M14 run/effect/history/disposition operations must obey, minimally:

1. **Workspace scoping first.** Every query and mutation is parameterized by an authenticated
   workspace the caller is a member of; the run, automation, and connected account must all belong
   to that workspace. Cross-workspace identifiers must not reveal existence or details
   (repository-convention 404/403 semantics; no existence oracle).
2. **Privilege model.** `WorkspaceMember` may view summaries and details
   (`ViewSummary`, `ViewDetail`); resolving dispositions (`ResolveDisposition`) requires
   `WorkspaceOperator`. Privilege `None` (unauthenticated/foreign) yields nothing. Exact
   role-to-privilege mapping follows the existing workspace-membership authorization patterns in
   the codebase.
3. **Exact-account authorization before provider-adjacent data.** Where a connected account's
   state or any provider access is involved, the exact `ConnectedAccountId` must be authorized
   within the workspace before any secret resolution or provider interaction — preserving M13's
   exact-account guarantee. Operator surfaces must not trigger provider calls merely by rendering;
   capability/status must come from Qasedak-persisted state.
4. **No privilege escalation through diagnostics.** Field exposure (§5) applies identically at
   every privilege level; a member viewing detail sees the same redaction as an operator.
5. **Concurrency and idempotency are server-owned.** Disposition outcomes
   (`Create` / `IdempotentReplay` / `Conflict` / `StaleTarget`) are decided by the server from
   durable state, never by the client.

---

## 8. Executable pinning

The contract is enforced by `AutomationOperationsPolicyTests` (unit, deterministic, no I/O):

- every persisted `AutomationRunStatus` and `AutomationActionStatus` value maps to a stable
  operator classification, and unknown values fail closed;
- `Attempting` and `Uncertain` can never offer retry/resend/cancel;
- only `Failed` permits (manual or automatic) safe-local retry — proven pre-provider;
- every policy has `BlindResendAllowed == false`, and the vocabulary contains no generic
  Retry/Resend/ForceSend verb;
- scheduled-work projection uses the actual row plus observation time (lease expiry, attempts);
  a `Scheduled` slot alone never yields cancellation eligibility;
- failure codes are opaque and never change retryability;
- field exposure is closed and redacts token/provider-payload/customer-text material;
- recovery targets pin workspace, run, action index, automation version, and the exact action
  revision tuple (status + timestamps) for stale-disposition detection;
- disposition concurrency is idempotent, conflict-safe, stale-aware, and refuses to treat
  operational actions as disposition replays;
- privileges: members read, only operators resolve, `None` gets nothing, unknown access fails
  closed.

Future M14-002..008 consumers must consume the policy (or its serialized projection) rather than
re-deriving semantics from enum names.
