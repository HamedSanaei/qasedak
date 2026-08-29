# M12-001 — Server-side inbox search (sync evidence)

**Date:** 2026-08-28 · **Source design:** canonical file
`c269caa0-e456-818c-8008-85a77340be64`, board "Conversations / Inbox / Desktop"
(`c48311ed-…-88200ed6b9fc`), page `c48311ed-e700-80f8-8008-88200ec40bf3`
(extracted 2026-08-24 in `docs/design/sync/2026-08-24-qasedak-final-designs.md`).

## What the approved design says

The list-panel search input is **disabled by design** (bg `#f6f7f9` r10, placeholder
«جستجو — پس از تکمیل query backend», warning badge «فعلاً غیرفعال»). The design
explicitly defers enabling it until the backend ships a search query capability — that
capability now exists, so the disabled state is retired without inventing new design
values.

## Divergence (enabled state)

The design defines only the disabled state, so the enabled state is a documented
extension:

1. Placeholder becomes «جستجو در گفتگوها…» (the design's own text minus the
   "pending backend" note), the «فعلاً غیرفعال» warning badge is removed, and the input
   is live with a 250 ms debounce.
2. Empty-state copy distinguishes a no-results search («گفتگویی با این عبارت پیدا نشد.»)
   from an empty inbox (original design copy preserved for the empty-inbox case).

## Backend capability shipped (M12-001)

| Concern | Implementation |
| --- | --- |
| Query surface | optional `search` query param on `GET /api/v1/workspaces/{id}/conversations`; blank = no filter; composes with `status` + paging |
| Semantics | case-insensitive contains over counterpart identity (`ParticipantId`) or any message body (`EF.Functions.ILike`, EXISTS translation) |
| Safety | `SearchPattern` trims and escapes `%` / `_` / `\` so user input never acts as a LIKE wildcard (bare `%` matches nothing) |
| Tests | 8 `InboxSearchTests` unit cases + `InboxListSupportsCaseInsensitiveSearchAcrossParticipantAndBodies` API e2e (queued for Docker) |

## Frontend

- `conversationsApi().list` forwards `search` (URLSearchParams-encoded; blank omitted).
- `/dashboard/inbox` search input is live; the warning badge is removed.
- Client contract tests updated in `tests/inbox.test.mjs`.

## Residual

Testcontainers API e2e (including the new search scenario) was NOT executed this session
— the Docker daemon was not running. The suite ran green previously (458 tests at the
M09-002 freeze) and the new test compiles into the Release build; it must run once Docker
is up.
