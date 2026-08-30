# Sync infrastructure evidence — M05-006: stable Penpot addressing

## Canonical file verification

| Field | Value |
|---|---|
| Canonical Penpot file identity | `c269caa0-e456-818c-8008-85a77340be64` (`identityMode: file-id`) |
| Connected file identity | `c269caa0-e456-818c-8008-85a77340be64` |
| Connected display name | `New File 1` (informational only) |
| File verification | **PASS** |
| Fingerprint verification | **PASS**; all three required stable page IDs present |
| Stable file ID exposed by MCP | **YES** — `penpot.currentFile.id` |
| Was a target page manually opened by a human? | **NO** |
| Was each target page activated programmatically? | **YES** |

The official plugin session was already attached to the canonical file. Browser-control
execution was not available in this agent environment, so wrong-file automatic navigation
was not exercised. The canonical safe `fileUrl` is committed; agents must use it when
browser automation is available, otherwise return `PENPOT_WRONG_FILE_CONNECTED`.

## Live two-page MCP navigation test

The test first activated `Directam Admin Dashboard`
(`f6b8d46f-5deb-801d-8008-85ad249c0ba1`) programmatically to establish a non-target
starting page. It then resolved two registry mappings without human navigation:

### Requested mapping: `global-navigation.sidebar`

| Field | Resolved value |
|---|---|
| Page ID | `f5bf3c2c-b970-8002-8008-8752c5573aef` |
| Board ID | `f5bf3c2c-b970-8002-8008-8752c6768b24` |
| Component ID | `f5bf3c2c-b970-8002-8008-8752c87448ee` |
| Live result | page opened; board `Navigation / Sidebar` read (256×1050, 34 children); component `Navigation/Sidebar` resolved |

### Requested mapping: `landing.main`

| Field | Resolved value |
|---|---|
| Page ID | `c269caa0-e456-818c-8008-85a77340be65` |
| Board ID | `f6b8d46f-5deb-801d-8008-85ab43d94e44` |
| Component ID | `null` (not declared) |
| Live result | page opened; board `Directam Landing — Desktop` read (1440×7200, 271 children) |

Both pages were obtained with `penpotUtils.getPageById`, activated using
`penpot.openPage`, and resolved by mapped board/component IDs. No visual design was
modified to prove navigation.

## Corrected assumptions

- The old registry asserted that file ID was unavailable; the live probe disproved it.
- The sync contract did not specify file verification or programmatic page activation.
- The offline validator accepted name-only/weak source metadata and did not validate UUID
  page/board/component targets or a page-ID fingerprint.
- No separate Harness implementation existed in the repository; the normative
  `.agents/AGENT_PROTOCOL.md` now explicitly rejects `active page == target page`.

## Repository changes and verification

- Canonical file metadata, fingerprint, and the `landing.main` stable mapping added to
  `frontend/Qasedak.Web/design/penpot-sync.json`.
- Stable resolution and wrong-file behavior documented in `AGENTS.md`,
  `.agents/AGENT_PROTOCOL.md`, `docs/design/PENPOT-SYNC.md`, and ADR-005.
- Offline validation expanded in `frontend/Qasedak.Web/tests/penpot-sync.test.mjs`;
  `scripts/validate_penpot_sync.py --mapping <slug>` provides deterministic target lookup.
- CI remains private-Penpot-independent; live existence checks are MCP agent evidence only.

Final gates: `agent_finalize.py --task M05-006` passed; `verify.py --full` passed,
including 248 backend tests, frontend 9/9 tests + production build, architecture/docs/state
checks, and both `qasedak-api:verify` / `qasedak-web:verify` Docker image builds.

Penpot revision remains `null` because the connected API exposed a file ID but no design
revision identifier.
