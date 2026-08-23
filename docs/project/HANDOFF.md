# Current handoff

## Where we are

Milestones M00–M05 complete; **M06 — Automations Engine v1 complete** (all five tasks implemented, finalized and verified). 248 backend tests green (38 Automations unit incl. deterministic evaluator + idempotent orchestration, 5 Automations PostgreSQL integration, 23 API integration incl. the full comment→DM flow). Frontend unchanged this milestone; `verify.py --full` green. Penpot remains the canonical visual source per `docs/design/PENPOT-SYNC.md`.

## Completed — M06 summary

- **M06-001:** Automation aggregate — channel-neutral Domain (`AutomationDefinition` with `CommentCreated` trigger + keyword filters, AND-composed text/sender conditions, ordered `SendDirectMessage` actions; stable rule codes via `AutomationsDomainException`), `Automation` lifecycle Draft→Active→Disabled with immutable version history (frozen-on-activate, draft replace-in-place, terminal disable preserving readable history, `FromState` rehydration); 15 unit tests.
- **M06-002:** Versioned persistence — `automations` schema (`automations` + immutable `automation_versions` rows keyed `(AutomationId, Number)`, module-owned JSON with string enums); `EfAutomationRepository` upsert semantics (every mutation flows through the aggregate; identity-map lesson: merge children in place, never Clear+re-add tracked keys); design-time factory, migration `InitialAutomationsCreation`, fixture provisions the 4th connection string; real-PostgreSQL round-trip tests (version-history fidelity, frozen-v1 stability across reloads, workspace listing).
- **M06-003:** Deterministic evaluator — pure function of (definition, TriggerContext): kind equality gate, ANY-of case-insensitive keyword filters (empty = match-all, null-text rejection), AND conditions (`Contains` case-insensitive substring, `Equals` trim-then-ordinal over `CommentText`/`SenderId`), declaration-ordered actions on match, structured non-match reasons; 13 tests incl. 25× repeat-call determinism.
- **M06-004:** Idempotent execution — `AutomationRun` ledger aggregate: one run per (automationId, triggerEventId) pinned to the frozen version number, fixed action slots Pending/Succeeded/Failed; succeeded slots never re-dispatched; closed runs immutable. `ExecuteAutomationUseCase`: active-only gate → evaluation (non-matches touch nothing) → ledger probe short-circuits redeliveries → unique-index races map to `AlreadyProcessed` (SQLSTATE 23505) → persist-per-slot crash resumption across process boundaries → stale-version refusal. Migration `AddAutomationRuns`; 7 unit + 2 PostgreSQL concurrency/resumption tests.
- **M06-005:** Comment→DM flow — composition-root `AutomationCommentBridge` (workspace resolution, active automations, executor invocation), `AutomationChannelDispatcher` binding the neutral dispatcher port to the outbound gateway (24h window stays enforced there → stable `instagram.windowExpired` slot failures), `FanOutIntegrationEventDispatcher` composing Conversations projection + Automations as the single module-visible dispatcher; normalizer extracts commenter `value.from.id`; e2e tests prove exactly-one-DM under redelivery, no-trace non-matches, window-expired failure codes, disabled refusal. CI-safe recording stand-in replaces live Meta messaging calls in tests.

## Next task — M07-001

1. `python scripts/agent_preflight.py --task M07-001`; refresh graph (`graphify . --update --no-viz --code-only`).
2. Bounded graphify query on contact/identity modeling seams (which modules already hold participant identities: Conversations participants, Instagram sender ids); record evidence.
3. Model workspace contact identity per TASKS.md invariants; keep Domain transport-free, timestamps as parameters.
4. Gates: build/format/test green; evidence; state files; finalize; continue M07.

Suggested commits: M06 per-task messages ended with `feat(automations): add comment to dm automation flow`; milestone roll-up commit is `feat(automations): deliver automation engine v1`.
