# Current handoff

## 2026-09-05 — M13-003 DONE; M13-004 packet ready (do not start M13-004)

M13-003 centralized the Graph transport foundation without merging adapters:
`MetaGraphOptions` (`Instagram:Meta`: GraphHost/ApiVersion/TimeoutSeconds),
`MetaGraphUris.Versioned`, `MetaGraphError` envelope + parser (both official
shapes, redaction, fbtrace), `MetaGraphFailure` taxonomy + classifier (official
10/2534022; IsRetryable), `MetaGraphTransport` executor. OAuth/inspector/
messaging adapters converged (versioned paths; OAuth endpoints unversioned by
contract; 490 deleted). Instagram unit 82→122; 546/546 backend; full verify
green; no schema change. Commit/push/CI/deploy/smoke/evidence follow in this
same instruction. State: M13-003 DONE, currentTask=M13-004 TODO. Production
runtime stays `sha-3c3c721bfa61` until the M13-003 deployment switches it.

### M13-004 packet (read-only handoff)

- No scheduler exists yet: M13-003 deliberately built no retry loops or queues —
  `MetaGraphFailure.IsRetryable` is the classification contract M13-004 job
  handlers will consume (RateLimited/Transient/Transport → retry with backoff).
- New shared primitives to reuse: `MetaGraphError.FbTraceId` for correlating
  provider failures inside durable job records (store trace id, never tokens).
- Module boundaries unchanged: Instagram adapters stay focused; durable work
  belongs in BuildingBlocks/platform with module-owned handlers; job payloads
  must never contain access tokens (resolve via `ConnectedAccountId` at
  execution, per ADR-011).
- Config surface: `Instagram:Meta:TimeoutSeconds` bounds single Graph attempts;
  M13-004 owns attempt/backoff/lease timing separately.

Inbound exact-account routing is now deterministic: `ResolveActiveAccountAsync`
returns Resolved/NotFound/Ambiguous over active rows only; connect enforces one
active owner globally (`account.alreadyConnectedElsewhere`); Outcome A identity
proven first-party (OAuth user_id == IG_ID == entry.id, no second column);
`ProviderAccountId` naming on events; unsafe primitive removed; additive routing
index. Reconnect E2E failed pre-fix (0 threads) and passes post-fix; 506/506
backend green; full verify green. Commit/push/CI/deploy/smoke/evidence follow in
this same instruction. State: M13-002 DONE, currentTask=M13-003 TODO.
Production runtime moves from `sha-2fd1b3205d87` to the correction image on
deploy; deployment evidence appended below.

### Deployment evidence — M13-002 routing correction (2026-09-05, UTC)

- Correction commit: `3c3c721bfa61df7de56c1eea6415dceef272e5c8`
  (`fix(instagram): make inbound account resolution deterministic`), pushed
  to `origin/master`.
- CI `33938422386`: success (repository-contracts, backend, frontend, docker).
- CodeQL `33938422377`: success.
- Publish Images `33938965616`: success after one `--failed` rerun. First
  attempt failed the API image on a transient Ubuntu archive `Hash Sum
  mismatch` (infrastructure flake, unrelated); rerun published both images as
  `ghcr.io/hamedsanaei/qasedak-api|web:sha-3c3c721bfa61`.
- Deploy Production `33939832514`: success for exactly the correction SHA.
  Previous tag `sha-2fd1b3205d87` → `sha-3c3c721bfa61`; DB backup
  `qasedak-20260905T024243Z-sha-3c3c721bfa61.dump`; routing-index migration
  replayed; api/web Healthy; in-workflow smoke passed (~02:42:37–02:42:59Z).
  No rollback.
- Independent smoke: `/` 200, `/api/v1/system` 200, invalid-login 401.
- Live Meta identity/reconnect smoke: NOT RUN (no designated test accounts).
- **HEAD vs production distinction:** the evidence commit below is docs/state-only;
  production runtime remains the immutable `sha-3c3c721bfa61` images.

M13-002 shipped exact channel-account binding (production code + 2 migrations +
tests). `ChannelAccountId` (BuildingBlocks.Domain, opaque struct over Guid)
flows through Conversations/Automations contracts; Api composition root maps
`ConnectedAccount.Id` → account at the boundary; first-account fallback deleted
everywhere; legacy NULL rows preserved readable but refused for outbound/execution.
ADR-011 is normative. `verify.py --full` green (495/495 backend, frontend 64,
Docker images). Commit/push/CI/deploy/smoke/evidence-commit follow in this same
instruction; deployment evidence appended below. Production runtime stays
`sha-6e5b912e4be7` until the M13-002 deployment switches it.

