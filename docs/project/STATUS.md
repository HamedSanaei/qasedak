# Project status

**Project:** Qasedak
**Current milestone:** M13 — Instagram OpenReply Parity & Production Integration
**Current task:** M13-003 — Centralize versioned Meta Graph transport and failure taxonomy (TODO)
**Last completed:** M13-002 (2026-09-05; inbound routing correction included)
**Product implementation:** Conversations/automations bound to exact connected accounts with deterministic inbound resolution; no M13-003 work started

## 2026-09-05 — M13-002 routing correction DONE

- Post-completion audit found first-match inbound routing over rows including
  disconnected history. First-party Meta evidence proved Outcome A (OAuth
  user_id == professional IG_ID == webhook entry.id), so no second identity
  column was created and misleading app-scoped labels were corrected.
- Fix: one-query `ResolveActiveAccountAsync` (Resolved/NotFound/Ambiguous,
  active-only) in all three bridges; connect-time single-owner guard
  (`account.alreadyConnectedElsewhere`, 409); cross-workspace duplicates fail
  closed as Ambiguous; `entry.id` renamed to `ProviderAccountId` on integration
  events; unsafe `FindWorkspaceIdByProviderIdentityAsync` removed; additive
  partial routing index (`20260905015456_AddActiveRoutingIdentityIndex`).
- Tests: reconnect E2E verified failing pre-fix (0 threads) and green post-fix;
  plus disconnected-only, Ambiguous fail-closed, insertion-order independence,
  connect-guard units, PG resolution/index coverage — 506/506 backend green,
  `verify.py --full` green. Deployed below; production runtime moves to the
  correction image.

## 2026-09-05 — M13-002 DONE: exact channel-account binding shipped
(see deployment record in the next section)

## 2026-09-05 — M13-002 deployed; production on immutable task image

- Task commit `2fd1b3205d87bb10fda70c12789bc9c4168fae68` pushed to
  `origin/master`. CI `33933983002` success (4 jobs); CodeQL `33933983007`
  success; Publish Images `33934204827` success
  (`ghcr.io/hamedsanaei/qasedak-api|web:sha-2fd1b3205d87`); Deploy Production
  `33934275735` success for the exact SHA (previous `sha-6e5b912e4be7`, DB
  backup `qasedak-20260905T005032Z-sha-2fd1b3205d87.dump`, both M13-002
  migrations replayed, api/web Healthy, in-workflow smoke passed ~00:50Z).
- Independent public smoke at `https://qasedak.tofanservice.ir`: `/` 200,
  `/api/v1/system` 200, invalid-login `/web-api/auth/login` 401.
- Structural DB check via deployment-workflow evidence (migration run complete,
  no errors; this agent has no direct production SSH): `conversations` gained
  nullable `ChannelAccountId` + `IX_conversations_exact_thread`;
  `automations` gained nullable `ChannelAccountId` + workspace/account index.
- Live multi-account Meta mutation smoke: NOT RUN — no explicitly designated
  production test Instagram accounts; functional proof rests on deterministic
  unit, Testcontainers PostgreSQL and API E2E gates (495/495). No customer
  accounts touched, no DMs sent. Production safety signals available to this
  agent: deploy health/smoke green, no rollback triggered; server logs not
  directly accessible.

- `ChannelAccountId` opaque struct (BuildingBlocks.Domain, no provider types);
  Conversations natural key `(WorkspaceId, Channel, ChannelAccountId,
  ParticipantId)` enforced by `IX_conversations_exact_thread` (migration
  `20260905000206_AddChannelAccountId`, nullable uuid, legacy NULL rows
  readable but refused for outbound with `reply.accountUnresolved`);
  Automations create-time-immutable binding (migration
  `20260905000458_AddChannelAccountBinding`, purely additive; legacy unbound
  automations never execute; rebind = new automation).
- Inbound bridges resolve the exact `ConnectedAccount` and drop
  unknown/disconnected accounts without guessing; `InstagramReplyGateway`
  resolves by ID with workspace/state/path checks against only that account's
  token — first-active-account fallback deleted; refusals are stable 409s with
  zero fallback sends. Executor refuses binding mismatches pre-ledger.
