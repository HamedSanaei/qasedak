# M12-003 — Dashboard overview Penpot sync

## Source and approval

- Human-designated URL file: `c269caa0-e456-818c-8008-85a77340be64`
- Live file name: `New File 1`
- Live file revision: `281`
- Page: `Directam Admin Dashboard` (`f6b8d46f-5deb-801d-8008-85ad249c0ba1`)
- Board: `Dashboard — Directam Reference` (`f6b8d46f-5deb-801d-8008-85ad24c7b3f3`)
- Board size: `1440 × 1800`
- Approval: approved by the human's explicit designation of this exact page for the dashboard redesign.

The board and its descendant text/shapes were read live through the official Penpot MCP. A PNG export of the complete board was also inspected. Penpot exposed 51 pages in the file at the time of sync.

## Extracted visual contract

- Three 1148 × 60 status rows at the top of the content area, 12px radius, 38px icon controls.
- Status colors: tip `#F4E2F0` / `#E9B9DD`; warning `#FFF4E9` / `#F3DFC8` / `#F47B20`; danger `#FBEDEE` / `#F0D0D3` / `#E72D3D`; secondary violet `#9B21D1`.
- Section title 20px/700, description 13px/400, primary heading color `#514D5E`, secondary copy `#7D7887`.
- Feature cards: two-column desktop grid, 220px minimum height, white surface, `#E3E5E8` border, 14px radius, 44px brand icon, 30px accent-soft tag.
- Last feature card spans the full desktop content width.
- Lower card row: three columns, 206px minimum height, 54px circular icons and accent-soft pill actions.
- The Penpot board defines desktop 1440 only. The 390px layout is a derived responsive adaptation and is not presented as a Penpot-authored target.

## Behavior-preserving adaptations

- Directam naming became Qasedak naming.
- Static connection/subscription claims were not copied because this overview does not receive authoritative connection or entitlement data.
- The three Penpot status rows now expose real onboarding, Workspace and Inbox states. Existing `workspaceState` and `inboxState` behavior remains the source of copy and error tones.
- The seven Penpot feature cards point only to routes that already exist in Qasedak. Shipped automation cards retain the `شروع` action; compatibility routes that currently render an unavailable-state screen use the honest `مشاهده وضعیت` action.
- The reference social cards became Qasedak quick-access cards for Inbox, Accounts and Help. No external social URL was invented.
- The existing authenticated `DashboardShell` remains unchanged; only the `/dashboard` content composition was redesigned.

## Next.js paths

- `frontend/Qasedak.Web/src/features/dashboard/DashboardOverview.tsx`
- `frontend/Qasedak.Web/src/features/dashboard/DashboardOverview.module.css`
- `frontend/Qasedak.Web/src/app/dashboard/page.tsx`
- `frontend/Qasedak.Web/src/app/globals.css`
- `frontend/Qasedak.Web/tests/frontend-experience.test.mjs`
- `frontend/Qasedak.Web/design/penpot-sync.json`

## Verification evidence

- `npm test -- --test-name-pattern="dashboard|Penpot"` — passed, 58/58.
- `npm run lint` — passed with zero warnings.
- `npm run verify` — passed lint, typecheck, 58/58 tests and production build.
- `python scripts/validate_penpot_sync.py` — passed 6/6.
- `python scripts/check_architecture.py` — passed (35 projects, 6 business modules).
- `python scripts/agent_finalize.py --task M12-003` — passed.
- `python scripts/verify.py --full` — static gates, restore, format and Release build passed with 0 warnings/errors; 380 unit tests plus 3 non-container API tests passed. PostgreSQL Testcontainers could not connect to the unavailable local Docker endpoint `npipe://./pipe/docker_engine`, so the Docker-dependent remainder is not represented as passing.

## Open differences

- Penpot has no mobile dashboard board, so mobile spacing is derived from the existing Qasedak breakpoint contract.
- Directam social-network destinations are intentionally omitted until Qasedak-owned URLs are approved.
