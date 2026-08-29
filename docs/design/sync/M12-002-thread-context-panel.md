# M12-002 — Inbox thread context panel (sync evidence)

Enabling the thread's future-CRM placeholder with the real **M07 contacts surface**.

## Design source

Canonical file `c269caa0-e456-818c-8008-85a77340be64`, page
`Qasedak · Inbox & Conversations` (`c48311ed-e700-80f8-8008-88200ec40bf3`), board
`Conversations / Inbox / Desktop` (`c48311ed-e700-80f8-8008-88200ed6b9fc`). The extracted
contract records the context panel «اطلاعات گفتگو» (white r14) with a future-CRM
placeholder box (`نام مخاطب/برچسب‌ها/یادداشت‌ها` muted + badge «غیرفعال») and the warning
«Tags و Notes تا تکمیل M07 قابل ویرایش نیستند.».

> No fresh Penpot MCP read was performed this session — the MCP client is not available
> in this environment. The implementation reconciles against the already-extracted,
> approved contract in `docs/design/sync/2026-08-24-qasedak-final-designs.md`; the
> manifest keeps `penpotRevision: null` and records no new revision identifier.

## Change

The disabled placeholder (name/tags/notes muted + «غیرفعال» badge + the M07 warning) is
replaced by a **live CRM surface**, because M07 (the Contacts module) shipped:

- Backend: new read-only workspace-scoped lookup
  `GET /api/v1/workspaces/{workspaceId}/contacts/by-identity?channel=…&identity=…`
  (Contacts module) that returns the contact bound to a provider identity, resolving any
  `MergedIntoId` chain to the absorbing primary; 404/`contact.notFound` when none exists.
  Reuses the existing `IContactQueries`, `EfContactQueries`, and the same detail payload
  as the by-id endpoint. The inbox resolves a conversation's `(channel, participantId)`
  to its CRM contact through this endpoint.
- Frontend: `src/shared/api/contacts.ts` client (by-identity resolve returning `null` on
  404 + tag/note mutations) and `src/features/contacts/presentation.ts` (copy + tag/note
  validation mirroring the Contacts domain bounds). The thread page
  `[conversationId]/page.tsx` now renders the «اطلاعات گفتگو» panel with:
  - the contact's display name;
  - tag chips with remove plus an add-tag input;
  - a notes timeline plus an add-note box;
  - a neutral, non-disabled empty state when no CRM contact exists yet (the old
    «تا تکمیل M07» warning is gone — the panel is functional, just awaiting its aggregate).

## Divergences

- The approved desktop concept is a three-panel layout with a dedicated right-side context
  panel. The current thread route is single-column, so the «اطلاعات گفتگو» panel renders
  as a card in the thread column using existing design tokens (that is unchanged M08-004
  behavior, not redesigned here) — the three-panel desktop shell remains future work.
- Empty contact (auto-created on first interaction) renders an enabled panel with the
  informative empty state, replacing the design's disabled placeholder copy.

## API wiring

- `GET /workspaces/{id}/contacts/by-identity` (resolve) — 404 → `null`
- `POST /workspaces/{id}/contacts/{contactId}/tags` (`{ tag }`) / `DELETE …/tags/{tag}`
- `POST /workspaces/{id}/contacts/{contactId}/notes` (`{ body }`)

## Tests / gates

- Backend: `dotnet build -c Release` 0 warnings/0 errors; new `ContactEndpointTests.ContactResolvesByProviderIdentityAndReturnsCrmSurface` e2e (resolve → tag/note → re-resolve reflects mutations, unknown identity 404, missing params 400, foreign workspace 403) — API integration suite 3/3; Contacts integration 9/9; Contacts unit 23/23.
- Frontend: `npm run verify` green — 47 tests (added `tests/contacts.test.mjs`: client URLs/verbs + 404→null + presentation bounds/copy; M07-warning absence asserted), lint, typecheck, build.
- `python scripts/validate_penpot_sync.py` re-run and passing after the manifest edit.

## Files

- `backend/Modules/Contacts/.../ContactQueries.cs` — `IContactQueries.FindByIdentityAsync`
- `backend/Modules/Contacts/.../EfContactQueries.cs` — implementation + shared detail projection
- `backend/Modules/Contacts/.../Endpoints/ContactEndpoints.cs` — `/by-identity` route + shared `ContactPayload`
- `backend/tests/Qasedak.Api.IntegrationTests/ContactEndpointTests.cs` — new e2e
- `frontend/Qasedak.Web/src/shared/api/contacts.ts`, `src/features/contacts/presentation.ts`
- `frontend/Qasedak.Web/src/app/dashboard/inbox/[conversationId]/page.tsx`
- `frontend/Qasedak.Web/tests/contacts.test.mjs`
- `frontend/Qasedak.Web/design/penpot-sync.json`, `docs/design/SCREEN-INVENTORY.md`