- ADR-011 records design, legacy semantics and rollback analysis (automations
  migration fully compatible; conversations index replacement safe for rollback
  before multi-account rows exist — duplicate-triple check documented).
- Tests: 495/495 backend (incl. pre-migration-row upgrade, coexistence, 23505
  duplicate rejection, round-trips, 2-account isolation, exact tokens,
  foreign/disconnected/missing/unknown/legacy refusals, automation A/B
  isolation). Notable find fixed: v7-Guid 8-char test tags collide suite-wide
  (~65s window) against the global `mid` unique index — tags made fully unique
  plus a strict-200 webhook assert against 202-masked deferrals.
- Gates: `verify.py --full` green (static/restore/Release/format/backend
  Testcontainers/frontend 64/Docker images); `agent_finalize --task M13-002`
  green. Frontend untouched (additive `channelAccountId`). Deployed as
  `sha-2fd1b3205d87` — see deployment record above.

## 2026-09-05 — M13-001 follow-status correction DONE

- The 2026-09-04 "globally unsupported" follow-status conclusion was wrong. The
  official Instagram User Profile API (`GET graph.instagram.com/<IGSID>` →
  `is_user_follow_business`) was verified same-day from first-party Meta pages.
- Corrected: profile lookup SUPPORTED with user-consent constraints (sent
  message / icebreaker / persistent menu consented; raw comment fails
  officially); ordinary template-postback consent UNVERIFIED behind a
  capability switch. M13-011 conditional design kept (Cases A/B/C); M13-012
  stays decoupled. All other M13-001 conclusions preserved.
- Gates re-run green (docs/state/arch/env/Penpot/manifest/diff-check,
  agent_finalize, verify.py --full). Docs-only; production runtime unchanged
  (`sha-6e5b912e4be7`); ships as `[skip ci]` with no new deployment.

## 2026-09-04 — M13-001 DONE: current Meta Instagram contract reconciled

- Fresh audit against current official Meta pages (revisions March–August 2026,
  retrieved 2026-09-04; direct host fetch is bot-blocked, first-party pages read
  same-day via full-text index; Meta-owned Postman collection located as
  supplementary). Normative result: `docs/product/meta-instagram-platform-contract.md`
  (provider/identity matrix + all 20 instruction questions answered + residual
  assumptions for M13-003/008/010).
- Headline: Instagram Login is primary for messaging/Conversations/Private
  Replies/public replies/webhooks/media/insights; ADR-010 accepted, ADR-006
  messaging decision superseded (file preserved); capability matrix marked
  historical; OAuth lifecycle re-verified unchanged; SRS §4 updated; DECISIONS.md
  disambiguated; verdict notes added to M13-003/008/009/011. Follow status is
  officially unsupported (M13-011 branch 2 confirmed). Window signal is Graph
  10/2534022 (no official 490); read receipts are `read:{mid}`; latest observed
  Graph v26.0 (configured, not hardcoded).
- Zero production source/migration/package/test/secret changes (docs/state only).
- Gates: Graphify 0.9.26 code-only refresh + 2 budget-1200 queries + evidence;
  check_docs/state/architecture/environment/Penpot 6/6/manifest (662 files)/
  diff-check pass; `agent_finalize.py --task M13-001` pass;
  `verify.py --full` pass (static, restore, Release build, format, backend
  suites with Testcontainers, frontend verify, both Docker image builds).
- Environment notes: the local Docker daemon was down, so the first full run
  failed 52/55 Api.IntegrationTests; Docker Desktop was started locally and the
  rerun passed fully. `check_docs.py` transiently failed on a pre-existing
  untracked user export (`docs/fa/qasedak_m13_production_handbook_fa.html`,
  created 2026-09-05 04:08, SHA256
  `9E15231D6FF648F76D01115A57A9AB03E972E328891CC8267DE8260E96702AE1`); it was
  parked outside the repo for the gate run and restored byte-identical
  (hash-verified), so CI clean-checkout state is unaffected. The file remains
  untracked and unpushed by design.