### Deployment evidence — M13-002 (2026-09-05, all times UTC)

- Task commit: `2fd1b3205d87bb10fda70c12789bc9c4168fae68`
  (`refactor(instagram): bind channel activity to connected accounts`), pushed
  to `origin/master`.
- CI `33933983002`: success (repository-contracts, backend, frontend, docker).
- CodeQL `33933983007`: success.
- Publish Images `33934204827`: success — `ghcr.io/hamedsanaei/qasedak-api` +
  `qasedak-web` tagged immutable `sha-2fd1b3205d87` (first 12 of the task SHA).
- Deploy Production `33934275735`: success for exactly the task SHA.
  Previous tag `sha-6e5b912e4be7` → `sha-2fd1b3205d87`; DB backup
  `qasedak-20260905T005032Z-sha-2fd1b3205d87.dump`; both M13-002 migrations
  replayed (`20260905000206_AddChannelAccountId`,
  `20260905000458_AddChannelAccountBinding`); api/web Healthy; in-workflow
  health + public auth-routing smoke passed (~00:50:27–00:50:49Z). No rollback.
- Independent smoke at `https://qasedak.tofanservice.ir`: `/` 200,
  `/api/v1/system` 200, invalid-login `/web-api/auth/login` 401.
- Structural DB verification via deployment-workflow evidence (no direct
  production SSH from this agent): migration run complete with no errors.
- Live multi-account Meta mutation smoke: NOT RUN — no designated production
  test Instagram accounts (acceptable per task §42; proof via deterministic +
  Testcontainers + E2E gates). No customer accounts touched.
- **HEAD vs production distinction:** the evidence commit below is docs/state-only;
  production runtime remains the immutable `sha-2fd1b3205d87` images.

### M13-003 packet (read-only handoff)

- ChannelAccountId representation: `Qasedak.BuildingBlocks.Domain.ChannelAccountId`
  readonly record struct (`From` rejects empty; `TryParse`; `IsResolved`); persisted
  as nullable uuid via per-module EF converters; null = legacy/unresolved.
- Legacy Conversation semantics: NULL readable (list/detail carry
  `channelAccountId: null`); replies refuse `reply.accountUnresolved`; exact inbound
  never adopts legacy rows (separate exact thread); global `mid` uniqueness unchanged.
- Legacy Automation semantics: NULL binding never matches exact events (filtered in
  `ListByAccountAsync`, refused pre-ledger in executor); binding create-time immutable
  (`automation.bindingImmutable` on PUT change); rebind = new automation (M13-014).
- New Conversation natural key: `(WorkspaceId, Channel, ChannelAccountId,
  ParticipantId)` unique via `IX_conversations_exact_thread` (NULLs distinct).
- Inbound path: webhook event provider identity → `FindWorkspaceIdByProviderIdentityAsync`
  → `FindByProviderIdentityAsync` (drop unknown/disconnected) → `ChannelAccountId.From`
  → module use cases (projection refuses unresolved).
- Outbound path: thread/automation account → `ChannelDeliveryRequest/ActionDispatch`
  → `InstagramReplyGateway`: ID lookup → workspace-ownership → connected-state →
  InstagramLogin-path → exact protected token → provider adapter. No `ListByWorkspace`
  / `FirstOrDefault` selection remains.
- Ownership validation: `instagram.accountWorkspaceMismatch` (cross-workspace),
  `unknownAccount`, `accountDisconnected`, `tokenMissing`, `accountUnresolved`,
  `unsupportedAccountPath` — all 409, zero fallback sends (proven by E2E).
- Removed fallbacks: `InstagramReplyGateway` first-active selection; workspace-wide
  automation enumeration in `AutomationCommentBridge` (now `ListByAccountAsync`).
- New tests: `ExactAccountRoutingTests` (7), `AutomationAccountBindingEndpointTests`
  (2), `ChannelAccountMigrationTests` (2), binding unit cases; fixed a real test bug
  (v7-Guid 8-char tags collide; full tags + strict-200 webhook asserts now).
- Migrations: `20260905000206_AddChannelAccountId` (conversations: nullable add +
  index replace), `20260905000458_AddChannelAccountBinding` (automations: purely
  additive). Rollback: automations fully safe; conversations safe before
  multi-account rows exist (duplicate-triple check + `Down()` limitation in ADR-011).
