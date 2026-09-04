# M12-008 — Help center Penpot sync

## Source inspection

- Primary human-designated file: `c269caa0-e456-818c-8008-89e5136d6851` (`directam`).
- Primary pages read live through the official Penpot MCP: `Qasedak · Product UI Components`, `Qasedak · Identity & Workspace`, `Qasedak · Inbox & Conversations`, and `Qasedak · Billing & Payments`.
- Primary result: no Accounts or Help board exists. The Identity page's `Identity / Validation, Session & Workspace States` board (`c48311ed-e700-80f8-8008-881f0ea618ba`) was read for the Accounts state contract.
- Permitted legacy reference file: `c269caa0-e456-818c-8008-85a77340be64` (`New File 1`).
- Help page: `Directam — Help Center` (`f5bf3c2c-b970-8002-8008-8749d0783515`).
- Help board: `Help Center — Desktop` (`f5bf3c2c-b970-8002-8008-8749d0a39e9f`), `1440 × 1050`.
- Penpot revision: `null`; the API did not expose a revision during this live inspection.
- Inspection: complete board identity, all descendant text, geometry and a full PNG export were read through MCP.

## Visual contract retained

- White bordered search hero with a prominent support heading and search field.
- Three quick-destination cards.
- A responsive category-card grid.
- Expandable FAQ rows.
- A visually distinct final support notice.
- Existing Qasedak tokens for page/card surfaces, borders, accent-soft icon bubbles, plum notice, typography and radii.

## Behavior-preserving adaptations

- Directam naming became Qasedak naming.
- Every quick/category destination resolves to a real Qasedak route: Accounts, Inbox, Automations, Instagram or Billing.
- FAQ search is deterministic client-side filtering over the truthful static content; it does not claim a remote knowledge-base backend.
- The legacy online-chat button and support hours were not copied because the repository has no configured ticket/chat backend.
- The Smart SMS category was removed because backend inspection found no Smart SMS capability.
- No request persistence, localStorage simulation or fake success state was added.
- The Penpot board defines desktop only. The 390px layout is explicitly derived with one-column cards and compact spacing; it is not claimed as a Penpot-authored mobile board.

## Next.js paths

- `frontend/Qasedak.Web/src/features/help/HelpScreen.tsx`
- `frontend/Qasedak.Web/src/features/help/HelpScreen.module.css`
- `frontend/Qasedak.Web/src/app/dashboard/help/page.tsx`
- `frontend/Qasedak.Web/design/penpot-sync.json`
- `frontend/Qasedak.Web/tests/frontend-experience.test.mjs`

## Verification

- `npm test` — passed 60/60, skipped 0.
- `npm run typecheck` — passed.
- `npm run lint` — passed with zero warnings.
- `python scripts/validate_penpot_sync.py` — passed 6/6.
- Authenticated browser review — passed at 1440 and 390; Help search reduced six FAQs
  to the single matching payment answer and the mobile drawer closed after navigation.
- `python scripts/verify.py --full` — passed, including 471/471 backend tests with
  Testcontainers, frontend 60/60 + production build and both Docker image builds.

Full browser, Docker and repository evidence is also summarized in the M12-008 handoff.
