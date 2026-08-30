# M08-006 — Product foundation sync

- Canonical and connected file: `c269caa0-e456-818c-8008-89e5136d6851` (`directam`)
- Page: Product UI Components — `c48311ed-e700-80f8-8008-881e9771c583`
- Foundations board: `c48311ed-e700-80f8-8008-881ea59038ed`
- Penpot revision: `null` (not exposed)
- Was target page manually opened by human? **NO**
- Was page activated programmatically? **YES**

## Native components inspected

| Component | Main board | Component ID |
|---|---|---|
| Primary Button | `c48311ed-e700-80f8-8008-881ea96e48d1` | `c48311ed-e700-80f8-8008-881ea9ca0747` |
| Text Field | `c48311ed-e700-80f8-8008-881ea9cecdfb` | `c48311ed-e700-80f8-8008-881eaa0bba2d` |
| Status Alert | `c48311ed-e700-80f8-8008-881eaa0f118d` | `c48311ed-e700-80f8-8008-881eaa60589f` |
| Conversation Item | `c48311ed-e700-80f8-8008-881eaa66516e` | `c48311ed-e700-80f8-8008-881eaafa74fb` |
| Payment Provider Option | `c48311ed-e700-80f8-8008-881eaafe087a` | `c48311ed-e700-80f8-8008-881eaba064db` |

All five main boards were structurally inspected and visually exported. Extracted tokens
are registered in `design/penpot-sync.json` and consumed through `src/app/globals.css`.
Changed implementation paths include `src/shared/design/Button.tsx`, `FormField.tsx`,
`Feedback.tsx`, `PageHeader.tsx`, their CSS modules, and `ContentCards.module.css`.

Viewport verification: 1440, 1280, 1024, 768, 390, and 360 px. Remaining difference:
the application composes the primitives into real server states; behavior and validation
are intentionally not copied from Penpot.