- Residual outside scope: Contacts keeps person-centric identity (no account split —
  analyzed, no change); text-limit/rate-limit pinning belongs to M13-010/009.
- M13-001 reminders for M13-003: IG Login primary; `graph.instagram.com`;
  configure one Graph version (latest observed v26.0, do not hardcode); replace stale
  window mapping `490` with official `10 / 2534022` (+ `fbtrace_id`); no token-bearing
  URLs/logs; do NOT build a giant Graph client (capability ports stay separate).

M13-001 was DONE and deployed, but a post-completion audit found one material
error: the contract classified per-user follow status as globally unsupported.
The official Instagram User Profile API with Instagram Login
(`GET graph.instagram.com/<IGSID>` → `is_user_follow_business`, IG User token,
basic + manage_messages) proves the field exists. Corrected semantics:
SUPPORTED with user-consent constraints (consent = sent message / icebreaker /
persistent menu; raw comment fails officially with a definitive consent error);
ordinary template-postback consent is UNVERIFIED and stays behind a
capability/policy switch. M13-011 keeps its provider-conditional design with
Cases A (query when consented) / B (never blindly) / C (gated until proven);
M13-012 stays decoupled from M13-011. All other M13-001 conclusions preserved.
Correction verified green (docs/state/arch/env/Penpot/manifest/diff-check,
agent_finalize, verify.py --full) and restored to DONE. State:
currentTask=M13-002 (TODO), lastCompletedTask=M13-001. Production runtime unchanged
(`sha-6e5b912e4be7`); this correction ships as a docs-only `[skip ci]` commit
with no new deployment.

M13-001 completed docs/state-only (zero production source/migration/package/
test/secret changes). Normative contract: `docs/product/meta-instagram-platform-contract.md`;
ADR-010 accepted; ADR-006 messaging decision superseded (preserved as history);
matrix marked historical; lifecycle re-verified; SRS §4 updated; DECISIONS.md
disambiguated; verdict notes on M13-003/008/009/011. State:
lastCompletedTask=M13-001, currentTask=M13-002 (TODO). `verify.py --full` passed
(Docker Desktop started locally for Testcontainers). The pre-existing untracked
`docs/fa/qasedak_m13_production_handbook_fa.html` was hash-parked outside the
repo for the doc gate and restored byte-identical; it stays untracked/unpushed.
Commit/push/CI/deploy/smoke/evidence-commit follow in this same instruction;
deployment evidence will be appended in the subsection below.

### Deployment evidence — M13-001 (2026-09-04, all times UTC)

- Task commit: `6e5b912e4be735df0aad773dbc8e0d2524d29085`
  (`docs(instagram): reconcile current meta api contract`), pushed
  `c989cce..6e5b912` to `origin/master`, docs/state-only (no runtime change).
- CI `33928409880`: success (repository-contracts, backend, frontend, docker).
- CodeQL `33928409979`: success.
- Publish Images `33928610451`: success — `ghcr.io/hamedsanaei/qasedak-api` +
  `qasedak-web` tagged immutable `sha-6e5b912e4be7` (first 12 of the task SHA).
- Deploy Production `33928687404`: success for exactly the task SHA.
  Previous tag `sha-c989ccee330e` → `sha-6e5b912e4be7`; DB backup
  `qasedak-20260904T231403Z-sha-6e5b912e4be7.dump`; migrations replayed;
  api/web containers Healthy; in-workflow health + public auth-routing smoke
  passed at `https://qasedak.tofanservice.ir` (~23:13:58–23:14:20Z).
- Independent smoke: `/` → 200, `/api/v1/system` → 200
  (`{"name":"Qasedak API","architecture":"Modular Monolith","status":"scaffold"}`),
  invalid-credential `POST /web-api/auth/login` → 401. No live Instagram
  mutation was performed (none exists in this task).
- **HEAD vs production distinction:** after the evidence commit below,
  repository HEAD is docs/state-only and newer than the deployed code, while
  the production runtime remains the immutable `sha-6e5b912e4be7` images. This
  is intentional: M13-001 contains no runtime change to deploy.

### M13-002 packet (read-only handoff)

- Verified assumptions for exact-account routing: Instagram Login is primary;
  professional account identity is the numeric `IG_ID` (`/me` alias) on
  `graph.instagram.com`, distinct from the app-scoped `user_id` from code
  exchange and from partner IGSIDs; token is the Instagram User long-lived
  token (basic + manage_messages for messaging reads).
