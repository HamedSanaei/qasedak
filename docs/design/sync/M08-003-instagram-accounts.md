# M08-003 — Instagram account management UI (live-synced)

**Date:** 2026-08-24 · **Canonical file:** `c269caa0-e456-818c-8008-85a77340be64`

## Live Penpot inspection (stable UUID addressing, no human navigation)

- **`Connect to Instagram — Desktop`** `f5bf3c2c-b970-8002-8008-874ac4b51953` on page
  `Connect to Instagram` (`f5bf3c2c-b970-8002-8008-874ac4aa747b`) — breadcrumb 13/400
  `#88828E`, page title 24/800, connect card (heading 22/800 `#141414`, body 14/400
  `#737373`, three feature rows with accentSoft glyph chips ✓/?/↗), method chooser
  ("روش اتصال را انتخاب کنید" 16/700; primary accent button «اتصال با اینستاگرام»;
  outline Facebook button text `#1877F2`; legal note 12/400 muted).
- **`Profile — Connected Accounts`** `f5bf3c2c-b970-8002-8008-874a8c53c34c` — account rows
  on surface-subtle with r-card radius, name 14/600 + status chip 12/700 accent-on-accentSoft,
  secondary "افزودن حساب جدید" action.

## Backend surface added (minimal HTTP glue over tested use cases)

The Instagram module had **no HTTP endpoints** for connections. Added
`ConnectionEndpoints` (`Infrastructure/Endpoints/`), workspace-scoped under the existing
`workspace-member` policy, mirroring the ConversationEndpoints pattern:

| Route | Behavior |
| --- | --- |
| `GET /api/v1/workspaces/{id}/instagram/connections?includeDisconnected=` | `ListWorkspaceConnectionsUseCase`; token values never leave the server |
| `GET /api/v1/workspaces/{id}/instagram/authorize-url?redirectUri=` | `IAuthorizationUrlBuilder`; server-generated anti-CSRF state |
| `POST /api/v1/workspaces/{id}/instagram/connections` | `ConnectInstagramAccountUseCase` (Business Login code exchange) |
| `DELETE /api/v1/workspaces/{id}/instagram/connections/{accountId}` | `DisconnectInstagramAccountUseCase` |

Failure mapping pinned by unit tests (`ConnectionsFailureMapperTests`, +6 tests →
Instagram suite 80/80). Solution builds clean in Release.

## Frontend

- `/dashboard/settings/instagram`: connect state ↔ connected-list state; health pills for
  all backend `AccountHealth` values (سالم/نزدیک انقضا/توکن منقضی/دسترسی لغو شده/ناسالم/
  قطع شده) via pure mapper `src/features/instagram/health.ts` (unknown values fail closed);
  reconnect affordance for Expired/ExpiringSoon/Revoked; disconnect with busy state;
  loading/error states with Persian copy for every stable failure code.
- `src/shared/api/connections.ts`: typed client over the new endpoints.

## Tests/gates

Frontend 22/22 (+4: health mapping parity, unknown-health fail-closed, failure-copy
coverage, endpoint contract w/ injected transport); typecheck pass; manifest validator
pass; architecture check pass.
