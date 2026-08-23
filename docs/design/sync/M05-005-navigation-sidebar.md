# Sync record — M05-005: global navigation sidebar foundation

## Source (read live through the official Penpot MCP server)

| Field | Value |
|---|---|
| Page | "Directam — Global Navigation Components" (`f5bf3c2c-b970-8002-8008-8752c5573aef`) |
| Board | "Navigation / Sidebar" (`f5bf3c2c-b970-8002-8008-8752c6768b24`, 256×1050) |
| Component | `Sidebar` (`f5bf3c2c-b970-8002-8008-8752c87448ee`, main instance of the board) |
| Inspection method | Penpot Plugin API via MCP (`shapeStructure`, fills/strokes/text-style extraction, geometry reads); PNG export of the board requested through `export_shape` |
| Penpot revision | not exposed by the connected plugin API → recorded as `null` in the manifest (never fabricated) |
| Token catalog | local library has no token sets (`TokenCatalog.sets.length === 0`), so tokens were derived from inspected layer values, not a Penpot token catalog |

Pages surveyed but **not** synchronized yet: Admin Dashboard (`f6b8d46f-…`), Connect Instagram (`f5bf3c2c-…874ac4aa747b`), Comment Automation (`f5bf3c2c-…874ebb680e25`) — board lists recorded in the manifest's `source.inspectedPages`.

## Extracted design values

- Colors: surface `#FFFFFF`, subtle `#F7F7F9`, border `#E8E9EC`, primary text `#2E2938`, nav `#514D5E`, secondary `#7D7887`, muted `#8A8592`, disabled `#737373`, accent `#BE0183`.
- Typography: Vazirmatn throughout; brand 22/800, mark 16/800, nav 14/500, sub 12/400, footer plan 12/600, footer time 11/400.
- Geometry: footer card 224×96 r=12 at x=16/y=930; nav rows on a 55px rhythm from y=88; sub items on a 36px rhythm; content inset 40–42px.
- Library assets: 3 components (`Plan Selector` variant, `Sidebar`, `User Menu` variant); sidebar icon set exists as SVG-path groups inside the board — exported as reusable React icons is deferred to M08-001 (recorded as open work, not invented inline).

## Affected Next.js paths

- `src/app/globals.css` — token block (`:root`), every value annotated with its Penpot origin.
- `src/shared/design/Sidebar.tsx` / `Sidebar.module.css` — visual component (labels verbatim, RTL).
- `src/app/layout.tsx` — Vazirmatn via `next/font/google` (weights 400/500/600/800).
- `src/app/dashboard/layout.tsx` — composition (application-owned nav targets per SCREEN-INVENTORY).
- `design/penpot-sync.json` — mapping entry `global-navigation.sidebar`.

## Approval status

`provisional`: the connected file was designated the working design source for this task,
but pixel-fidelity sign-off by a human reviewer has not happened. Promotion to
`approved` in the manifest is a human decision; only approved mappings may ship to
production routes.

## Unresolved design differences / open questions

1. Active-state styling: the reference board shows all nav items in resting state (no highlighted row observed); active treatment (weight 800 + primary color) is a minimal interpretation recorded here and must be confirmed against designer intent during M08-001.
2. Icon SVGs are present in Penpot but not yet extracted to code (deferred, tracked above).
3. Mobile target: no mobile frame exists for the sidebar → `responsiveTargets.mobile` stays `"tbd"`.
4. Brand strings read "دایرکتم/DM" (Directam) in the source; rendered verbatim pending product renaming decision.

## Tests run for this sync

- `npm run lint` → pass
- `node --test tests/penpot-sync.test.mjs` → 6 pass / 0 fail (manifest contract)
- `npm run verify` (lint + typecheck + tests + production build) → pass
- `python scripts/check_architecture.py` → pass