- IDs that must not be confused: `IG_ID` (account) vs app-scoped user id vs
  `IGSID` (conversation partner) vs `mid` (provider message id) vs comment ID.
- Token/account path: per-`ConnectedAccountId` IG User token from the protected
  store; no workspace-first-account fallback afterward.
- Superseded: Messenger-Platform-only messaging, FB-Login-required messaging,
  Page-required-for-messaging, code-490 window mapping (now 10/2534022),
  watermark-shaped reads (now `read:{mid}`).
- Provider blockers: none for M13-002 scope; follow-gate consent semantics do
not touch this task; carried-over text-limit/rate-limit assumptions belong to
M13-003/010, not here.
- First architectural objective: introduce Qasedak-owned opaque
  `ChannelAccountId`, change thread uniqueness to `(WorkspaceId, Channel,
  ChannelAccountId, ParticipantId)` with a backward-compatible migration, and
  resolve the exact `ConnectedAccount.Id` on the inbound bridge and in the
  reply gateway — no Instagram domain types outside the Instagram module.

## 2026-09-05 — M13 planning dependencies refined; M13-001 remains next

Planning-only refinement completed without starting M13-001 or changing any M13 status.
M13-001 through M13-015 remain TODO; `currentMilestone=M13`,
`currentTask=M13-001`, and `lastCompletedTask=M12-008` remain authoritative.

The dependency correction is deliberate: M13-012 now depends on M13-002, M13-004,
M13-008, M13-009 and M13-010—not M13-011. Its DM triggers, post scoping,
original-media matching, keyword modes, public replies, delayed follow-ups,
exact-account routing and direct/Private Reply capabilities can complete independently.
It consumes follow-gate configuration/execution from M13-011 only if current official
Meta evidence verifies and implementation delivers that provider capability.

M13-011 remains one task but has two explicit scopes. Provider-independent opening
Private Reply, opaque postback correlation/validation, durable continuation, exact-
account/workspace checks, direct-message reveal, supported read fallback and race/
idempotency handling still ship. Relationship/follow-status lookup is provider-dependent:
if unavailable or too restricted, record the official limitation, disable only that gate,
and never scrape, call private APIs or invent status. Either a fully supported follow-
gated flow or a supported opening/postback/reveal flow with the gate truthfully unavailable
can satisfy the task.

M13-013 now has Phase A for bounded restart-safe comment reconciliation through the same
normalized automation/global Private Reply claim path, and Phase B for bounded provider
conversation/message import through Api/CrossModule into a channel-neutral Conversations
upsert contract. Phase B is explicitly designed not to inherit M13-012 automation
concerns, and Instagram Infrastructure still cannot write the Conversations schema.

M13-014 exposes only verified/implemented capability, exact ConnectedAccount state and
truthful unsupported/permission/disconnected/unhealthy/temporary-failure states.
M13-015 defines OpenReply parity as current-Meta-supported, intentionally scoped behavior
and requires a six-category compliance matrix. Unsupported historical/reference behavior
does not fail the final gate merely because OpenReply contains it.

The next agent still executes only M13-001 and must use live current official Meta
documentation before any production work. Preserve the extensive unrelated frontend/
design changes already in the dirty worktree. No ADR, production source, migration,
package, test, commit, push, tag or deployment was changed by this refinement.

Verification for this refinement: `agent_preflight.py --task M13-001` ready; Graphify
0.9.26 code-only update + cluster refresh + the exact planning query at budget 1200
succeeded and evidence was appended; `check_docs.py`, `check_state.py` (72 tasks,
current M13-001), `check_architecture.py` (35 projects/6 modules),
`validate_penpot_sync.py` (6/6), `generate_manifest.py --check` (662 files), and
`git diff --check` passed. A bounded tracker assertion confirmed all 15 M13 statuses are
TODO, M13-012's dependency list excludes M13-011, and M13-013 contains both phase labels.
No Docker/Testcontainers, live Meta call, full toolchain verification or agent finalizer
was run; M13-001 remains TODO.

## 2026-09-05 — M13 OpenReply parity plan READY; M13-001 is next

M13 — Instagram OpenReply Parity & Production Integration is registered with
M13-001 through M13-015 all TODO. The next agent must execute only M13-001: reconcile
ADR-006, the capability matrix, OAuth/token-lifecycle document and current adapter
assumptions against live official Meta documentation. Do not begin M13-002 or production
feature work until that current-contract task is complete.

