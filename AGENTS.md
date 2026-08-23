# Qasedak AI Agent Contract

This file is normative. Every AI coding agent must obey it before reading broadly, editing code, or claiming task completion.

## 1. Mandatory opening sequence

Read only these bounded files first:

1. `AGENTS.md`
2. `docs/project/STATUS.md`
3. `docs/project/TASKS.md` for the assigned task ID
4. `docs/project/HANDOFF.md`
5. `docs/04-ARCHITECTURE.md`
6. any ADR explicitly referenced by the task

Never start feature work without a task ID (`Mxx-yyy`).

## 2. Graphify is mandatory

Graphify is the default repository-navigation mechanism and is required to reduce token consumption.

Before broad Glob/Grep/search or broad file reading:

```bash
graphify --version
# first graph creation only:
graphify . --no-viz
# later tasks:
graphify . --update --no-viz
# mandatory task-specific discovery:
graphify query "<task-specific architectural question>" --budget 1200
```

Use `graphify path` / `graphify explain` when dependency or call-path understanding is needed. Prefer graph output over opening many files.

Record evidence:

```bash
python scripts/record_graphify_evidence.py   --task Mxx-yyy   --status healthy   --version "<graphify --version output>"   --command "graphify . --update --no-viz"   --query "<exact query used>"
```

An AI agent **must stop before feature edits** if Graphify is unavailable, unhealthy, or cannot refresh, unless the human explicitly authorizes a bypass. A bypass must be recorded as `status=bypassed` with the human reason. Never fabricate Graphify health or query evidence.

The final report for every AI instruction must state:

- Graphify status and version;
- refresh command used;
- task-specific query/query budget;
- whether `graphify-out/graph.json` and `GRAPH_REPORT.md` were refreshed;
- verification commands and results.

## 3. Hard architecture constraints

Qasedak is a Modular Monolith with Clean Architecture inside each business module.

- `*.Domain` may reference only `Qasedak.BuildingBlocks.Domain`.
- `*.Application` may reference its own Domain plus application/domain building blocks.
- `*.Infrastructure` may reference its own Application/Domain plus infrastructure building blocks.
- Module projects must not directly reference another business module.
- `Qasedak.Api` is the HTTP composition root. It may reference module Infrastructure projects.
- Domain code must not know ASP.NET Core, EF Core, PostgreSQL, Meta HTTP, serialization, UI or filesystem concerns.
- Cross-module communication requires an explicit contract/event/ADR. Never reach into another module's DbContext or tables.
- PostgreSQL uses one physical database initially, with module-owned schemas.
- Frontend is independent Next.js and never references backend source projects.

### 3.1 Penpot ↔ Next.js design sync (mandatory for frontend screens)

Penpot (via the official Penpot MCP server) is the canonical source of truth for approved
visual design; Next.js owns application behavior and frontend architecture. The contract,
manifest and evidence locations are defined in `docs/design/PENPOT-SYNC.md`; the mapping
lives in `frontend/Qasedak.Web/design/penpot-sync.json`.

Any task modifying a Penpot-owned frontend screen MUST, in order:

1. run the Graphify preflight (§2);
2. connect to and read the current design through the Penpot MCP server;
3. identify the mapped Penpot page/board/component in `penpot-sync.json`;
4. inspect the live design rather than relying on memory or screenshots;
5. update Next.js through reusable components and tokens — never by regenerating whole files or pasting generated HTML/CSS; API integration, application state, validation, authorization behavior and tests must survive re-sync untouched;
6. update the sync manifest and write a sync record under `docs/design/sync/`;
7. run frontend tests/build (`npm run verify`) plus `python scripts/check_architecture.py`;
8. report both Penpot MCP evidence (pages/boards/components actually read) and Graphify evidence.

If an approved screen exists in Penpot, redesigning it from imagination is forbidden.
If the Penpot MCP server is unavailable, report that as a blocker; never claim a page was
synchronized without reading it through MCP. Do not invent design values or a Penpot
revision identifier — record `null` when the API exposes none.

Run `python scripts/check_architecture.py` whenever project references change.
Run `python scripts/validate_penpot_sync.py` whenever the sync manifest changes.

## 4. Engineering and testing rules

- A behavior change requires a test that fails without the change when practical.
- Bug fixes require a regression test first or alongside the fix.
- Prefer domain unit tests, application tests, PostgreSQL integration tests with Testcontainers, API integration tests, external-contract fixtures, and frontend behavior tests at the appropriate boundary.
- Do not mock the database for persistence semantics.
- Meta/external APIs must be behind ports/adapters and tested with deterministic fixtures/contracts; CI must not call live Meta APIs.
- Idempotency, concurrency, authorization and failure paths are first-class tests for webhook/automation code.
- Do not reduce test coverage, skip tests, weaken assertions, add suppressions, or remove quality gates to make CI green without explicit human approval.
- No production feature is complete while its relevant tests are TODO.

## 5. State is part of the product

After **every AI instruction/task**, update all applicable state:

- `.agent-state/PROJECT_STATE.json`
- `.agent-state/GRAPHIFY_EVIDENCE.md`
- `docs/project/STATUS.md`
- `docs/project/TASKS.md`
- `docs/project/HANDOFF.md`
- `docs/project/DECISIONS.md` / ADRs when decisions change
- `FILE_MANIFEST.txt` via `python scripts/generate_manifest.py`

Do not silently leave stale task status or handoff text.

## 6. Git discipline

Agents must not commit, push, tag, release or deploy unless the human explicitly asks. At the end of every instruction and milestone, always provide a suggested Conventional Commit message. The task tracker already defines the default message for every task; update it if scope legitimately changes.

## 7. Completion gate

Before claiming `done`:

```bash
python scripts/agent_finalize.py --task Mxx-yyy
python scripts/verify.py --full
```

If a required tool is unavailable, report the exact unexecuted gate; do not represent it as passing.

## 8. Required final report

Use this compact structure:

- **Task:** ID + outcome
- **Graphify:** version, health, refresh, queries, output freshness
- **Changes:** bounded summary
- **Tests/gates:** commands + pass/fail/not-run
- **State:** files updated and next task
- **Risks/notes:** only concrete residual risks
- **Suggested commit:** `type(scope): description`

The repository state—not chat memory—is the source of truth for the next agent.