- State: M13-001 DONE, lastCompletedTask=M13-001, currentTask=M13-002 (TODO).
  Next: commit/push/deploy per the M13-001 instruction, then M13-002.

## 2026-09-04 — M13-001 deployed; production on immutable task image

- Task commit `6e5b912e4be735df0aad773dbc8e0d2524d29085` pushed to
  `origin/master`. CI `33928409880` success (4 jobs); CodeQL `33928409979`
  success; Publish Images `33928610451` success
  (`ghcr.io/hamedsanaei/qasedak-api|web:sha-6e5b912e4be7`); Deploy Production
  `33928687404` success for the exact SHA (previous `sha-c989ccee330e`, DB
  backup taken, migrations replayed, containers Healthy, in-workflow smoke
  passed ~23:14Z).
- Independent public smoke at `https://qasedak.tofanservice.ir`: `/` 200,
  `/api/v1/system` 200, invalid-login `/web-api/auth/login` 401. No Instagram
  mutations performed. Production runtime = immutable `sha-6e5b912e4be7`;
  a later docs-only evidence commit does not change the deployed image.

## 2026-09-05 — M13 provider-conditional dependency refinement

- All M13-001 through M13-015 tasks remain TODO; current milestone/task remain
  M13/M13-001 and last completed remains M12-008. No implementation task was started.
- M13-012 no longer hard-depends on M13-011. Independent automation parity can complete
  when follow status is officially unavailable; it conditionally consumes M13-011's
  follow-gate capability only when verified and implemented.
- M13-011 now separates provider-independent opening Private Reply, postback validation,
  durable reveal/read-fallback orchestration and exact-account idempotency from the
  provider-dependent relationship lookup. Follow-status unavailability disables only
  the gate and does not block the otherwise supported flow.
- M13-013 now contains explicit Phase A comment reconciliation and Phase B Conversations
  history synchronization scopes; Phase B crosses through a channel-neutral import/upsert
  contract and is not coupled to automation behavior.
- M13-014 and M13-015 define parity only for capabilities supported by the current
  official Meta contract and intentionally included in Qasedak. Unsupported provider
  behavior must be omitted/disabled/classified truthfully, never simulated.
- Exact channel-account identity, distinct direct/Private/public operations, the global
  account+comment Private Reply claim, deterministic evaluator, PostgreSQL durable work
  and Clean Architecture/cross-module boundaries remain unchanged.
- Planning verification passed: agent preflight; Graphify 0.9.26 code-only refresh,
  re-cluster and budget-1200 query; document/state/architecture checks; Penpot manifest
  validation 6/6; manifest freshness at 662 files; `git diff --check`; and a bounded M13
  assertion proving 15 TODO tasks, the exact M13-012 dependency list and both M13-013
  phase headings. Docker/Testcontainers, live Meta and full implementation gates were not
  run because this instruction changed planning documentation only.

## 2026-09-05 — OpenReply parity milestone planned (M13-001 → M13-015 TODO)

- Added M13 with 15 ordered TODO tasks: current Meta-contract reconciliation; exact
  ConnectedAccount binding; common versioned Graph transport; PostgreSQL-native durable
  scheduled work; account enrichment/subscriptions/refresh; media; insights/follower
  history; webhook expansion; Private Reply correctness; interactive messaging;
  opening/postback/follow/read-fallback flow; automation parity; reconciliation/history
  import; Penpot-governed frontend/API integration; and final compliance/production gates.
- Scope was narrowed around completed M01–M08 work. Existing OAuth exchanges, AES-GCM
  protected token storage, account health primitives, challenge/HMAC verification,
  durable webhook inbox, normalized event boundary, Conversations/Contacts projections,
  24-hour normal reply path, automation versioning/evaluator/run ledger, and current
  frontend surfaces remain authoritative and are not duplicated.