Planning evidence: Graphify 0.9.26 full semantic refresh first reported no LLM API key for
133 changed document/image files; the documented code-only refresh succeeded, then
`graphify cluster-only .` refreshed `graphify-out/graph.json`, `GRAPH_REPORT.md` and
`graph.html` (3758 nodes, 7571 edges, 284 communities). The budget-1200 discovery query
traced OAuth, connected accounts, webhook ingestion, messaging, Conversations identity,
automation dispatch and Contacts projection; a second budget-1200 query found no durable
scheduled-work subsystem beyond the webhook inbox/metrics hosted service. Evidence is
recorded under M13-001 with the code-only limitation explicit in project state.

The gap analysis used the current Qasedak implementation and OpenReply reference commit
`f180d2db6381f0c37e4b29848ab97e77c18f610f` (2026-09-03). OpenReply was treated only as
a behavior reference. Direct requests to the official Meta documentation host returned
HTTP 429 in this session; that is why M13-001 remains TODO and requires a fresh live
official-source audit rather than carrying the prompt or OpenReply assumptions forward.

Critical current-code corrections recorded in the tasks:

- Conversations lacks `ChannelAccountId`; its uniqueness key can merge the same
  participant across two Instagram accounts in one workspace.
- inbound bridges resolve provider identity only to workspace, outbound replies select
  the first active Instagram-login account, and active automations are enumerated across
  the workspace rather than bound to the receiving account;
- M06-005 routes comment first contact through the standard direct-message gateway, but
  M13 requires distinct direct-message, comment Private Reply and public-comment-reply
  operations plus a global account+comment Private Reply claim;
- Graph URLs/error handling are fragmented and mostly unversioned; a common transport is
  needed without creating a giant client;
- token refresh exists only as a primitive and the repository has no general durable
  scheduler; profile/professional identity enrichment, webhook subscription health,
  media/insights/history, postback/read events, interactive/follow flows and bounded
  reconciliation/history import remain missing.

No production code, migration, feature test, frontend behavior or dependency was changed.
Planning/state/manifest checks passed, as did Penpot manifest 6/6, repository contracts
2/2, restore, Release build (0 warnings/errors), format and all 380 unit tests.
`python scripts/verify.py --full` was attempted and stopped only when the Docker-backed
PostgreSQL suites could not connect to `npipe://./pipe/docker_engine`; frontend verify and
Docker image builds were not reached in that invocation. `agent_finalize.py --task
M13-001` was intentionally not run because M13-001 remains TODO, not completed. The
worktree already contained extensive unrelated user/frontend/design changes; preserve
them and do not reset or overwrite them. No commit, push, tag or deploy was requested or
performed.

## 2026-09-04 — Undone-task sweep (no registered TODOs; ad-hoc feature screens verified)

The human asked for every undone task to be done. Result: all 57 registered tasks
(M00-001 → M12-008) are DONE in `docs/project/TASKS.md` with zero TODO,
IN_PROGRESS or BLOCKED-as-status rows, so no registered task work was started.
The only unverified work in the tree was today's unregistered ad-hoc instruction
(Directam-reference feature screens under `/dashboard/features/*` and
`/dashboard/smart-sms`, explicitly assigned no task ID, local-only, no push).

Verification performed this session (no product edits): Graphify 0.9.26 healthy,
code-only refresh (3758 nodes/7571 edges/285 communities) plus a bounded task
query; `npm run verify` green (lint, typecheck, 64/64 frontend tests incl. 4 new
`features-penpot` cases, production build rendering every feature route);
`check_architecture.py` passed (35 projects, 6 business modules);
`validate_penpot_sync.py` passed 6/6; `agent_finalize.py --task M12-008` passed
and regenerated `FILE_MANIFEST.txt` (646 files; `--check` passes). Backend tree
has 0 changed files vs HEAD, so `verify.py --full` was not re-run — M12-008's
471/471 Testcontainers pass stands for the identical backend tree.

Divergence on record: `dashboard-navigation.mjs` now nests 6 feature
destinations under «امکانات» (12 unique destinations total), superseding the
7-destination count recorded in M12-008's evidence, by direct human request in
the ad-hoc instruction. Next agent: no registered product task is scheduled;
operational Mellat/Shaparak/Zarinpal go-live steps remain human-owned. No
commit, push, tag or deploy was performed.

## M12-008 — Customer dashboard navigation COMPLETE (2026-08-30)

The current working tree preserves the completed M08-007 landing and M12-003 dashboard
overview. `/dashboard` was audited as a normal customer/workspace surface: its actions
cover onboarding, Inbox, Automations, Accounts and Help only; no admin-only system,
cross-workspace or operational controls are exposed.

