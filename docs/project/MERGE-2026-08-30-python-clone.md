# Repository consolidation — 2026-08-30

## Decision

`C:\Users\Hamed\Documents\Qasedak` is the canonical working clone. It is the clone
whose `master` and `origin/master` both point at the live GitHub repository
`HamedSanaei/qasedak` (`0cd57876b3a672fffc5b773bf7c40e2bfd00dbf9`). The older
`C:\Users\Hamed\Documents\Python\qasedak` clone tracked the same remote but was
locally behind and its push was rejected with `fetch first`.

Before consolidation, the complete older clone was copied to:

`C:\Users\Hamed\Documents\Python\qasedak-archive-20260830`

The source and archive contained 27,029 files and exactly 915,560,857 bytes each.
The archive is the recovery copy. The active duplicate clone was removed after the
verification gates; the archive remains available for recovery.

## Transferred from the older clone

- Public Directam landing page, its three WebP assets, metadata, responsive styles and
  App Router root composition.
- Server-authenticated dashboard shell, responsive sidebar/drawer, user menu, dashboard
  overview state model and account/help/onboarding/feature routes.
- Same-origin API route handlers, server backend/session adapters and bearer-header
  compatibility for the existing client API.
- Shared design primitives and icon components that were not present in the canonical
  clone, plus the subscription compatibility routes.
- M08-006/M08-007 Penpot sync records and visual-review artifacts.
- Historical ADR-008 provider-neutral payment note and older design audit records that
  were not present in the canonical clone.

## Deliberate conflict resolution

The canonical clone's newer, tested implementations remain authoritative for
automations, billing plans/checkout/result, Instagram connections, Inbox list/thread
search and CRM context, authentication forms, API clients, and their tests. The older
clone's four `features/conversations/ui/*` files were not copied because they predate
the canonical M12 search/context implementation; the complete originals remain in the
recovery archive.

The frontend manifest remains the canonical v1 contract and now includes the
human-designated `landing.main` board mapping and landing tokens. No fresh Penpot MCP
read was possible in this merge session; the imported M08-007 record and its explicit
board identity are retained, and `penpotRevision` remains `null`.

The `/api/v1/[...path]` proxy accepts the existing local-storage bearer header when no
HttpOnly cookie exists, while login/workspace responses establish the server-owned
cookies used by the new dashboard shell. Logout clears both cookie and local-storage
sessions.

## Post-delete completeness audit

After deleting the active duplicate, a path-level comparison of the recovery archive
against the canonical working tree (excluding `.git`, generated `.next`, dependency
build output and Graphify output) found exactly four source paths absent from the
canonical tree:

- `frontend/Qasedak.Web/src/features/conversations/ui/ConversationList.tsx`
- `frontend/Qasedak.Web/src/features/conversations/ui/Inbox.module.css`
- `frontend/Qasedak.Web/src/features/conversations/ui/ReplyComposer.tsx`
- `frontend/Qasedak.Web/src/features/conversations/ui/ThreadPanel.tsx`

These are the deliberately superseded pre-M12 conversation components described above;
the active Inbox list/detail routes and M12 search/context implementation are present
in the canonical tree, and the four originals remain recoverable in the archive.
