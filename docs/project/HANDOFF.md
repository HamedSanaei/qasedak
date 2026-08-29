# Current handoff

## Where we are

**M12-002 is DONE (2026-08-29): live inbox thread context panel.** M12-001 (inbox
search) shipped 2026-08-28. The next actionable task is **M12-003 — Workspace dashboard
overview**, which stays blocked until an approved Qasedak-native dashboard design exists
(`reference surveyed; pending sync` in SCREEN-INVENTORY.md; Penpot sync contract forbids
inventing content). Changes for M12-002 are in the working tree, not committed.

## What this run delivered (M12-002)

### Backend
- `GET /api/v1/workspaces/{workspaceId}/contacts/by-identity?channel=…&identity=…`
  (`IContactQueries.FindByIdentityAsync` in `EfContactQueries`): returns the contact bound
  to a provider identity, resolving any `MergedIntoId` chain to the absorbing primary;
  404/`contact.notFound` when none. Same `ContactPayload` shape as the by-id endpoint
  (factored out). New e2e `ContactResolvesByProviderIdentityAndReturnsCrmSurface`.

### Frontend
- `src/shared/api/contacts.ts` — by-identity resolve (404 → `null`) + add/remove tag +
  add note. `src/features/contacts/presentation.ts` — copy + tag/note bounds.
- `[conversationId]/page.tsx` — the «اطلاعات گفتگو» panel now shows the contact's display
  name, removable tag chips + add-tag, and a notes timeline + add-note; a neutral empty
  state covers conversations whose CRM contact isn't materialized yet. The design's
  «غیرفعال» badge and the «تا تکمیل M07» warning are removed (M07 shipped).

### Sync evidence
- penpot-sync `inbox.conversations` notes updated, SCREEN-INVENTORY row updated, and a
  sync record `docs/design/sync/M12-002-thread-context-panel.md` written. Honest note: no
  fresh Penpot MCP read this session (MCP client unavailable) — reconciled against the
  extracted 2026-08-24 contract; `penpotRevision` stays `null`.

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

M12-002 is DONE; next actionable task is **M12-003 — Workspace dashboard overview**
(TODO in TASKS.md) but it is BLOCKED until a Qasedak-native dashboard design is approved
(`reference surveyed; pending sync`). Do not commit/push/tag unless explicitly asked;
suggested commits are recorded per task in TASKS.md.