- Repository inspection confirmed the critical corrections: conversation identity is
  currently `(workspace, channel, participant)`; inbound bridges resolve only workspace;
  `InstagramReplyGateway` chooses the first active Instagram-login account; automations
  are workspace-wide; and M06-005 sends a commenter through the normal `recipient.id`
  path rather than a comment-scoped Private Reply. No durable general scheduler, media/
  insights clients, subscription lifecycle, postback/read normalizers, or provider-history
  sync exists.
- OpenReply reference commit `f180d2db6381f0c37e4b29848ab97e77c18f610f`
  (2026-09-03) was inspected for behavior only. Current official Meta pages could not be
  fetched during this planning pass because the documentation host returned HTTP 429;
  M13-001 explicitly requires a fresh official-source reconciliation before code changes.
- Planning gates passed: document/state/architecture checks, Penpot manifest 6/6,
  repository-contract tests 2/2, manifest freshness, restore, Release build with 0
  warnings/errors, format, and all 380 unit tests. `python scripts/verify.py --full` was
  attempted but the Docker-backed PostgreSQL suites could not connect to
  `npipe://./pipe/docker_engine`; the script stopped there, so current-run frontend
  verification and Docker image builds were not reached.
- Planning-only instruction honored: no production code, migration, feature test,
  frontend behavior, dependency, commit, push, tag or deployment change was made.

## 2026-09-04 — Undone-task sweep: nothing registered TODO, ad-hoc screens verified

- All 57 task statuses in `docs/project/TASKS.md` are DONE; `PROJECT_STATE.json`
  lists all 57 as completed with `currentTask`/`lastCompletedTask` M12-008. Zero
  TODO/IN_PROGRESS/BLOCKED-as-status rows. Remaining items are human-operational
  only (Mellat terminal/Shaparak/Zarinpal go-live smokes, never in CI).
- Today's unregistered ad-hoc instruction (Directam-reference feature screens, no
  task ID, local-only) was verified, not re-implemented: `npm run verify` green
  (lint, typecheck, 64/64 tests incl. 4 new `features-penpot` cases, production
  build with all `/dashboard/features/*` + `/dashboard/smart-sms` routes);
  `check_architecture.py` passed (35 projects, 6 modules);
  `validate_penpot_sync.py` passed 6/6; `agent_finalize.py --task M12-008`
  passed and regenerated `FILE_MANIFEST.txt` (646 files, check passes).
- Backend tree has 0 changed files vs HEAD, so the backend suite
  (`verify.py --full`, 471-test Testcontainers gate) was not re-run; M12-008's
  full pass stands for the identical backend tree. Divergence noted: the
  navigation contract now nests 6 feature destinations under «امکانات» (12 unique
  destinations), superseding M12-008's recorded 7-destination count by direct
  human request. No commit or push was performed.

## 2026-08-30 — Customer dashboard navigation COMPLETE (M12-008)

- M08-007 Landing and M12-003 DashboardOverview are preserved; the dashboard contains
  customer/workspace actions only and no admin-only system or cross-workspace controls.
- One shared navigation contract now feeds the desktop Sidebar and mobile drawer. It has
  8 clickable link instances (7 unique destinations), all valid, with zero Sidebar 404s;
  nested active routing is covered deterministically.
- Accounts remains backed by live Identity/workspace APIs. Help was reconciled from a
  live MCP read of the permitted legacy Help board after the primary Qasedak file was
  confirmed to have no Help screen; it now has real local FAQ search and only real links,
  without invented tickets, chat hours or Smart SMS capability.
- Smart Answer and Comment Automation redirect to canonical Automations. Unsupported
  Cards, Follow-up, Form Maker, Ice Breakers and Smart SMS remain truthful unavailable
  states and are not active Sidebar destinations.
- A real Docker browser smoke reproduced the silent post-login navigation race: the
  cookie/backend login succeeded while the App Router stayed on `/login`. Successful
  login now performs a full `/dashboard` navigation; the Docker regression smoke passed.
- Exact authenticated review passed at 1440/1280/1024/768/390/360, including RTL,
  drawer open/close-on-navigation and Dashboard/Accounts/Help at 1440/390.
