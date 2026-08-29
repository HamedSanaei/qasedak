# Current handoff

## Where we are

**M12-001 is DONE (2026-08-28): server-side inbox search.** All v1 milestones (M00–M11)
were complete and committed at `4e70f1e`; a human decision started M12 (v2 Product
Features) with backend conversation search as the first task — the capability the
approved Penpot inbox design explicitly marked as pending («جستجو — پس از تکمیل query
backend», «فعلاً غیرفعال»). Nothing is committed — working tree only, per contract.

## What this run delivered (M12-001 — inbox search)

### Backend
- `SearchPattern` (Conversations.Application, `InboxQueries.cs`): trims the term and
  escapes `%` / `_` / `\` so LIKE wildcards in user input match literally; blank terms
  yield no filter.
- `EfConversationQueries.ListAsync`: applies the escaped term with `EF.Functions.ILike`
  over `ParticipantId` or any message body (`c.Messages.Any(...)`, EXISTS translation in
  PostgreSQL). No migration needed — no schema change.
- HTTP: optional `search` query param on `GET /api/v1/workspaces/{id}/conversations`;
  backward compatible, composes with `status` and paging.
- Tests: 8 new `InboxSearchTests` unit cases (Conversations suite 23/23); new API e2e
  `InboxListSupportsCaseInsensitiveSearchAcrossParticipantAndBodies` (participant match,
  case-insensitive body match, Persian terms, bare `%` → zero results, search+status
  composition, blank term = unfiltered) — ADDED but NOT RUN: Testcontainers needs the
  Docker daemon, which was down this session.

### Frontend
- `conversationsApi().list` forwards `search` (URLSearchParams encoding; blank omitted).
- `/dashboard/inbox`: search input live with 250 ms debounce, «فعلاً غیرفعال» badge
  removed, empty state distinguishes «گفتگویی با این عبارت پیدا نشد.» from the empty
  inbox copy. Contract tests updated in `tests/inbox.test.mjs`.

### Docs/state
- MILESTONES.md: M12 — v2 Product Features (retire v1 deferrals; Penpot sync contract
  applies to UI tasks).
- TASKS.md: M12-001 DONE; M12-002 (inbox thread context panel, contacts/tags/notes) and
  M12-003 (dashboard overview, blocked on approved design) TODO.
- `docs/design/sync/M12-001-inbox-search.md`: enabled-state divergence recorded (design
  only defined the disabled state; placeholder «جستجو در گفتگوها…»).
- SCREEN-INVENTORY.md inbox row updated; PROJECT_STATE.json, STATUS.md updated.

## Verification status

- Backend Release build 0 warnings/0 errors; `dotnet format --verify-no-changes` clean;
  all unit suites green: BuildingBlocks 12, Automations 44, Billing 119, Contacts 23,
  Conversations 23, Identity 79, Instagram 80 (380 total).
- Frontend `npm run verify` green: lint, typecheck, 37/37 tests, production build.
- NOT run this session (honest residual): every Testcontainers integration/e2e suite
  (API integration incl. the new search scenario, billing/contacts/automations/identity/
  instagram Postgres suites) — Docker daemon is not running. Re-run once Docker is up.
- Not re-run this session: `check_architecture.py`, `validate_penpot_sync.py` (no
  manifest change), `verify.py --full`, rehearsals (unchanged since v1 freeze).

## Next actions for a human

1. Start Docker (Desktop or engine) so the Testcontainers suites — especially the new
   M12-001 search e2e — can run.
2. Operational go-live prerequisites (unchanged, not CI): real Mellat terminal
   credentials; Shaparak registration of the deployment public host; staging smoke incl.
   deliberate cancel and duplicate replay; Zarinpal staging smoke when ready.

## Next task for an agent

M12-001 is DONE; next actionable task is **M12-002 — Enable inbox thread context panel**
(TODO in TASKS.md): replace the thread's future-CRM placeholder with the real M07
contacts surface (name/tags/notes) behind the existing workspace-scoped Contacts APIs,
and remove the now-false «Tags و Notes تا تکمیل M07 قابل ویرایش نیستند» warning with
sync evidence. Do not commit/push/tag unless explicitly asked; suggested commits are
recorded per task in TASKS.md.
