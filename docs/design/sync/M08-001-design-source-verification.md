# M08-001 — Design-source verification (live Penpot MCP)

**Date:** 2026-08-24
**Method:** Live reads through the official Penpot MCP plugin (`penpot.currentFile`,
`penpotUtils.getPages()`, `penpotUtils.findShape`, `penpotUtils.shapeStructure`).
No Penpot content was modified or recreated during this pass.

## Verified design source

| Field | Value |
|---|---|
| File id (stable) | `c269caa0-e456-818c-8008-85a77340be64` |
| File display name | `New File 1` (default name — never use as the sole identifier) |
| Page count | 24 |
| Plugin API version | 2.17.2 |

The stable file id is now persisted in `frontend/Qasedak.Web/design/penpot-sync.json`
(`source.fileId`) together with all 24 page ids, so future agents cannot accidentally
sync against another same-named file.

## Page inventory (stable ids)

`Page 1` (`c269caa0-…40be65`), `Directam Admin Dashboard`
(`f6b8d46f-…49c0ba1`), `Directam — Help Center`, `— Smart SMS`, `— Profile States`,
`— Connect Instagram`, `— Pricing & Components`, `— Cards / Showcase`, `— Follow-up`,
`— Comment Automation`, `— Form Maker`, `— Ice Breakers`, `— Global Navigation Components`
(all `f5bf3c2c-…/f6b8d46f-…` ids recorded verbatim in the manifest), plus eleven
`GetCode · 00–10` pages (`324404a7-ad1e-8048-…`). Full id list lives in the manifest
`source.verifiedPages`.

## Resume-brief expectations vs. reality

| Expected | Actual | Verdict |
|---|---|---|
| ~8 pages | 24 pages | mismatch |
| Page named `Directam Landing` | no such page | mismatch |
| Page contains the earlier 13-section landing build | a board `Directam Landing — Desktop` (`f6b8d46f-5deb-801d-8008-85ab43d94e44`) sits on `Page 1`; flat layout, different section naming (Promo Bar / Hero Heading / Feature Card … / Price Card … / FAQ / Footer), no flex layouts | mismatch |

Global search across every page found no remnant of the earlier 13-section build
(`01 · topbar`, `03 · hero`, `13 · footer` absent everywhere). The candidate board is
therefore treated as an unverified alternative until a human designates the canonical
landing source.

## Consequences

- M08-001..005 remain **BLOCKED**
  (`design-source-verification-pending-human-decision`); resumption requires the human
  to pick: (a) adopt the current live file/board as canonical and resume M08, or
  (b) restore/recreate the 13-section landing first.
- Manifest updated with stable ids only; existing mapping `global-navigation.sidebar`
  re-validated against live page/board/component ids (unchanged, still matching).
- Existing sidebar mapping tokens were not re-derived in this pass; token/component
  extraction for M08-001 happens after the human decision.

## Gates run

| Gate | Result |
|---|---|
| Graphify preflight (`graphify . --update --no-viz --code-only` + `cluster-only`) | healthy, evidence appended to `.agent-state/GRAPHIFY_EVIDENCE.md` |
| `graphify query "penpot sync manifest file identity stable ids design source landing board" --budget 1200` | executed (traversal hit manifest/test nodes) |
| `python scripts/validate_penpot_sync.py` | PASSED (6/6 tests) |
| Backend/frontend full gates | not run — no application code changed in this pass |