- Docker Desktop 4.86.0 / Engine 29.7.2 is healthy. Full backend discovery/execution is
  471/471 passed, 0 failed/skipped, with Testcontainers executed. Frontend verification
  is 60/60 passed plus lint/typecheck/build. Docker smoke passed `/`, same-origin
  `/api/v1/system`, register/login, dashboard and core routes. `verify.py --full` passed,
  including both Docker image builds. No commit or push was performed.

## 2026-08-30 — Penpot-synced dashboard overview COMPLETE (M12-003)

- The human-designated Penpot page was read live through the official MCP. Source board
  `Dashboard — Directam Reference` (`f6b8d46f-…-85ad24c7b3f3`) is now mapped as
  `dashboard.overview`, approved and synced at exposed file revision `281`.
- `/dashboard` now implements the reference's status rows, two-column 220px feature
  cards, full-width final feature and three-card lower section with responsive RTL CSS.
  Qasedak's real Workspace/Inbox state, authorization and existing routes remain
  authoritative; no external social URL, entitlement or connection state was invented.
- `npm run verify` passed lint, typecheck, 58 tests and production build;
  `validate_penpot_sync.py` passed 6/6 and architecture checks passed (35 projects,
  6 business modules). `agent_finalize.py` passed. The full gate also passed static,
  restore, format and Release build (0 warnings/errors), then stopped at PostgreSQL
  Testcontainers because local Docker is unavailable at `npipe://./pipe/docker_engine`.

## 2026-08-30 — Production auth proxy repair COMPLETE (M12-007)

- Production routes public `/api/` traffic directly to ASP.NET Core, so the M12-006
  Next.js handlers under `/api/auth/*` and `/api/workspace` were unreachable after
  deployment. The web-owned routes and callers now use `/web-api/*`.
- The production deployment smoke now POSTs invalid credentials through
  the public `/web-api/auth/login` route and require HTTP 401, covering both reverse-proxy
  routing and the internal Web-to-API auth path.
- Commit `7e72322` passed CI `33311180968`, CodeQL `33311180901`, image publish
  `33311326079` and production deploy `33311381218`. The public auth-routing smoke passed
  at `https://qasedak.tofanservice.ir` on the running immutable release.

## 2026-08-30 — Registration/login session flow COMPLETE (M12-006)

- The active `/login` and `/register` pages now use same-origin web-owned auth/workspace
  handlers. Successful auth establishes the server-owned HttpOnly
  session/workspace cookies before dashboard navigation; the short-lived bearer value is
  retained only for the existing client-feature compatibility bridge.
- Auth and workspace failures are rendered as Persian form-level errors, including the
  previously silent missing-session/token path. Frontend auth regression coverage was
  added and the full repository verification gate passed.

## 2026-08-30 — GitHub Actions manifest gate repair COMPLETE (M12-005)

- The failed run `33284839710` was isolated to `repository-contracts / Ensure manifest is
  current`: the consolidation commit had 641 tracked files, while the manifest had been
  generated before the newly transferred files were staged.
- Commit `b177542` regenerated the manifest from the final tracked tree without changing
  CI contracts or weakening quality assertions. CI run `33286334704` passed all jobs,
  including Docker; CodeQL, Publish Images and Deploy Production also passed for the
  same SHA.
- The final local `python scripts/verify.py --full` also passed once Docker became
  available: restore, Release build, format, 471 backend tests, frontend verification,
  Docker image builds and static contracts all passed.

## 2026-08-30 — Duplicate clone consolidation COMPLETE (M12-004 → DONE)

- Canonical Git state is `C:\Users\Hamed\Documents\Qasedak`: `master` and
  `origin/master` both resolve to GitHub SHA `0cd57876b3a672fffc5b773bf7c40e2bfd00dbf9`.
  The Python clone was a stale independent checkout; its push dry-run was rejected with
  `fetch first`.