`src/shared/navigation/dashboard-navigation.mjs` is now the sole navigation contract
passed to both Sidebar instances. It defines 7 unique destinations; with the Sidebar
brand link there are 8 clickable link instances. Deterministic tests read this exact
contract and the Sidebar/DashboardShell composition, prove all pages exist and cover all
requested nested active paths. There are zero Sidebar redirects, disabled destinations
or 404s. Smart Answer and Comment Automation remain compatibility redirects to canonical
Automations; Cards, Follow-up, Form Maker, Ice Breakers and Smart SMS have no backend and
remain truthful unavailable screens outside active Sidebar navigation.

Accounts continues to read the real Identity and workspace-member APIs and covers
identity error, selected/no-workspace and session-expiry behavior. The human-designated
primary Penpot file (`c269caa0-e456-818c-8008-89e5136d6851`) was read first through the
official MCP; it contains no Help or Accounts board. The primary Identity states board
was used to validate Accounts. The permitted legacy `Directam — Help Center` board
(`f5bf3c2c-b970-8002-8008-8749d0a39e9f`, 1440×1050) was then live-read and PNG-exported.
Help now retains its search/quick-path/category/FAQ composition with Qasedak tokens and
real routes. FAQ search is genuinely functional; chat/ticket, support hours and Smart SMS
were not copied because no backend/configuration exists. `penpotRevision` remains null
because the API exposed none; evidence is in `docs/design/sync/M12-008-help-center.md`.

An authenticated Docker browser smoke reproduced the reported silent login symptom:
backend login and the HttpOnly cookie succeeded, but a cached client transition left the
browser on `/login` without an error. Successful login now uses a full
`window.location.replace("/dashboard")`; the same Docker logout/login flow then navigated
directly to Dashboard. Exact responsive review at 1440/1280/1024/768/390/360 passed;
desktop Sidebar is present through 1024, drawer below it, selection closes the drawer,
RTL and active state are correct, and Dashboard/Accounts/Help were visually inspected at
1440 and 390 without page-wide overflow.

Verification: Graphify 0.9.26 healthy with refreshed graph/report and the M12-008 budget
1200 query; `npm run verify` passed lint, typecheck, 60/60 tests and build; Docker Desktop
4.86.0 / Engine 29.7.2 healthy; full backend 471 discovered/executed/passed, 0 failed or
skipped, Testcontainers executed; isolated application smoke passed landing 200,
same-origin system API 200, register/login, Dashboard, Inbox, Automations, Instagram,
Billing, Accounts and Help. `python scripts/verify.py --full` passed static checks,
restore/build/format, backend tests, frontend verification and both Docker image builds.
No commit, push or deployment was performed.

## M12-003 — Penpot-synced dashboard overview COMPLETE (2026-08-30)

The exact page supplied by the human was inspected live through the official Penpot MCP:
file `c269caa0-e456-818c-8008-85a77340be64`, page
`f6b8d46f-5deb-801d-8008-85ad249c0ba1`, board
`f6b8d46f-5deb-801d-8008-85ad24c7b3f3`, exposed revision `281`. The approved
`dashboard.overview` mapping and `docs/design/sync/M12-003-dashboard-overview.md` now
own the `/dashboard` visual contract.

The content follows the Penpot geometry (three status rows, two-column 220px feature
cards, full-width final feature, three-column lower cards), but preserves Qasedak's real
Workspace/Inbox state and existing routes. Directam branding, static subscription/
connection assumptions and unapproved social URLs were not copied. `npm run verify`
passed lint, typecheck, 58 tests and production build; Penpot validation passed 6/6;
architecture passed (35 projects, 6 business modules). No commit, push or deployment was
performed because the human did not request one for this phase. `agent_finalize.py`
passed. `verify.py --full` passed static gates, restore, format and Release build with
0 warnings/errors; 380 unit tests and 3 non-container API tests passed, while the
Docker-backed PostgreSQL suites could not start because the local endpoint
`npipe://./pipe/docker_engine` is unavailable. This is the only residual gate.

## M12-007 — Production auth proxy repair COMPLETE (2026-08-30)

The deployed reverse proxy owns the `/api/` prefix and forwards it directly to ASP.NET
Core. M12-006 had placed Next.js cookie handlers under that same prefix, so production
login bypassed the handlers even though local frontend tests passed. The handlers and
callers now use `/web-api/*`, and the deploy script requires a public invalid login to
return HTTP 401 before accepting a release. Commit `7e72322` passed CI `33311180968`,
CodeQL `33311180901`, Publish Images `33311326079`, and Deploy Production `33311381218`.
The server runs immutable `sha-7e723229b191` images and the public auth-routing smoke
passed at `https://qasedak.tofanservice.ir`.

