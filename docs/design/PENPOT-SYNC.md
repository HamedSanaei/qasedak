# Penpot ↔ Next.js synchronization contract

Penpot is the canonical source of truth for **approved visual design** (screens, boards,
components, variants, typography, colors, spacing, radii, assets). The Next.js app is the
canonical source of truth for **application behavior**: routing decisions, API integration,
application state, validation and authorization logic. This document defines the exact
workflow that keeps both true without one destroying the other.

## Workflow

```
Penpot (approved source)
  → Penpot MCP (official server, live inspection)
  → design inspection (pages / boards / components / tokens / geometry)
  → stable mapping (design/penpot-sync.json)
  → reusable Next.js components (visual layer only)
  → route/page composition (app-owned behavior)
  → verification (build/tests/manifest validation)
  → sync evidence (docs/design/sync/<task>-<slug>.md)
```

Every task that touches a Penpot-owned screen MUST execute all eight steps of this loop.
The authoritative agent obligations are codified in `AGENTS.md` §3 (design sync rules).

## Manifest

`frontend/Qasedak.Web/design/penpot-sync.json` is the machine-readable mapping between
Penpot identifiers and code. One entry per synchronized board/component:

| Field | Meaning |
|---|---|
| `id` | Stable slug for the mapping (unique). |
| `designName` | Exact Penpot board/component name. |
| `penpotPageId` / `penpotPageName` | Page containing the design. |
| `penpotBoardId` | Board identifier (unique per mapping). |
| `penpotComponentId` | Library component identifier when the board instantiates one; otherwise `null`. |
| `approval.status` | `draft` \| `provisional` \| `approved` \| `superseded`. `provisional` means the connected Penpot file was designated the working source but a human has not yet signed off pixel fidelity. Only `approved` screens may ship to production routes. |
| `nextRoutes` | Application routes rendering the component (globally unique). |
| `componentPath` | Reusable component source (exists in repo; validated). |
| `compositionPaths` | Route/layout files composing it (exist in repo; validated). |
| `responsiveTargets` | Device widths the design defines; unknown targets stay `"tbd"` — never guessed. |
| `tokenDependencies` | Token names that must exist under the manifest `tokens` section. |
| `assetDependencies` | Exported asset identifiers (empty until assets are synced). |
| `lastSyncedAtUtc` | ISO-8601 timestamp of the last real MCP inspection. |
| `penpotRevision` | Penpot revision/fingerprint **only if the API exposes one**; otherwise literal `null`. Never fabricated. |
| `syncStatus` | `synced` \| `stale` \| `pending`. |

## Layer separation (what sync may touch)

| Layer | Owner | Sync may modify |
|---|---|---|
| Design primitives (tokens/CSS variables) | Penpot | yes — regenerate from Penpot values |
| Visual components (`src/shared/design/*`) | Penpot | yes — visual/layout markup + styles |
| Layout composition (`src/app/**`) | shared | layout wrappers only |
| Application behavior, API integration, business state, authorization, tests | application | **never** |

Re-sync updates visual/layout code, tokens and mapped assets. It must never regenerate or
delete behavioral code. Generated HTML/CSS dumps are forbidden as production architecture;
components are idiomatic React (server components by default, RTL-aware, token-driven).

## Tokens

Approved Penpot color/typography/radius values become CSS custom properties in
`src/app/globals.css` under `:root`, each annotated with its Penpot origin. Components
consume tokens — hardcoded duplicates of a mapped value fail review. Values absent from
Penpot are never invented; they are recorded as open questions in sync evidence.

## Verification

Deterministic repository validation lives in
`frontend/Qasedak.Web/tests/penpot-sync.test.mjs` and runs in both CI lanes:

- `npm test` / `npm run verify` (full frontend gate),
- `python scripts/verify.py --full` via `scripts/validate_penpot_sync.py`.

Checks: manifest schema; duplicate mapping ids/routes/board/component identifiers;
missing component/composition paths; token dependencies resolving against the token
section; enum validity for status fields; ISO timestamps; `approved` entries missing
routes/components; `penpotRevision` null-or-string (fabrication guard).

These checks never contact Penpot. Live synchronization happens through MCP during an
agent design-sync task; CI validates the committed contract.

## Evidence

Each real synchronization writes `docs/design/sync/<task>-<slug>.md` recording: source
page/board/component IDs, inspection method, extracted values (colors/typography/geometry),
affected Next.js paths, tokens introduced, visual export performed, tests run, unresolved
differences. `SCREEN-INVENTORY.md` is the human-readable roll-up of the manifest.
