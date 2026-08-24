# Current handoff

## Where we are

**M08 is complete (2026-08-24).** M08-001..005 were executed end-to-end from the local
working tree against the verified canonical Penpot file
`c269caa0-e456-818c-8008-85a77340be64`, per the human resume directive. All roadmap
milestones are now closed: M00–M08 fully delivered; **M09 and M10 complete**
(M09-002 remains BLOCKED on a provider-selection ADR); **M11 release baseline prepared**
(M11-001..003 DONE).

## What M08 delivered (this run)

- **M08-001 — design foundation:** 14 sidebar icons extracted verbatim (path-data),
  extended token set (`radius.control/chip`, `elevation.menu`, `colorExtended.*`) all
  annotated with live Penpot origins, presentation-only primitives `src/shared/design/ui`,
  active-state OPEN QUESTION pinned by test. Evidence: `docs/design/sync/M08-001-design-foundation.md`.
- **M08-002 — auth/workspace:** application-owned `http.ts`/`identity.ts` clients,
  backend-mirroring validators, `/login` + `/register` (+ workspace creation). Mapping
  `identity.auth` is **draft**: the only auth boards are GetCode OTP references
  (`324404a7-…8776b27352cb`) that diverge from the email+password backend.
- **M08-003 — Instagram accounts:** approved mapping; new thin `ConnectionEndpoints`
  HTTP surface over tested use cases (tokens never leave the server); UI covers all six
  `AccountHealth` states with reconnect/disconnect flows.
- **M08-004 — inbox:** functional list/thread/reply UI on foundation tokens. The visual
  sync portion is **BLOCKED — no inbox/conversation design exists anywhere in the
  canonical file** (all 24 pages swept); no manifest mapping was fabricated.
- **M08-005 — automation builder v1:** new `AutomationEndpoints`
  (CRUD + activate/deactivate; billing denials surface verbatim), builder form + list
  synced from three boards with documented divergences (1000-char counter wins over the
  design's ۰/۲۰۰۰; post-scoping disabled in v1).

Per-task evidence: `docs/design/sync/M08-00{1..5}-*.md`; manifest:
`frontend/Qasedak.Web/design/penpot-sync.json` (validator green); screen roll-up:
`docs/design/SCREEN-INVENTORY.md`.

## Verification status

- Frontend: `npm run verify` green (lint 0 problems, tsc clean, node --test 30/30,
  production build prerenders all routes).
- Backend: solution builds Release clean; Automations unit suite 44/44 (incl. endpoint
  contract tests), Instagram unit suite 80/80.
- Gates: `validate_penpot_sync.py` PASSED, `check_architecture.py` PASSED
  (35 projects / 6 modules), `agent_finalize.py` passed for every M08 task.
- `verify.py --full` re-run at handoff time (see GRAPHIFY_EVIDENCE.md / CI log for the
  recorded result of this final pass).

## Next actions for a human

1. **Decision required — payment provider (unblocks M09-002):** choose the provider and
   record an ADR (legal fit, webhook reachability, pricing model).
2. **Design decisions to lift remaining drafts/blockers:**
   - Approve (or supply) a Qasedak-branded auth design → lifts `identity.auth` from draft.
   - Supply an inbox/conversation design in the canonical file → unblocks the BLOCKED
     visual sync of `/dashboard/inbox`.
   - Confirm the landing mapping target (`Directam Landing — Desktop` board on Page 1)
     if a public landing implementation task is ever added.

## Next task for an agent

None actionable in TASKS.md — every task is DONE except **M09-002**, which stays BLOCKED
until the payment-provider ADR lands (then implement the adapter behind the existing
billing ports). Do not commit/push/tag unless explicitly asked; suggested commits are
recorded per task in TASKS.md.