- A byte-for-byte recovery archive was created at
  `C:\Users\Hamed\Documents\Python\qasedak-archive-20260830` (27,029 files,
  915,560,857 bytes). Unique landing, dashboard shell/overview, server session/proxy,
  feature routes, design primitives, sync records and visual-review evidence were
  transferred selectively.
- After source/archive verification, the duplicate active clone at
  `C:\Users\Hamed\Documents\Python\qasedak` was deleted. The recovery archive remains.
- The canonical clone's newer backend, CI/CD/deployment workflows, automations, billing,
  Instagram, Inbox search/context, API clients and tests remain authoritative. The
  `/api/v1` proxy now bridges legacy bearer headers with server-owned HttpOnly cookies.
- Frontend verification passes (lint, typecheck, 56 tests, production build). Release
  backend build passes and static architecture/manifest/state gates pass. The full
  `verify.py --full` gate was attempted; 383 tests passed and 88 Testcontainers tests
  failed only because Docker is unavailable at `npipe://./pipe/docker_engine`.
  The
  standalone dashboard design approval remains a follow-up (M12-003); no fresh Penpot
  MCP read was possible during this merge, so imported sync evidence is explicitly
  recorded as such.

## 2026-08-29 — Inbox thread context panel COMPLETE (M12-002 → DONE)

- Backend: read-only workspace-scoped lookup `GET /api/v1/workspaces/{id}/contacts/by-identity`
  (`IContactQueries.FindByIdentityAsync`, resolving `MergedIntoId` chains) so a conversation's
  `(channel, participantId)` resolves to its CRM contact; reuses the by-id detail payload.
  New e2e `ContactResolvesByProviderIdentityAndReturnsCrmSurface` (resolve → tag/note
  mutations reappear on re-resolve, 404 for unknown identity, 400 for missing params, 403
  for foreign workspace).
- Frontend: `src/shared/api/contacts.ts` (resolve + tag/note mutations), `src/features/contacts/presentation.ts`
  (copy + validation), and the thread page `[conversationId]/page.tsx` renders the
  «اطلاعات گفتگو» panel as a live CRM surface — contact name, removable tag chips + add-tag,
  notes timeline + add-note, and a neutral empty state when no contact exists yet. The
  design's «غیرفعال» badge and the «Tags و Notes تا تکمیل M07 …» warning are gone (M07 shipped).
- Sync: penpot-sync `inbox.conversations` notes updated + SCREEN-INVENTORY row + sync record
  `docs/design/sync/M12-002-thread-context-panel.md`. No fresh Penpot MCP read this session
  (MCP client unavailable) — reconciled against the extracted 2026-08-24 contract.
- Gates: backend Release build 0 warnings/0 errors, full backend suite 471/471, `npm run verify`
  47/47, validate_penpot_sync + check_architecture + check_environment_contract all PASS.

## 2026-08-28 — Server-side inbox search COMPLETE (M12-001 → DONE)

