# M08-002 — Authentication & workspace UI (live-synced)

**Date:** 2026-08-24 · **Canonical file:** `c269caa0-e456-818c-8008-85a77340be64`

## Live Penpot inspection

- Board **`Auth / Login / Desktop`** (`324404a7-ad1e-8048-8008-8776b27352cb`) and
  **`Auth / OTP / Mobile`** (`324404a7-ad1e-8048-8008-8776b3100eb1`) on page
  `GetCode · 05 Auth & Checkout` (`324404a7-ad1e-8048-8008-8772682a1c67`), resolved by
  stable UUID via `getPageById` — no human page switching.
- **Divergence recorded (approval = draft):** these are GetCode-branded OTP/phone
  screens (cyan `#13A9D4`, phone credentials). Qasedak's backend is email+password
  (`POST /api/v1/identity/register|login`, bearer sessions) with no OTP endpoints, and
  no Qasedak-branded auth board exists anywhere in the file. Implementation therefore
  uses the approved Qasedak token set (M08-001 foundation); the GetCode boards serve as
  structural reference only. A human-approved Qasedak auth design is required before
  the mapping can leave `draft`.

## Backend contracts consumed (application-owned client)

`src/shared/api/http.ts` + `identity.ts`: register → login → `/me`, workspace creation,
member listing; bearer token from `POST /identity/login` stored in localStorage with
expiry check; stable failure codes surfaced verbatim (`auth.*`, `workspace.invalidName`).
Server authorization untouched; no fake auth behavior added.

## Implemented

- `/login` — email/password form, field-level validation mirroring backend rules,
  Persian error copy for every stable failure code, submitting state.
- `/register` — display name/email/password/**workspace name** in one step:
  register → login → `POST /api/v1/workspaces` → dashboard redirect.
- Session helpers (`saveSession/readSession/clearSession`, workspace id persistence).

## Known gaps (documented, not silently dropped)

1. No backend "list my workspaces" endpoint ⇒ no workspace *selection* UI possible;
   creation-only flow implemented.
2. Auth visual treatment pending a human-approved Qasedak design (draft mapping).

## Tests

`tests/auth.test.mjs` (validation parity incl. PasswordPolicy 10..128 + non-alphanumeric;
every failure code translated), `tests/identity-api.test.mjs` (login request shape,
failure-code propagation, bearer header on workspace endpoints — injected transport).
Frontend suite: **18/18 pass**; typecheck pass; manifest validator pass.
