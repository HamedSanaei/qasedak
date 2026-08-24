# M08-001 — Penpot design foundation (live sync)

**Date:** 2026-08-24
**Canonical file:** `c269caa0-e456-818c-8008-85a77340be64` (verified via live
`penpot.currentFile.id`; display name ignored per contract).

## Live inspection

| Board | Page | Stable ID |
|---|---|---|
| Navigation / Sidebar | Directam — Global Navigation Components (`f5bf3c2c-b970-8002-8008-8752c5573aef`) | `f5bf3c2c-b970-8002-8008-8752c6768b24` |
| Global Navigation — Documentation | same page | `f5bf3c2c-b970-8002-8008-8752c56cf17a` |
| Navigation / User Menu | same page | `f5bf3c2c-b970-8002-8008-8753db0115dd` |
| Dashboard — Directam Reference | Directam Admin Dashboard (`f6b8d46f-5deb-801d-8008-85ad249c0ba1`) | `f6b8d46f-5deb-801d-8008-85ad24c7b3f3` |
| Smart Answering — Component States | same page | `f5bf3c2c-b970-8002-8008-8747843b4ad6` |

Pages were resolved by stable UUID via `getPageById`; the active page did not need to be
the target during inspection (board content was read through shape traversal).

## Extracted values now codified

- **Colors:** ink `#141414`, heading plum `#2A0020`, soft accent surfaces `#FCEEF6` /
  `#FFEBF9`, input border `#C8C6CC`, placeholder `#AAA6B0`, status success `#0D9F00`,
  warning `#F47B20`, danger `#D32F2F`, error `#E72D3D`, violet accent `#9B21D1`.
- **Typography scale observed live:** 30/800 page title, 22/800 brand, 20/800 section,
  18/700 panel, 17/700 card, 16/500–700, 15/500–700, 14/500 nav + input, 13/400–700,
  12/400–800 labels/sub-items, 11/400 micro, 10/400 hints. Family Vazirmatn.
- **Radii:** card 12, controls 8, chips 4–6.
- **Elevation:** user-menu spec "Menu shadow: 0 4px 4px #0000004D" → `--shadow-menu`.
- **Icons:** all 14 sidebar icon paths extracted verbatim (M/L/C/Z only), normalized to
  their 10/20/24 boxes → `src/shared/design/SidebarIcon.tsx`. Resolves the M05-005
  deferred item "icon/SVG extraction".

## Deferred M05-005 items

1. **Icon/SVG extraction** — resolved (see above).
2. **Sidebar active-state treatment** — Penpot defines NO explicit active/hover row
   anywhere in the navigation boards or the documentation board. Per
   PENPOT-SYNC.md no values were invented: implementation uses only existing tokens
   (brand-accent label/icon + weight 800) and records an explicit OPEN QUESTION in
   `Sidebar.module.css` plus a regression test pinning that annotation until sign-off.

## Implemented

- Extended token block in `src/app/globals.css` (each line carries its Penpot origin).
- Reusable presentation primitives in `src/shared/design/ui/*`:
  Button (primary/secondary/outline/danger × sizes), Card, TextField, TextAreaField
  (with counter/error shell), SelectField, StatusPill (success/warning/danger/info/
  neutral), PageHeader.
- Sidebar wired to real extracted icons; nav→icon mapping stays application-owned in
  `src/app/dashboard/layout.tsx`.

## Manifest changes

- New mapping `design-system.foundation` (provisional) → board above; tokens extended;
  sidebar mapping re-stamped with today's live inspection note.

## Verification

- `npm run typecheck` pass; `npm test` **12/12 pass** (4 new design-system tests).
- `python scripts/validate_penpot_sync.py` pass (run after manifest edit).