- Backend: `SearchPattern` (Conversations Application) trims search terms and escapes
  LIKE wildcards (`%`/`_`/`\`) so user input matches literally; blank terms remove the
  filter. `EfConversationQueries.ListAsync` applies the term with `EF.Functions.ILike`
  over the counterpart identity or any message body (EXISTS translation).
- HTTP surface: optional `search` query param on
  `GET /api/v1/workspaces/{id}/conversations`, composing with `status` and paging.
- Frontend: `/dashboard/inbox` search is live (250 ms debounce), the «فعلاً غیرفعال»
  badge is removed, empty state distinguishes no-results from empty inbox; client
  contract tests updated.
- Tests: 8 new `InboxSearchTests` unit cases (Conversations suite 23/23); a new API e2e
  scenario (`InboxListSupportsCaseInsensitiveSearchAcrossParticipantAndBodies`) is ADDED
  but NOT executed — Docker daemon was down this session (honest residual).
- Gates: backend Release build 0 warnings, `dotnet format --verify-no-changes` clean,
  all unit suites green (380); frontend `npm run verify` green (37 tests incl. search
  contract, lint/typecheck/build).
- Sync evidence: `docs/design/sync/M12-001-inbox-search.md` (enabled-state divergence:
  placeholder «جستجو در گفتگوها…» — the design only defined the disabled state);
  SCREEN-INVENTORY inbox row updated; MILESTONES.md gained M12 (v2 Product Features);
  TASKS.md gained M12-001 DONE + M12-002/M12-003 TODO.

## 2026-08-24 — Behpardakht Mellat live transport COMPLETE (M09-002 → DONE)

- Vendor contract arrived in-repo: `docs/vendor/behpardakht/BEHPARDAKHT-IPG-v1.29-EN.md`
  (User Guide v1.29 EN translation, "Unofficial - External" provenance preserved; newer
  conflicting onboarding docs ⇒ future ADR). Used as the SOLE protocol source.
- `BehpardakhtSoapClient`: explicit SOAP 1.1 envelopes for bpPayRequest/bpVerifyRequest/
  bpSettleRequest/bpInquiryRequest/bpReversalRequest; XML-escaped params; namespace-agnostic
  response parsing; fault/HTTP/timeout → typed Unavailable. No SOAP types escape Infrastructure.
- Gateway orchestration: pay per §8 (IRR unchanged, payerId 0, deterministic orderId persisted
  as new `ProviderOrderId` column + migration), exact-case RefId persisted; jump endpoint
  `/api/v1/payments/mellat/startpay` auto-posts only RefId to startpay.mellat; POST form
  callback normalized to OK/CANCEL/FAILED with mandatory identity check BEFORE verification
  (SaleOrderId must equal stored ProviderOrderId; mismatch → `payment.callbackRejected`,
  zero bank calls, audited); verify→settle chain with idempotent 43/45, bounded §19 code
  classifier, Inquiry reconciliation of unknown outcomes, reversal ≤ ~3h post-verify on the
  concrete gateway only. Callback values never prove payment; entitlement exactly once intact.
- Typed options extended (`ServiceUrl`/`PaymentPageUrl`/`ServiceNamespace`, overridable);
  `.env.example`/docker-compose/appsettings aligned; docs/08 §6 rewritten as implemented +
  operational go-live prerequisites; ADR-009 updated to reference the vendor doc path.
- Tests: billing unit 119/119 (new envelope/parsing/classifier/orchestration/callback-validation
  suites); API e2e over real host + PostgreSQL + scripted SOAP fake: jump redirect + persisted
  ProviderOrderId, jump page HTML carries exact RefId and no credentials, form callback
  activates exactly once (verify+settle once, duplicate harmless), forged SaleOrderId rejected
  without any bank call or entitlement. Full backend suite green (458 tests).

## 2026-08-24 — Payment architecture (M09-002 executable scope) COMPLETE

- `PaymentAttempt` aggregate (Pending→Verified|Failed), xmin optimistic concurrency,
  unique filtered Authority index = anti-replay; verified payment extends entitlement
  exactly once; callback queries alone never activate anything.
- Provider-neutral `IPaymentGateway` in Application; Infrastructure owns protocols:
  `ZarinpalPaymentGateway` implements the CURRENT official v4 REST contract
  (request.json/verify.json, code 100/101 semantics, StartPay redirect); typed options;
  secrets server-side only; merchant id/secrets/payloads/card PAN never logged.
- **Provider decision updated same day (ADR-009): Bank Melli/SADAD CANCELLED; Behpardakht
  Mellat selected.** `BehpardakhtMellatPaymentGateway` (`providerId="mellat"`) is a
  fail-closed boundary with typed `BehpardakhtOptions`; enabling without the verified
  current official contract surfaces `payment.providerUnavailable` naming exactly which
  documents are required. Historical bpPayRequest/bpVerify/bpSettle flow treated as
  background only — nothing copied into transport.
- Endpoints: plans catalog, workspace subscription, checkout (202 + server-owned
  redirect), payment status/history, public provider callback → 302 to frontend result
  page. Migration `AddPaymentsAndPlanPrices`; env contracts in `.env.example`,
  docker-compose and deployment guide §6 (`MELLAT_*`); ADR-008 + ADR-009 accepted.
- Penpot Checkout boards updated in-file via MCP: «پرداخت مستقیم بانک ملی» → «به‌پرداخت
  ملت» on Desktop+Mobile; frontend reconciled; design system unchanged.
- Tests: Billing unit 61/61; Billing integration (Testcontainers) incl. concurrent
  verify exactly-once 9/9; full Api.IntegrationTests 46/46.

## 2026-08-24 — Final Penpot designs reconciled into the app

- Codex completed four new `Qasedak ·` pages in the canonical file
  (`c269caa0-e456-818c-8008-85a77340be64`); all boards live-inspected via MCP.
- Extracted contract: `docs/design/sync/2026-08-24-qasedak-final-designs.md`;
  sync record: `docs/design/sync/2026-08-24-qasedak-final-sync-record.md`.
- Manifest updates (validated 6/6): `identity.auth` draft→**approved** on
  `Qasedak · Identity & Workspace`; NEW `inbox.conversations` **approved** (removes the
  historical M08-004 no-design blocker; evidence preserved); NEW `billing.payment`
  **approved** across Plans/Subscription/Checkout/Results boards.
- Frontend: auth screens visually reconciled (email+password behavior untouched);
  inbox reconciled (search disabled BY DESIGN until backend query ships); new billing
  UI `/dashboard/billing`, `/dashboard/billing/checkout`, `/dashboard/billing/result`
  with server-authoritative IRR amounts and bounded status polling; new
  `tests/billing.test.mjs`. `npm run verify` green.

## Next action

1. Operational (human, not CI): Mellat go-live per docs/08 §6 — real terminal credentials,
   Shaparak registration of the deployment's public host (IP allowlist; callback path +
   jump page inside the registered domain), staging smoke incl. deliberate cancel and
   duplicate replay; same for a Zarinpal staging smoke when its merchant account is ready.
2. Continue M09 with the next task in TASKS.md.

## Baseline established

- Modular Monolith backend boundary defined.
- Clean Architecture inside each module: Infrastructure → Application → Domain.
- ASP.NET Core Web API composition root scaffolded.
- Independent Next.js frontend scaffolded for future Penpot implementation.
- PostgreSQL 18 deployment baseline defined with module-owned logical schemas.
- CI, image publishing, CodeQL and Dependabot workflows scaffolded.
- Architecture/state/documentation guard scripts scaffolded.
- English engineering document set and Persian printable HTML document set created.
- Milestones/tasks and multi-agent handoff protocol created.

## Engineering foundation verified (M00)

### Graphify (M00-003)

- Graphify CLI 0.9.26 healthy; mode is code-only (local AST): no LLM API key on this
  machine; doc semantic extraction stays unavailable until a key is provided, then
  re-run without `--code-only`.
- Evidence recorded per task in `.agent-state/GRAPHIFY_EVIDENCE.md`.

### Toolchain and gates (M00-004)

- Toolchain resolved: .NET SDK 10.0.302, Node 24/npm 11, Docker engine 29.7.2. TypeScript pinned to 6.0.3 because the installed typescript-eslint hard-fails on TS ≥ 7.
- Dependencies locked: `package-lock.json` committed; frontend Dockerfile and CI use `npm ci`.
- All local gates green: backend Release build 0 warnings/0 errors, format check pass; frontend lint/typecheck/test/build pass; Docker images build successfully.
- `generate_manifest.py` ignores gitignored runtime artifacts and `verify.py` resolves npm correctly on Windows.

## Meta feasibility & contracts verified (M01)

- `docs/product/instagram-mvp-capability-matrix.md` — capability rows grounded in official Meta docs; comment→DM is Private-Reply-only; messaging requires the Messenger Platform path.
- `docs/product/meta-oauth-token-lifecycle.md` — full OAuth flow, scopes, token lifecycle, module ownership.
- Webhook authenticity: Application ports + Infrastructure HMAC/challenge implementations; ADR-006 (integration paths) and ADR-007 (webhook authenticity) accepted.
