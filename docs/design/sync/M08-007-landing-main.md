# M08-007 — Public landing sync evidence

## Stable Penpot target

| Field | Evidence |
|---|---|
| Requested mapping | `landing.main` |
| Configured source | `directam-public` |
| Expected / connected file ID | `c269caa0-e456-818c-8008-85a77340be64` / same — PASS |
| Connected display name | `New File 1` (informational) |
| Page ID / name | `c269caa0-e456-818c-8008-85a77340be65` / `Page 1` |
| Board ID / name | `f6b8d46f-5deb-801d-8008-85ab43d94e44` / `Directam Landing — Desktop` |
| Board geometry | 1440 × 7200; 271 direct child layers |
| Component ID | `null` (the board is not a declared library component) |
| Page activated programmatically? | YES — `getPageById` then `penpot.openPage` |
| Target page opened manually by human? | NO |
| Penpot revision | `null` — the API exposed no revision; none was invented |

## Live inspection and implementation

MCP first verified `penpot.currentFile.id`, enumerated the connected pages, resolved Page
1 by stable ID, opened it programmatically, and resolved the landing board by stable ID.
The live structure exposed a flat desktop composition with Header/Hero, feature cards,
pricing cards (monthly, annual and support), audience cards, four named client cards, FAQ,
CTA and Footer. The implementation translates those regions into reusable React sections
under `src/features/landing/ui/` instead of pasting generated Penpot markup.

The MCP plugin session disconnected on the first full-board export attempt, then
reconnected during the same task. A later full-board PNG export succeeded, exposing the
exact 7200-pixel composition and confirming the section order, yellow hero, pale feature/
audience/FAQ surfaces, purple consultation/steps, three plan cards and dark footer.
Live queries also extracted all 18 fill colors, both stroke colors, 30 Vazirmatn
size/weight combinations, section-anchor geometry and the complete text-layer copy.

Three image fills were resolved and exported live: Hero Team
`f6b8d46f-…85ab4e2d3d68`, Video Image `…85ab527ef7c7`, and Phone Mockup
`…85ab547d72d4`. Their source-identical assets in the captured Directam reference were
stored locally as `directam-team.webp`, `directam-video.webp` and
`directam-support.webp`, avoiding runtime dependence on the source website.

No dedicated mobile board exists on Page 1. The 390/360 layouts are explicitly derived
responsive adaptations: single-column content, two-column intermediate grids, native
mobile navigation, readable type and touch targets. The three plan amounts are the exact
text layers read from the live board rather than invented values. Their CTAs only enter
the registration flow; no checkout, payment or entitlement success is claimed without a
server contract.

## Code and tokens

- Composition: `src/app/page.tsx`
- Reusable view: `src/features/landing/ui/LandingPage.tsx`
- Responsive styling: `src/features/landing/ui/LandingPage.module.css`
- Landing tokens: exact live fills purple `#8919A1`, pink `#BE0182`, ink `#242424`,
  muted `#6B6B6B`, pale feature surface `#F1F5F8`, gold `#FED400`, plus secondary
  fills recorded in the MCP evidence above
- Registry: additional source `directam-public` and implemented mapping `landing.main`
- Assets: three live-resolved image fills stored under `public/landing/`; remaining icons
  and simple shapes stay code-native

## Verification evidence

- Graphify 0.9.26 refresh: 2,126 nodes / 4,152 edges; task query recorded healthy.
- Penpot offline target validator resolves mapping → additional source file → page → board.
- Frontend unit/contract tests: 20/20 pass before final state gates.
- Production build: pass; `/` is statically prerendered.
- Chrome screenshots: `artifacts/visual-review/M08-007/`.
- Exact viewport checks: no horizontal overflow at 1440, 1280, 1024, 768, 390 or 360 px.
- At 390 px the opened mobile menu bounds are 16…326 inside the 390 px viewport.

## Residual fidelity note

The Penpot board has no mobile frame, so mobile remains a deliberate derived adaptation.
The desktop implementation is compared against a fresh live board export; visual QA still
remains human-reviewable because no automated pixel-diff threshold is committed.