Local frontend verification passed all 57 tests and production build. Local full
verification was attempted, but Docker Desktop 4.86.0 crashed while initializing its
Inference socket; GitHub's Linux backend/Testcontainers and Docker jobs passed. Product
work returns to M12-003.

## M12-006 — Registration/login session flow COMPLETE (2026-08-30)

The reported symptom was caused by the active `src/app/login/page.tsx` and
`src/app/register/page.tsx` bypassing the server auth handlers used by the dashboard
guards. They now call same-origin server auth and workspace handlers with
same-origin credentials; the server establishes HttpOnly cookies and the existing
client-side feature screens retain a compatibility bearer value. Missing/failed responses
are rendered visibly. `npm run verify` (57 tests) and `python scripts/verify.py --full`
(471 backend tests plus Docker/static gates) passed.

M12-007 supersedes the original route namespace and carries the production push.

## Where we are

**M12-005 is DONE (2026-08-30): GitHub Actions manifest gate repaired.**
Run `33284839710` for `b39d508` failed only in `repository-contracts / Ensure manifest is
current` because the consolidation commit added tracked files after manifest generation.
Commit `b177542` regenerated the manifest from all 641 tracked files without weakening
the gate. CI `33286334704` passed repository-contracts, backend, frontend and Docker;
CodeQL `33286334733`, Publish Images `33286464960` and Deploy Production `33286506764`
also passed for the same SHA.
The final local `python scripts/verify.py --full` passed as well after Docker became
available (471 backend tests, frontend verify, Docker images and static gates).

**M12-004 is DONE (2026-08-30): duplicate repository clone consolidated.**
`C:\Users\Hamed\Documents\Qasedak` is canonical and is the clone connected to the
live GitHub `master`. The older Python clone was snapshotted byte-for-byte at
`C:\Users\Hamed\Documents\Python\qasedak-archive-20260830`, then its unique frontend,
server-adapter and design evidence was transferred. The verified original duplicate at
`C:\Users\Hamed\Documents\Python\qasedak` has now been deleted; no commit or push was
made.

M12-003 has since completed the standalone dashboard mapping through a live Penpot MCP
read; see the current handoff section above. The historical M12-004 merge evidence below
remains preserved as the state before that design approval.

## What this run delivered (M12-004)

- Canonical clone proof: `master` and `origin/master` resolve to
  `0cd57876b3a672fffc5b773bf7c40e2bfd00dbf9`; the Python clone's push dry-run returned
  `fetch first`.
- Recovery archive: `C:\Users\Hamed\Documents\Python\qasedak-archive-20260830`,
  exactly 27,029 files / 915,560,857 bytes, matching the source snapshot.
- Transferred public landing (three WebP assets), responsive dashboard shell/overview,
  account/help/onboarding/feature routes, shared design components, server session/API
  routes and the compatibility billing routes.
- Preserved the canonical clone's newer automations, billing, Instagram, Inbox search +
  CRM context, API clients, backend modules, CI/CD workflows, deployment scripts and
  tests. The older four conversation UI files remain available in the recovery archive
  because they predate the canonical M12 implementation.
- Added `landing.main` to the existing v1 Penpot manifest, retained `penpotRevision: null`,
  and copied M08-006/M08-007 sync records plus visual-review artifacts.
- `/api/v1/[...path]` forwards a legacy bearer header when no cookie exists and attaches
  HttpOnly session/workspace cookies to successful legacy login/workspace responses;
  logout clears the client-side token too.

## What this run delivered (M12-002)

### Backend
- `GET /api/v1/workspaces/{workspaceId}/contacts/by-identity?channel=…&identity=…`
  (`IContactQueries.FindByIdentityAsync` in `EfContactQueries`): returns the contact bound
  to a provider identity, resolving any `MergedIntoId` chain to the absorbing primary;
  404/`contact.notFound` when none. Same `ContactPayload` shape as the by-id endpoint
  (factored out). New e2e `ContactResolvesByProviderIdentityAndReturnsCrmSurface`.

### Frontend
- `src/shared/api/contacts.ts` — by-identity resolve (404 → `null`) + add/remove tag +
  add note. `src/features/contacts/presentation.ts` — copy + tag/note bounds.
- `[conversationId]/page.tsx` — the «اطلاعات گفتگو» panel now shows the contact's display
  name, removable tag chips + add-tag, and a notes timeline + add-note; a neutral empty
  state covers conversations whose CRM contact isn't materialized yet. The design's
  «غیرفعال» badge and the «تا تکمیل M07» warning are removed (M07 shipped).

### Sync evidence
- penpot-sync `inbox.conversations` notes updated, SCREEN-INVENTORY row updated, and a
  sync record `docs/design/sync/M12-002-thread-context-panel.md` written. Honest note: no
  fresh Penpot MCP read this session (MCP client unavailable) — reconciled against the
  extracted 2026-08-24 contract; `penpotRevision` stays `null`.

## What this run delivered (M12-001 — inbox search)

### Backend
- `SearchPattern` (Conversations.Application, `InboxQueries.cs`): trims the term and
  escapes `%` / `_` / `\` so LIKE wildcards in user input match literally; blank terms
  yield no filter.
- `EfConversationQueries.ListAsync`: applies the escaped term with `EF.Functions.ILike`
  over `ParticipantId` or any message body (`c.Messages.Any(...)`, EXISTS translation in
  PostgreSQL). No migration needed — no schema change.
- HTTP: optional `search` query param on `GET /api/v1/workspaces/{id}/conversations`;
  backward compatible, composes with `status` and paging.
- Tests: 8 new `InboxSearchTests` unit cases (Conversations suite 23/23); new API e2e
  `InboxListSupportsCaseInsensitiveSearchAcrossParticipantAndBodies` (participant match,
  case-insensitive body match, Persian terms, bare `%` → zero results, search+status
  composition, blank term = unfiltered) — ADDED but NOT RUN: Testcontainers needs the
  Docker daemon, which was down this session.

### Frontend
- `conversationsApi().list` forwards `search` (URLSearchParams encoding; blank omitted).
- `/dashboard/inbox`: search input live with 250 ms debounce, «فعلاً غیرفعال» badge
  removed, empty state distinguishes «گفتگویی با این عبارت پیدا نشد.» from the empty
  inbox copy. Contract tests updated in `tests/inbox.test.mjs`.

### Docs/state
- MILESTONES.md: M12 — v2 Product Features (retire v1 deferrals; Penpot sync contract
  applies to UI tasks).
- TASKS.md: M12-001 DONE; M12-002 (inbox thread context panel, contacts/tags/notes) and
  M12-003 (dashboard overview, blocked on approved design) TODO.
- `docs/design/sync/M12-001-inbox-search.md`: enabled-state divergence recorded (design
  only defined the disabled state; placeholder «جستجو در گفتگوها…»).
- SCREEN-INVENTORY.md inbox row updated; PROJECT_STATE.json, STATUS.md updated.

## Verification status

- Backend Release build 0 warnings/0 errors; `dotnet format --verify-no-changes` clean;
  all unit suites green: BuildingBlocks 12, Automations 44, Billing 119, Contacts 23,
  Conversations 23, Identity 79, Instagram 80 (380 total).
- Frontend `npm run verify` green after consolidation: lint, typecheck, 56/56 tests,
  production build (36 routes including landing, dashboard shell and compatibility
  routes).
- `python scripts/verify.py --full` was attempted: static gates, restore, format and
  Release build passed; 383 tests passed and 88 Testcontainers tests failed because the
  Docker endpoint `npipe://./pipe/docker_engine` is unavailable on this workstation.
- NOT run this session (honest residual): every Testcontainers integration/e2e suite
  (API integration incl. the new search scenario, billing/contacts/automations/identity/
  instagram Postgres suites) — Docker daemon is not running. Re-run once Docker is up.
- Not re-run this session: `check_architecture.py`, `validate_penpot_sync.py` (no
  manifest change), `verify.py --full`, rehearsals (unchanged since v1 freeze).

## Next actions for a human

1. Start Docker (Desktop or engine) so the Testcontainers suites — especially the new
   M12-001 search e2e — can run.
2. Operational go-live prerequisites (unchanged, not CI): real Mellat terminal
   credentials; Shaparak registration of the deployment public host; staging smoke incl.
   deliberate cancel and duplicate replay; Zarinpal staging smoke when ready.

## Next task for an agent

No product task is currently scheduled after M12-003. Keep the remaining operational
payment-provider go-live steps human-owned. Do not commit/push/tag unless explicitly
asked; suggested commits are recorded per task in TASKS.md.
