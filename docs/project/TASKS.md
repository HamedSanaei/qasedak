# Task tracker

This file is the human-readable task source of truth. Every task has an explicit status and suggested commit. Agents must keep this file synchronized with `.agent-state/PROJECT_STATE.json`.

Status values: `TODO`, `IN_PROGRESS`, `BLOCKED`, `DONE`.

## M00-001 — Bootstrap modular repository
**Status:** DONE

**Outcome:** Create backend/frontend boundaries, module projects, Docker/GitHub baseline and guard scripts.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `chore(repo): bootstrap qasedak modular monolith`

## M00-002 — Establish documentation and agent-state baseline
**Status:** DONE

**Outcome:** Create engineering docs, Persian docs, ADRs, milestones, task tracker and multi-agent contract.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `docs(architecture): establish qasedak engineering baseline`

## M00-003 — Initialize and verify Graphify
**Status:** DONE

**Outcome:** Create backend/frontend boundaries, module projects, Docker/GitHub baseline and guard scripts.

**Completion evidence:** Graphify 0.9.26 verified; first real graph created (`graphify . --no-viz --code-only`: 277 nodes/297 edges/43 communities) plus `graphify cluster-only .` for `graphify-out/GRAPH_REPORT.md`; four bounded queries (--budget 1200) run on module dependencies, composition root, Next.js boundary and state system; four healthy evidence rows appended to `.agent-state/GRAPHIFY_EVIDENCE.md`; `agent_preflight.py --task M00-003` passes. code-only mode used because no LLM API key exists in the environment; doc semantic extraction remains unavailable until a key is provided.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `chore(graphify): initialize repository knowledge graph`

## M00-004 — Lock dependencies and green all gates
**Status:** DONE

**Outcome:** Resolve toolchains; generate npm lockfile; restore/build/test .NET; run frontend checks; build both Docker images; make CI green without weakening gates.

**Completion evidence:** `package-lock.json` committed and frontend Dockerfile/CI switched to `npm ci`; TypeScript pinned 7.0.2→6.0.3 (installed typescript-eslint hard-fails on TS≥7); missing `using Xunit;` added to both backend test projects; underscore test-method names renamed to satisfy CA1707 under TreatWarningsAsErrors; minimal `.dockerignore` files added for backend/frontend. Gates on workstation: `dotnet build -c Release` 0 warnings/0 errors; `dotnet format --verify-no-changes` pass; `dotnet test` 3/3 pass; `npm run verify` (lint/typecheck/test/build) pass; `docker build` qasedak-api:verify and qasedak-web:verify pass. `generate_manifest.py` now ignores gitignored runtime artifacts (`cache` dirs, `tsconfig.tsbuildinfo`) so the committed manifest matches fresh CI checkouts.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `chore(ci): lock baseline dependencies and green all gates`

## M01-001 — Define Instagram MVP capability matrix
**Status:** DONE

**Outcome:** Verify desired automations against current official Meta API capabilities, permissions, review requirements and policy constraints.

**Completion evidence:** `docs/product/instagram-mvp-capability-matrix.md` created; every capability row grounded in official Meta docs fetched during this task (webhooks requirements table, private replies rules, 24-hour window/Human Agent tag policy, business login scopes/token lifetimes) with inline citations. Key verified constraints: comment→DM only as Private Reply (one message per comment, 7-day window, Live during broadcast only); messaging path requires Messenger Platform + Facebook Login tokens; `comments`/`live_comments` webhooks need Advanced Access, Live app and public account.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `docs(product): define instagram automation mvp capability matrix`

## M01-002 — Define Meta OAuth and token lifecycle
**Status:** DONE

**Outcome:** Specify OAuth flow, permission model, token protection, refresh/expiry/revocation and workspace ownership.

**Completion evidence:** `docs/product/meta-oauth-token-lifecycle.md` created from official Meta docs (fetched as canonical Markdown): authorize URL/params, code→short-lived→long-lived (60d) exchanges at `graph.instagram.com/access_token`, refresh via `refresh_access_token` with verified preconditions (≥24h old, valid, `instagram_business_basic`), permanent expiry after 60d without refresh, `instagram_business_*` scope family, module ownership split (Identity=workspaces, Instagram=connected accounts+encrypted tokens), health-state surface and operational refresh rules; open questions OQ-1..3 routed to M03-001/M03-004.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `docs(instagram): define meta oauth and token lifecycle contract`

## M01-003 — Spike webhook verification contract
**Status:** DONE

**Outcome:** Implement/test a minimal deterministic webhook verification and fixture contract without introducing feature persistence.

**Completion evidence:** Ports `IWebhookSignatureVerifier`/`IWebhookSubscriptionValidator` in `Qasedak.Modules.Instagram.Application.Webhooks`; HMAC-SHA256 verifier (constant-time compare, strict `sha256=<lowercase hex>` header grammar) and subscription-challenge validator in `Qasedak.Modules.Instagram.Infrastructure.Webhooks`, registered in `AddInstagramModule`. New test project `Qasedak.Modules.Instagram.UnitTests` (added to solution) passes **20/20** deterministic tests over committed JSON fixtures — including a raw-bytes escaped-unicode payload locking Meta's documented signing behavior — plus tamper/wrong-secret/malformed-header/negative-handshake cases. No persistence, no HTTP endpoints; Release build 0 warnings, format check clean.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `test(instagram): add webhook verification contract spike`

## M01-004 — Finalize Meta integration ADRs
**Status:** DONE

**Outcome:** Record decisions/constraints learned from feasibility work and update SRS/architecture.

**Completion evidence:** `docs/architecture/ADR-006-meta-integration-paths.md` (dual connection paths, Private-Reply-only comment→DM with comment-ID idempotency, Human Agent tag as operator action, Advanced Access/Business Verification as M11 external dependency) and `docs/architecture/ADR-007-webhook-authenticity.md` (raw-bytes HMAC-SHA256 + constant-time compare, challenge handshake, fixture contract proven by the M01-003 spike) accepted. SRS §4 extended to bind Meta-facing behavior to the verified contracts; capability matrix and OAuth lifecycle docs referenced as normative companions.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `docs(adr): finalize meta integration decisions for mvp`

## M02-001 — Model workspace membership domain
**Status:** DONE

**Outcome:** Define users/workspaces/memberships/roles and invariants.

**Completion evidence:** Identity Domain implemented: `User` aggregate with `EmailAddress` value object (canonicalization + conservative shape validation), `Workspace` aggregate root owning `Membership` entities with `MembershipRole` (Owner/Admin/Member, privilege-ordered). Invariants enforced atomically by the aggregate: at-least-one-owner (creation seeds owner; last-owner demotion/removal rejected; persisted state without owner rejected), no duplicate memberships per user/workspace, actor-privilege rules for membership management, Owner role only grantable via explicit ownership transfer (source-must-be-self). New `Qasedak.Modules.Identity.UnitTests` passes **47/47** deterministic tests; solution build 0 warnings; format clean; architecture check 26 projects OK. Domain events deliberately deferred until an integration consumer exists.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(identity): model workspace membership domain`

## M02-002 — Implement authentication use cases
**Status:** DONE

**Outcome:** Implement bounded application use cases and security contracts.

**Completion evidence:** `RegisterUserUseCase` and `AuthenticateUserUseCase` in Identity Application over ports `IUserRepository`/`IPasswordHasher`/`ISecurityTokenIssuer`, with stable failure codes (`auth.invalidEmail|invalidDisplayName|emailTaken|weakPassword|invalidCredentials`) and a password policy (10–128 chars, not all alphanumeric). Unknown-email logins burn an equalizer hash so unknown-email and wrong-password are indistinguishable. Infrastructure adapters: PBKDF2-SHA256 hasher (210k iterations floor, per-hash salt, self-describing format, constant-time verify) and HMAC-SHA256 compact token issuer/validator (constant-time signature check, clock-injected expiry). 32 new tests; suite total 102 passing; build 0 warnings; format clean; no plaintext credential ever persisted or logged.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(identity): implement authentication use cases`

## M02-003 — Persist identity/workspace state
**Status:** DONE

**Outcome:** Implement EF Core/PostgreSQL identity schema and integration tests.

**Completion evidence:** `IdentityDbContext` owns the module's `identity` schema — tables `users` (unique canonical email), `user_credentials` (persistence-only password-hash record), `workspaces`, `memberships` (unique `(WorkspaceId, UserId)`, role stored as int, cascade to users and workspaces). Value conversions keep `EmailAddress`/`WorkspaceName` value objects and membership roles out of the persistence model; memberships materialize through the aggregate backing field. `EfUserRepository`/`EfWorkspaceRepository` implement the Application ports; design-time factory enables `dotnet ef`; committed initial migration `InitialIdentityCreation`. New Testcontainers project runs 5 integration tests against real PostgreSQL 18: migrate-apply, user+credential roundtrip, duplicate-email rejection, duplicate-membership rejection at schema level, workspace-delete cascade. Suite total 107 passing, build 0 warnings; `Microsoft.EntityFrameworkCore.Relational` pinned to 10.0.11 in CPM to kill a floating-version conflict.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(identity): persist users workspaces and memberships`

## M02-004 — Enforce workspace authorization
**Status:** DONE

**Outcome:** Apply workspace authorization policies at server boundaries with negative-path tests.

**Completion evidence:** `SecurityTokenAuthenticationHandler` ("QasedakBearer" scheme) turns valid `ISecurityTokenIssuer` tokens into ClaimsPrincipals (NameIdentifier + email claims); invalid/missing tokens challenge to plain 401. Identity endpoint group (`register`, `login`, `me`) and workspaces group (`POST /api/v1/workspaces`, `GET /api/v1/workspaces/{id}/members`) enforce the scheme via `RequireAuthorization(AuthenticationSchemes=…)` at the HTTP boundary; `CreateWorkspaceUseCase` (creator becomes Owner) and `ListWorkspaceMembersUseCase` map `workspace.notFound`→404 and `workspace.forbidden`→403. Token signing-key configuration resolves per use so an unconfigured host still boots health endpoints while failing loudly on first token operation; the handler treats that as 401, not 500. 7 API integration tests run the real host against real PostgreSQL 18 (Testcontainers) covering 201/200 happy paths plus no-token, garbage-token, wrong-password, non-member and unknown-workspace negatives; suite total 112 passing, build 0 warnings, format clean.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(identity): enforce workspace authorization policies`

## M03-001 — Implement Meta OAuth adapter
**Status:** DONE

**Outcome:** Create Infrastructure Meta OAuth adapter behind application ports with deterministic contract tests.

**Completion evidence:** Application ports `IAuthorizationUrlBuilder`/`IMetaOAuthClient` in Instagram Application (`InstagramAuthorizationScopes` pinned to the verified `instagram_business_*` set). Infrastructure `InstagramAuthorizationUrlBuilder` emits the documented authorize URL (client_id, redirect_uri, response_type=code, comma scopes, anti-CSRF state — OQ-1 resolved: official query-string table confirms `state` is supported and echoed back). `GraphInstagramOAuthClient` implements the verified token contract: POST `api.instagram.com/oauth/access_token` form exchange (data-array payload parsing), GET `graph.instagram.com/access_token` (`ig_exchange_token`) and `/refresh_access_token` (`ig_refresh_token`); failures are structured results (`RejectedByMeta`/`TransportFailure`/`MalformedResponse`) that never throw and never echo secrets/tokens (redaction tested). Endpoint correction captured in the lifecycle doc (code exchange is api.instagram.com, not graph.instagram.com). OQ-2 resolved with citation: FB Page tokens from long-lived User tokens never expire on schedule — no refresh scheduling for the FB path; invalidation detected via API errors (feeds M03-004). 11 new deterministic contract tests (scripted HttpMessageHandler, zero live Meta calls); suite total 123 passing, build 0 warnings, format clean.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(instagram): add meta oauth infrastructure adapter`

## M03-002 — Implement account lifecycle
**Status:** DONE

**Outcome:** Connect/disconnect/inspect Instagram professional accounts as application use cases.

**Completion evidence:** Domain aggregate `ConnectedAccount` (path discriminator per ADR-006: InstagramLogin vs FacebookLogin; scope snapshot; health enum `Connected|ExpiringSoon|Expired|Revoked|Unhealthy`; token expiry metadata only — raw tokens never enter the domain) with guarded transitions (`ApplyTokenRotation`, `MarkRevoked/Unhealthy/Expired/ExpiringSoon`, terminal `Disconnect`), `InstagramDomainException` with stable rule codes, and `FromState` rehydration. Application use cases over new ports `IConnectedAccountRepository`/`IProtectedTokenStore`: `ConnectInstagramAccountUseCase` (code→short-lived→long-lived via the M03-001 adapter, duplicate-provider guard, raw token stored only in the protected store, expiry computed from injected clock), `DisconnectInstagramAccountUseCase` (deletes token material, records terminal state), `ListWorkspaceConnectionsUseCase` (token-free projections per the §6 contract sketch). Use-case DI registration intentionally deferred to M03-003 with their repository implementations to keep host scope validation green. 8 new unit tests with fakes (happy paths, OAuth rejection/transport failures write nothing, duplicates, double-disconnect, token-free listing); suite total 131 passing, build 0 warnings, format clean.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(instagram): add account connection lifecycle`

## M03-003 — Persist accounts and protected tokens
**Status:** DONE

**Outcome:** Create Instagram schema and secure token storage/rotation abstraction.

**Completion evidence:** `InstagramDbContext` owns the module's `instagram` schema — `connected_accounts` (path discriminator int conversion, health int, comma-joined scope snapshot via List-backed value conversion, partial unique index on `(WorkspaceId, ProviderUserId)` filtered to `DisconnectedAtUtc IS NULL` so reconnection after disconnect is allowed) and `account_tokens` (AccountId PK, opaque ciphertext only). Committed migration `InitialInstagramCreation` with design-time factory. `EfConnectedAccountRepository` implements the Application port (tracked loads for mutation paths, no-tracking for reads). Protection: new `ITokenProtector` port with `AesGcmTokenProtector` adapter — AES-GCM 256-bit key, random 96-bit nonce, 128-bit tag, blob base64(nonce‖ct‖tag), runtime-injected key per secret policy (validated lazily at first use); `ProtectedTokenStore` persists only ciphertext and replaces it atomically on rotation; disconnect hard-deletes rows (`ExecuteDeleteAsync`). Lifecycle use cases now fully DI-registered. New Testcontainers project with 5 real-PostgreSQL tests: end-to-end connect persists account + encrypted token (plaintext asserted absent from the row), reconnect-after-disconnect allowed by schema, active duplicate rejected by partial unique index, rotation swaps ciphertext+expiry atomically, disconnect removes token row. Suite total 136 passing, build 0 warnings, format clean.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(instagram): persist connected accounts securely`

## M03-004 — Manage token health/revocation
**Status:** DONE

**Outcome:** Detect unhealthy/revoked access and surface actionable state.

**Completion evidence:** New `IMetaTokenInspector` port + `GraphInstagramTokenInspector` adapter (GET graph.instagram.com/me as cheapest authenticated probe) mapping Meta's error payload to the OQ-3 taxonomy: 190+"expired"→Expired; 190 subcodes 463/467 or deauthorization→Revoked; codes 10/200→PermissionLoss (surfaced Unhealthy); rate limits 4/17/32, HTTP 429/5xx, transport and unknown shapes→Transient (health deliberately untouched). `EvaluateAccountHealthUseCase` persists the resulting aggregate state: local expiry rule short-circuits without a network call, missing token material is an actionable Unhealthy fault, healthy inspection inside the ≤7-day window flags ExpiringSoon for refresh scheduling, transient outcomes leave persisted health unchanged. Disconnected/unknown accounts return notFound. OQ-3 resolution recorded in the lifecycle doc §7. 17 new tests (taxonomy fixtures + evaluation paths with scripted inspector); suite total 159 passing, build 0 warnings, format clean.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(instagram): manage token health and revocation`

## M04-001 — Verify Meta webhook requests
**Status:** DONE

**Outcome:** Implement endpoint challenge/signature/authenticity rules per verified Meta contract.

**Completion evidence:** `MetaWebhookEndpoints` maps `GET/POST /api/v1/webhooks/instagram`: GET performs the subscription handshake through `IWebhookSubscriptionValidator` (challenge echoed verbatim as text/plain, failures → 403); POST enforces `X-Hub-Signature-256` HMAC over the exact raw received bytes via `IWebhookSignatureVerifier` — missing/bad signature → 401 with an empty body (no content echo), signed-but-non-JSON → 400, oversized payloads → 413 before signature work (1 MB cap). New Application boundary `IMetaWebhookIngester` + `MetaWebhookNotification` keeps transport types out of Application; `DiscardingWebhookIngester` placeholder is registered until M04-002's durable inbox replaces it. Endpoints mounted in Program.cs; fixture now configures real verify-token/app-secret. 7 new end-to-end tests over real HTTP (handshake happy/wrong-mode/wrong-token; POST valid-signature/bad-signature-no-echo/no-header/oversized). Suite total 166 passing, build 0 warnings, format clean.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(instagram): verify meta webhook requests`

## M04-002 — Create durable idempotent webhook inbox
**Status:** DONE

**Outcome:** Persist event identity/body metadata and guarantee duplicate-safe ingestion.

**Completion evidence:** `WebhookInboxEntry` + `webhook_inbox` table in the instagram schema: event identity is the SHA-256 of the exact raw body (Meta redeliveries are byte-identical), so the primary key enforces at-most-once receipt; row carries topic, canonical body JSON, ReceivedAtUtc, status (pending/processed), ProcessedAtUtc and DeliveryAttempts; index on (Status, ReceivedAtUtc) for later dispatch scans; committed migration `AddWebhookInbox`. `InboxWebhookIngester` implements `IMetaWebhookIngester`: unknown identities insert-and-accept, known ones record a redelivery attempt and accept as no-op, concurrent same-identity races are caught via DbUpdateException and still report accepted — duplicate-safe under retries and parallel delivery. The M04-001 placeholder ingester was removed and DI now binds the real inbox. 3 new real-PostgreSQL tests (first-delivery persistence with pending state, identical redelivery swallowed with attempt counter, distinct payloads → distinct rows). Suite total 169 passing, build 0 warnings, format clean.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(instagram): add idempotent webhook inbox`

## M04-003 — Normalize integration events
**Status:** DONE

**Outcome:** Translate raw Meta payloads to explicit integration events without leaking transport models into Domain.

**Completion evidence:** Application-level `MetaPayloadNormalizer` translates canonical Meta bodies into explicit, transport-free events: `InstagramMessageReceived` (echoes skipped), `InstagramCommentCreated`, `InstagramMentionCreated`; unknown fields and messaging-without-message surface as `UnrecognizedWebhookFragment` and malformed JSON yields one fragment instead of throwing — nothing is dropped silently. `IWebhookInboxStore` port exposes pending entries to Application (`EfWebhookInboxStore` adapter over webhook_inbox with ExecuteUpdate closing); `ProcessPendingWebhookEventsUseCase` consumes a bounded batch: normalize → dispatch each event via new `IIntegrationEventDispatcher` port → close entry (unrecognized fragments never block closing; raw body stays durable in the inbox). Infrastructure ships `LoggingIntegrationEventDispatcher` (LoggerMessage source-generated structured log carrying event id + provider identity as correlation) until real consumers arrive. API fixture now provisions the Instagram connection string + migrations so the endpoint→inbox path is exercised for real. 9 new tests (6 normalizer shape fixtures incl. echo-skip/malformed JSON, 3 use-case tests incl. batch bound and dispatch-before-close invariant). Suite total 172 passing, build 0 warnings, format clean.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(instagram): normalize webhook integration events`

## M04-004 — Instrument webhook processing
**Status:** DONE

**Outcome:** Add correlation, structured logs, metrics and failure/retry visibility.

**Completion evidence:** New module meter `Qasedak.Instagram.Webhooks` (`WebhookMetrics`): notifications counter tagged by outcome (accepted/rejected/deferred), events-dispatched counter by kind (message/comment/mention), duplicates counter by topic, ingestion-duration histogram per outcome — all exercised through a real MeterListener in tests so dashboards see exactly these series. Correlation ids: POST honors caller's `X-Correlation-Id`, mints UUIDv7 otherwise, always echoes via response header (asserted end-to-end); correlation flows through structured logs and into inbox redelivery warnings. Structured logs are source-generated LoggerMessage methods (`MetaWebhookLogs`) on every rejection path (oversized/signature-failure/non-JSON) and when a known event exceeds the redelivery attention threshold (≥3 attempts → stuck-consumer visibility); request content is never logged. Failure/retry visibility: pending-backlog observable gauge over the inbox attached via hosted service; dispatcher counts normalized events by kind at the boundary. 5 new tests (2 metric-series fixtures + correlation echo/mint end-to-end). Suite total 175 passing, build 0 warnings, format clean.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(observability): instrument webhook processing`

## M05-001 — Model conversations/messages
**Status:** DONE

**Outcome:** Define conversation and message state/identity ownership/invariants.

**Completion evidence:** Conversations Domain now models the inbox core per established conventions (UUIDv7 ids, rule-code exceptions via `ConversationsDomainException`, no clock in Domain): `Conversation` aggregate owns `Message` children — workspace-owned identity with opaque `Channel`/`ParticipantId` (no Meta types), Open/Archived status, monotonic `LastMessageAtUtc`, unread accounting (`MarkRead` resets, second read rejected); `AppendMessage` enforces unique provider message identity per thread (idempotency at aggregate level), 1000-char body cap, and inbound traffic reopens archived threads; `Archive` rejects repeats; `FromState` rehydrates for EF. Application port `IConversationRepository`; Infrastructure `ConversationsDbContext` ("conversations" schema: conversations + messages tables, unique `(WorkspaceId, Channel, ParticipantId)`, partial unique `ProviderMessageId`, indexed inbox ordering), committed migration `InitialConversationsCreation`, design-time factory, EF packages + DI registration. New test project `Qasedak.Modules.Conversations.UnitTests` with 8 domain tests (guards, unread/last-activity accounting, duplicate provider id, body cap by rule code, read/archive transitions incl. reopen, rehydrate-and-append). Suite total 183 passing, build 0 warnings, format clean.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(conversations): model conversation and message state`

## M05-002 — Project inbound Instagram messages
**Status:** DONE

**Outcome:** Consume normalized events idempotently into conversation state.

**Completion evidence:** Normalization now carries Meta's per-message `mid` (`InstagramMessageReceived.ProviderMessageId`) as the stable dedup key. New channel-neutral `ProjectInboundMessageUseCase` (Conversations Application): finds-or-creates the thread per (workspace, channel, participant), appends the message with aggregate-level idempotency — duplicate deliveries return `DuplicateDelivery` instead of failing; oversized inbound text is stored truncated rather than dropped. Workspace binding stays outside the module: composition-root `InstagramConversationBridge` (Api/CrossModule) resolves the owning workspace via new `IConnectedAccountRepository.FindWorkspaceIdByProviderIdentityAsync` and routes messaging events into the projection — the explicit cross-module contract; unbound accounts are logged and never fabricate conversations. Instagram's webhook POST now invokes a post-ingest seam (`IWebhookPostIngestProcessor`, no-op default) that Api fills with an adapter running pending normalization+dispatch inline; processing failures keep entries durably pending and answer 202. Domain fix: removed a wrong guard rejecting provider timestamps earlier than thread creation (webhook send-times legitimately predate processing time). Tests: end-to-end signed-webhook→conversation persistence + redelivery idempotency over real HTTP/PostgreSQL, unbound-account negative path, projection use-case unit coverage via updated fixtures. Suite total 185 passing, build 0 warnings, format clean.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(conversations): project inbound instagram messages`

## M05-003 — Expose inbox queries
**Status:** DONE

**Outcome:** Add workspace-scoped pagination/filter/detail APIs.

**Completion evidence:** New read-side port `IConversationQueries` (Conversations Application) with `EfConversationQueries` — server-side paging/filtering, no tracking, no aggregate loading: list ordered by recency with last-message preview via correlated subquery, status filter (`InboxFilter` clamps page size 1..100, defaults 25), detail returns thread row + messages ordered by occurrence. HTTP surface `ConversationEndpoints` in Conversations.Infrastructure mapped by the composition root at `/api/v1/workspaces/{workspaceId}/conversations` (+`/{conversationId}`), JWT-authorized like Identity routes; threads outside the queried workspace are 404, never leaked. API integration tests over real PostgreSQL + bearer auth cover pagination, status filter, workspace scoping (untouched workspace = empty; foreign thread = 404), anonymous rejection, message ordering in detail. Suite total 195 passing, build 0 warnings, format clean. Lesson encoded: minimal-API non-nullable query params are required and throw when absent — optional params use nullable signatures with explicit defaults.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(conversations): expose workspace inbox queries`

## M05-004 — Send compliant replies
**Status:** DONE

**Outcome:** Implement reply use case and Instagram messaging adapter with policy/error tests.

**Completion evidence:** Channel-neutral `IConversationChannelGateway` port (Conversations Application) with `SendReplyUseCase` enforcing compliance before any network call: open thread only, recipient inside the 24-hour customer-service window (measured from newest inbound message, boundary-tested), text validated; delivery happens first and only an accepted send is appended to the aggregate — local state never claims an unsent message. Instagram side: `IInstagramMessagingClient` port + `GraphInstagramMessagingClient` adapter (named HttpClient) posting the documented `{graph}/me/messages` contract with Bearer page token; structured failure taxonomy with Graph error 490 mapped to a distinct `MessagingWindowExpired` reason so callers can schedule instead of retrying blindly; token material never appears in failure details or logs. Composition-root `InstagramReplyGateway` binds the gateway: workspace account lookup via `ListByWorkspaceAsync`, token decrypt via `IProtectedTokenStore`, reason→stable-failure-code mapping (`instagram.windowExpired`, `.rejected`, `.unavailable`, `.malformed`). HTTP surface: POST `/api/v1/workspaces/{id}/conversations/{id}/replies` with failure-code→status mapping (404/400/409/502). Tests: 7 reply use-case unit tests (fakes for repo+gateway incl. window boundary and no-append-on-rejection), 6 deterministic adapter contract tests (scripted handler: payload/auth shape, malformed success, 490 mapping, bounded redacted error detail, non-JSON rejection, transport failure). Suite total 200 passing, build 0 warnings, format clean, architecture check passed.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(conversations): send replies through instagram`

## M05-005 — Establish Penpot ↔ Next.js design sync foundation
**Status:** DONE

**Outcome:** Create a repeatable MCP-based workflow translating approved Penpot pages, boards, components, tokens and assets into the existing Next.js project, with a machine-readable sync manifest (`frontend/Qasedak.Web/design/penpot-sync.json`), deterministic repository validation of that manifest (schema, duplicate routes/identifiers, missing paths), sync evidence under `docs/design/sync/`, and an agent-contract rule in `AGENTS.md` forbidding redesigning approved screens from imagination. Penpot stays the canonical visual source; Next.js keeps ownership of behavior/API/state. At least one representative mapped page/component must prove the mechanism end to end; M06 must not begin until this task's gates are green.

**Completion evidence:** Penpot MCP connected live — inspected 13 pages of the connected file; surveyed boards on Admin Dashboard, Connect Instagram, Comment Automation and Global Navigation Components pages; deep-inspected board "Navigation / Sidebar" (`f5bf3c2c-…8752c6768b24`, component `…8752c87448ee`) extracting exact colors (9 hex values), typography (Vazirmatn 22/16/14/12/11 at weights 400–800) and geometry (256px rail, 55/36px rhythms, 224×96 r12 footer card). Contract `docs/design/PENPOT-SYNC.md`; manifest entry `global-navigation.sidebar` with real IDs, `penpotRevision: null` (API exposes none — not fabricated), approval `provisional`; deterministic validator `tests/penpot-sync.test.mjs` (6 checks: schema/enums/uniqueness/path existence/token resolution/approved-mapping completeness) wired into both `npm test` and `verify.py --full` via `scripts/validate_penpot_sync.py`; representative implementation: token block in `globals.css`, reusable RTL `Sidebar.tsx`+module CSS under `src/shared/design/`, Vazirmatn via next/font, `/dashboard` shell composition; sync record `docs/design/sync/M05-005-navigation-sidebar.md` incl. unresolved items (active-state treatment, icon SVG extraction deferred to M08-001); `SCREEN-INVENTORY.md` rewritten as manifest roll-up; `AGENTS.md` §3.1 added. Gates: npm lint/typecheck/test/build all green (/dashboard route prerenders), architecture check passed.

**Completion contract:** Graphify evidence recorded; real Penpot MCP inspection evidence recorded (or explicit blocker); scoped tests/gates pass including frontend verification with manifest validation; project state/handoff/manifest updated.

**Suggested commit:** `feat(web): establish penpot nextjs design sync`

## M06-001 — Model automation aggregate
**Status:** DONE

**Outcome:** Define trigger/conditions/actions/status/version invariants.

**Completion evidence:** Automations Domain: `Automation` aggregate with fixed identity/name/workspace; lifecycle Draft→Active→(Unpublish↔Active)→Disabled(terminal); versioned definitions — unfrozen drafts edit in place, activation freezes the current version permanently (`FrozenActiveVersion()`), editing after freeze continues at a fresh version number so executed history is never rewritten; `AutomationDefinition` (channel-neutral `TriggerKind.CommentCreated`, ordered conditions incl. text contains/equals on comment text or sender id, ordered actions `SendDirectMessage` with 1000-char limit and required text) validated at construction (≥1 action, ≤10 conditions, ≤5 actions) with stable rule codes via `AutomationsDomainException`; timestamps passed in — no clock, no transport types in Domain. New test project `Qasedak.Modules.Automations.UnitTests` registered in slnx: 15 tests covering creation guards, draft replace-in-place, freeze-on-activate, edit refusal while active (versionFrozen), unpublish+revise continuing as v2 with v1 intact, terminal disable keeping readable history, action order preservation. Suite total 215 passing, build 0 warnings, format clean.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(automations): model automation aggregate`

## M06-002 — Persist versioned automations
**Status:** DONE

**Outcome:** Persist definitions without losing execution reproducibility/history.

**Completion evidence:** `AutomationsDbContext` under the "automations" schema: `automations` root table (status/activation/disable timestamps, explicit `CurrentVersionFrozen` marker exposed by the aggregate) + immutable `automation_versions` rows keyed `(AutomationId, Number)` holding module-owned JSON of each `AutomationDefinition` (System.Text.Json with string enums; transport models never enter persistence). `Automation.FromState` rehydration; `EfAutomationRepository` with upsert semantics — `SaveChangesAsync(automation)` inserts or rebuilds the aggregate's rows so every mutation flows through the aggregate (lesson: snapshot-on-add silently dropped later mutations; identity-map conflicts taught local-first upsert). Design-time factory (`QASEDAK_AUTOMATIONS_CONNECTION`), migration `InitialAutomationsCreation`, editorconfig generated-code exemption, DI registration, fixture provisioning 4th connection string + migration. New `Qasedak.Modules.Automations.IntegrationTests` over real PostgreSQL: full version-history round-trip incl. conditions/keyword filters surviving serialization byte-exact semantics; frozen v1 stability across reload while drafts advance to v2 and terminal disable persists with unfrozen draft marker; workspace-scoped newest-first listing. Suite total 218 passing, build 0 warnings, format clean.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(automations): persist versioned automation definitions`

## M06-003 — Implement deterministic evaluator
**Status:** DONE

**Outcome:** Evaluate trigger/conditions deterministically with exhaustive unit/property-style cases.

**Completion evidence:** `AutomationEvaluator` (Automations Application) — a pure function of (definition, TriggerContext): trigger-kind equality gate; keyword filters ANY-of case-insensitive substrings against comment text with empty-list match-all and explicit null-text rejection; conditions AND-composed per field (`CommentText`/`SenderId`) and operator (`Contains` case-insensitive substring, `Equals` trim-then-ordinal); actions returned exactly in declaration order on match; structured non-match reasons (`trigger.kindMismatch`, `trigger.keywordFilter`, `condition.<field>.<operator>`) for observability. 13 new evaluator unit tests covering the full matrix incl. boundary trims, null sender/text, multi-condition AND semantics and a 25× repeat-call identity check for determinism. Automations unit suite now 28 passing; solution total 231; build 0 warnings; format clean.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(automations): implement deterministic rule evaluation`

## M06-004 — Orchestrate idempotent actions
**Status:** DONE

**Outcome:** Guarantee at-most-intended-effect under webhook redelivery/retry/concurrency.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Completion evidence:** `AutomationRun` aggregate — the idempotency ledger: one run per (automationId, triggerEventId) pinned to the frozen version number, fixed action slots with Pending/Succeeded/Failed states; succeeded slots immutable, closed runs immutable, `FromState` rehydration. `ExecuteAutomationUseCase` orchestration: active-only gate → deterministic evaluation (non-matches never touch the ledger or dispatcher) → ledger probe short-circuits redeliveries (`AlreadyProcessed`) → run-insert races resolved by the unique index (SQLSTATE 23505 mapped to `AlreadyProcessed`) → slots dispatched strictly in order with persist-per-slot so crashes resume at the first non-succeeded slot across process boundaries → recorded-version ≠ frozen version refuses as stale; disabled/foreign/unknown automation refuses without dispatch. `EfAutomationRunRepository` upsert over real PostgreSQL (in-place slot merge — identity-map lesson generalized). Migration `AddAutomationRuns` (+`automation_runs`/`automation_run_actions`). 7 unit tests + 2 PostgreSQL integration tests (barrier-synchronized concurrent deliveries yield exactly one run; partial failure persists and resumes in fresh repositories to Completed). `ExecuteAutomationUseCase` DI registration deliberately deferred to M06-005 with its dispatcher port (host validation would fail on the unresolvable port today). Suite total 243 passing; build 0 warnings; format clean; architecture check passed.

**Suggested commit:** `feat(automations): orchestrate idempotent action execution`

## M06-005 — Deliver comment-to-DM flow
**Status:** DONE

**Outcome:** Implement first policy-compliant comment trigger → DM action based on current Meta capability matrix.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Completion evidence:** Composition-root wiring (`Qasedak.Api/CrossModule`): `AutomationCommentBridge` consumes normalized `InstagramCommentCreated` events (resolves workspace via connected account; iterates the workspace's ACTIVE automations through `ExecuteAutomationUseCase`; unbound accounts/non-comment events logged and skipped); `AutomationChannelDispatcher` binds the module's channel-neutral `IAutomationActionDispatcher` port to the outbound `IConversationChannelGateway`, so 24-hour messaging-window policy stays enforced inside the gateway (`instagram.windowExpired` recorded per failed slot); `FanOutIntegrationEventDispatcher` composes Conversations projection + Automations engine as the single dispatcher the Instagram module resolves — multi-consumer fan-out owned by the composition root. Normalizer now extracts `value.from.id` as the commenter identity (`InstagramCommentCreated.FromId`) with regression tests incl. missing-from tolerance. E2E over real PostgreSQL + real host (`CommentToDmAutomationFlowTests`, isolated account/workspace/automation + deterministic recording messaging stand-in replacing live Meta calls in CI): matching comment sends exactly one DM with correct token/recipient/text, ledger run Completed pinned to v1, redelivery never re-sends; non-matching comment leaves no send and no run; window-expired recipient records Failed slot with stable code `instagram.windowExpired`; disabled automation never dispatches. Fixture gained protection key config + recording client. Suite total 248 passing (incl. 74 Instagram unit after FromId additions); build 0 warnings; format clean; architecture check passed.

**Suggested commit:** `feat(automations): add comment to dm automation flow`

## M07-001 — Model workspace contact identity
**Status:** DONE

**Outcome:** Define contact/social identity ownership and merge invariants.

**Completion evidence:** Contacts module activated: `Contact` aggregate (workspace-owned; display name ≤200; up to 10 social identities per contact, channels normalized lowercase, provider identities opaque strings ≤128; interaction recency `FirstSeen/LastSeen/InteractionCount` with monotonic last-seen; Active→Archived lifecycle guards; terminal `Merged` status with `MergedIntoId` provenance). Merge design decision: identities are NOT physically moved between contacts — the absorbed contact keeps its identity rows and lookups resolve `MergedIntoId`, keeping the workspace-wide unique index `(WorkspaceId, Channel, ProviderIdentity)` unbreakable regardless of persistence ordering or concurrency. Persistence: `contacts` schema (`contacts` + `contact_identities` tables, workspace-unique identity backstop), `EfContactRepository` aggregate upsert with identity lookup (`FindByIdentityAsync`), design-time factory, migration `InitialContactsCreation`, DI wiring, ApiPostgreSqlFixture provisions 5th connection string + migrates. New test projects: 13 unit tests (guards, normalization, idempotent linking, limits, recency monotonicity, merge absorption/provenance, terminal states, rehydration) + 4 PostgreSQL integration tests (round-trip fidelity incl. case-insensitive identity lookup, uniqueness enforcement across workspaces, merge provenance across reloads, recency-ordered listing). Build 0 warnings; format clean; architecture passed.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(contacts): model workspace contact identity`

## M07-002 — Project interactions into contacts
**Status:** DONE

**Outcome:** Idempotently maintain contacts from supported social activity.

**Completion evidence:** `ProjectContactInteractionUseCase` (find-or-create by social identity with concurrent-create arbitration via the unique identity index; ledger-gated mutation so webhook redelivery/replay never double-counts; every newly-ledgered event bumps interaction recency; placeholder display names upgrade once real attribution arrives). New `IContactInteractionLedger` port + `EfContactInteractionLedger` over the unique-indexed `contacts.contact_interactions` event ledger; migration `AddContactInteractions`. Composition-root `ContactsInteractionBridge` fans InstagramMessageReceived senders and InstagramCommentCreated authors into contacts through the existing fan-out dispatcher (no module-to-module reference). Tests: 5 new unit tests (create+count, replay idempotency, recency accumulation, placeholder upgrade, create-race adoption) + 2 PostgreSQL projection tests (per-event idempotency, merge-pointer resolution keeps post-merge activity on the merged row) + 2 API e2e tests through the signed webhook pipeline (message sender contact creation/redelivery dedup; comment author projection). Build 0 warnings; all suites green.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(contacts): project social interactions into contacts`

## M07-003 — Add tags, notes and queries
**Status:** DONE

**Outcome:** Implement lightweight CRM behavior and workspace-scoped queries.

**Completion evidence:** Domain: normalized (trimmed/lowercase) tags with per-contact cap (12) and length guard (32), idempotent add/remove; append-only `ContactNote` records (≤2000 chars, immutable, no edit/remove API). Persistence: `contact_tags` + `contact_notes` tables, migration `AddContactTagsAndNotes`, upsert convergence for tags (add missing, remove absent) and append-only notes. Read side: `IContactQueries`/`EfContactQueries` with paged list (name ILIKE search, status and tag filters, LastSeen-descending) and workspace-scoped detail incl. notes. HTTP: `/api/v1/workspaces/{id}/contacts` list + detail, tag add/remove, note append — all JWT-authorized; unknown/foreign contacts 404 (`contact.notFound`); domain rule violations map to 400/409 by code. Tests: 5 unit tests (tag normalization/dedupe/caps/removal, note guards/immutability, FromState round-trip) + 3 PostgreSQL tests (upsert tag/note convergence across scopes, search/status/tag/paging filters with strict workspace scoping, detail scoping) + 2 API e2e tests through the signed webhook pipeline (authenticated searchable list, tag/note flow, foreign-workspace 404s). Build 0 warnings; suites green.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(contacts): add lead tags notes and queries`

## M08-001 — Consume/extend Penpot design system via sync
**Status:** DONE (2026-08-24)

**Outcome:** Extend the established Penpot ↔ Next.js sync foundation (`docs/design/PENPOT-SYNC.md`, `penpot-sync.json`, `docs/design/sync/` evidence) with the full approved token/component set: fetch the current Penpot design through MCP, translate approved tokens/components/layout primitives into reusable Next.js UI primitives, update the manifest and sync evidence for every mapped item. No screen may be implemented from imagination while an approved Penpot source exists.

**Completion contract:** Graphify evidence recorded; Penpot MCP inspection evidence recorded; manifest validation green; scoped tests/gates pass; project state/handoff/manifest updated.

**Completion evidence:** Canonical file verified by UUID `c269caa0-e456-818c-8008-85a77340be64` via `getPageById` (no human navigation). Extracted verbatim path-data for all 14 sidebar icons into `src/shared/design/SidebarIcon.tsx`; extended `:root` tokens in `globals.css` with live-sampled values (`radius.control/chip`, `elevation.menu`, `colorExtended.*` inkStrong/headingPlum/accentSoft/accentSofter/borderInput/textPlaceholder/status*/accentViolet) each annotated with `/Penpot/` origin; built presentation-only primitives `src/shared/design/ui` (Button variants×sizes, Card, TextField, TextAreaField with counter shell, SelectField, StatusPill, PageHeader); sidebar active state remains an OPEN QUESTION pinned by test (Penpot defines no explicit active row); tests `tests/design-system.test.mjs` 4/4; sync record `docs/design/sync/M08-001-design-foundation.md`; agent_finalize passed.

**Suggested commit:** `feat(web): implement penpot design foundation`

## M08-002 — Implement auth/workspace UI from synced designs
**Status:** DONE (2026-08-24)

**Outcome:** Build approved authentication/workspace screens and behavior. Before implementing, agents MUST fetch the latest mapped Penpot boards/components through MCP (per `AGENTS.md` sync contract), verify against `penpot-sync.json`, and update manifest + sync evidence. API integration, authorization behavior and validation logic stay application-owned and must survive re-sync.

**Completion contract:** Graphify evidence recorded; Penpot MCP inspection evidence recorded; manifest validation green; scoped tests/gates pass; project state/handoff/manifest updated.

**Completion evidence:** Live-inspected GetCode OTP boards `Auth / Login / Desktop 324404a7-…8776b27352cb` + `Auth / OTP / Mobile …8776b3100eb1` — documented divergence (phone-OTP vs backend email+password; no Qasedak auth board exists) → mapping `identity.auth` status **draft** citing both UUIDs. Implemented application-owned API layer `src/shared/api/http.ts` (injectable transport, ApiError with stable codes) + `identity.ts` (register/login/me/createWorkspace/listMembers, localStorage session with expiry check); pure validators `src/features/auth/validation.ts` mirroring PasswordPolicy (10..128 + non-alphanumeric) and workspace name rules with Persian copy for every failure code; pages `/login`, `/register` on foundation primitives. Tests: `tests/auth.test.mjs` + `tests/identity-api.test.mjs` (contract w/ injected transport). Frontend suite 18/18 at completion.

**Suggested commit:** `feat(web): add authentication and workspace flows`

## M08-003 — Implement Instagram account UI from synced designs
**Status:** DONE (2026-08-24)

**Outcome:** Build connection/state/revocation management screens. Fetch the latest mapped Penpot designs via MCP before implementing or updating; record sync evidence; keep OAuth/API integration and account state application-owned.

**Completion contract:** Graphify evidence recorded; Penpot MCP inspection evidence recorded; manifest validation green; scoped tests/gates pass; project state/handoff/manifest updated.

**Completion evidence:** Live-inspected `Connect to Instagram — Desktop f5bf3c2c-…874ac4b51953` + `Profile — Connected Accounts …874a8c53c34c` (approved mapping `instagram.connections`). Added minimal backend surface over tested use cases: `ConnectionEndpoints` (list / authorize-url / connect / disconnect under workspace-member policy; token material never leaves server) + public `ConnectionsFailureMapper` pinned by 6 unit tests (Instagram suite 80/80). Frontend `/dashboard/settings/instagram`: connect card ↔ connected list, health pills for all six `AccountHealth` values via pure mapper (unknown values fail closed), reconnect for Expired/ExpiringSoon/Revoked, disconnect busy-state, Persian copy for every stable code. Sync record `docs/design/sync/M08-003-instagram-accounts.md`.

**Suggested commit:** `feat(web): add instagram account management ui`

## M08-004 — Implement inbox UI from synced designs
**Status:** DONE WITH DOCUMENTED BLOCKED PORTION (visual sync BLOCKED — missing design) (2026-08-24)

**Outcome:** Build responsive conversation inbox/detail/reply experience. Fetch the latest mapped Penpot inbox boards/components via MCP before implementing or updating; record sync evidence; keep conversation queries/reply integration application-owned.

**Completion contract:** Graphify evidence recorded; Penpot MCP inspection evidence recorded; manifest validation green; scoped tests/gates pass; project state/handoff/manifest updated.

**Completion evidence:** Full sweep of all 24 canonical-file pages found **no inbox/conversation/DM design anywhere** → per milestone directive only this portion is BLOCKED with exact gap recorded in `docs/design/sync/M08-004-conversation-inbox.md` + `SCREEN-INVENTORY.md`; NO manifest mapping created (no design source exists) and no design values invented. Everything else delivered: functional inbox on approved foundation tokens only — `/dashboard/inbox` (filters, status/unread pills, fa relative time, empty/error states) and `/dashboard/inbox/[conversationId]` (thread bubbles, reply composer mirroring backend empty/tooLong rules, Persian copy for every stable reply/channel code incl. instagram.* and messaging-window) over existing `ConversationEndpoints`. Tests `tests/inbox.test.mjs` (+4).

**Suggested commit:** `feat(web): implement conversation inbox`

## M08-005 — Implement automation builder v1 from synced designs
**Status:** DONE (2026-08-24)

**Outcome:** Build approved automation list/editor/validation/state UX. Fetch the latest mapped Penpot automation-builder designs via MCP before implementing or updating; record sync evidence; keep automation definitions/evaluator integration application-owned.

**Completion contract:** Graphify evidence recorded; Penpot MCP inspection evidence recorded; manifest validation green; scoped tests/gates pass; project state/handoff/manifest updated.

**Completion evidence:** Live-inspected three boards (`Comment Automation — List …874ebb85c7c2`, `…— New …874ec2cb62fb`, `Smart Answering — Component States …8747843b4ad6`) → approved mapping `automations.comment` with documented divergences (design ۰/۲۰۰۰ counter vs domain 1000 cap — backend wins; per-post scoping has no v1 domain field → disabled «همه پست‌ها»; quick-replies/audio out of v1 scope). Backend: added `AutomationEndpoints` (list/create/get/PUT revision/activate/deactivate/DELETE) as thin glue over tested use cases incl. policy-checked activation surfacing `billing.subscriptionRequired|limitExceeded` verbatim; wire→domain `DefinitionMapper` fail-closed on enums, pinned by `AutomationEndpointContractTests` (+4 → 44/44). Frontend: list with search/status pills/lifecycle actions, shared `AutomationBuilderForm` used by new+edit routes with preview bubble and match-mode hints. Sync record `docs/design/sync/M08-005-automation-builder.md`.

**Suggested commit:** `feat(web): implement automation builder v1`

## M09-001 — Model subscriptions/entitlements
**Status:** DONE

**Outcome:** Define plans, subscription lifecycle and server-owned entitlements.

**Completion evidence:** Billing module activated provider-neutrally (no payment-provider identifiers anywhere in Domain — repo/docs searched; no provider-selection ADR exists; SRS defers provider to a future external adapter): \Plan\ aggregate (unique lowercase code, entitlement grants with latest-wins replacement, fail-closed \EntitlementFor\), \Entitlement\ (-1 unlimited / 0 disabled / positive cap, normalized feature keys), \Subscription\ lifecycle Trial→Active→PastDue→Canceled/Expired with explicit timestamped transitions, immutable period history, monotonic grace semantics (\IsEntitledAt\), \FromState\ rehydration. Application ports \IPlanRepository\/\ISubscriptionRepository\, \StartSubscriptionUseCase\ (one live subscription per workspace), \ResolveWorkspaceEntitlementsUseCase\ (fails CLOSED on missing plan). Infrastructure: \illing\ schema (\plans\+unique code, \plan_entitlements\, \subscriptions\+workspace-unique index backstop, \subscription_periods\), Ef repositories, design-time factory, migration \InitialBillingCreation\. ApiPostgreSqlFixture provisions 6th connection string + migrates. Tests: 11 unit (lifecycle rules, plan catalog) + 4 PostgreSQL (plan round-trip/uniqueness, one-row-per-workspace enforcement, period append across reloads, entitlement resolution incl. orphan-plan fail-closed). Build 0 warnings; suites green.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(billing): model subscriptions and entitlements`

## M09-002 — Integrate payment provider
**Status:** DONE (Zarinpal production-capable REST v4; Behpardakht Mellat live SOAP transport implemented against the human-supplied vendor reference; exactly-once guarantees verified end to end)

**Completion evidence (Mellat transport completion, 2026-08-24):** The vendor technical contract now exists in-repo at `docs/vendor/behpardakht/BEHPARDAKHT-IPG-v1.29-EN.md` (English translation of the Behpardakht IPG User Guide v1.29, Tir 1402/2023, label "Unofficial - External" — provenance preserved verbatim; newer conflicting onboarding material requires a future vendor-reference ADR, never silent changes). It was the SOLE protocol source (no NuGet/GitHub/blog/Laravel derivation). Implemented: `BehpardakhtSoapClient` — explicit SOAP 1.1 envelopes over typed HttpClient for bpPayRequest/bpVerifyRequest/bpSettleRequest/bpInquiryRequest/bpReversalRequest with XML-escaped parameters, namespace-agnostic `*Response`→`return` parsing by local name, SOAP fault/HTTP-failure/timeout → `PaymentGatewayUnavailableException`; no SOAP-generated types outside Infrastructure (`IBehpardakhtSoapClient` + wire records Infrastructure-internal, visible to tests via InternalsVisibleTo). Gateway orchestration per vendor §8–§13/§19/§25: pay params terminalId/userName/userPassword/orderId/amount IRR unchanged/localDate/localTime/additionalData/callBackUrl/payerId="0", defensive "ResCode,RefId" parse, ResCode=0 requires non-empty exact-case RefId persisted on the attempt, non-zero → typed rejection; numeric orderId derived deterministically from the attempt id and persisted via new `ProviderOrderId` column (migration `AddPaymentProviderOrderId` + snapshot); redirect through jump endpoint `/api/v1/payments/mellat/startpay` rendering an auto-submitting form POSTing only RefId to the configured payment page (credentials never reach the browser; jump hosted on the registered domain so Referer rule §62 holds). Callback: POST form variant on the public callback route parses RefId/ResCode/SaleOrderId/SaleReferenceId/CardHolderPan(masked) and normalizes to OK/CANCEL/FAILED; mandatory identity check BEFORE verification — callback SaleOrderId must exactly equal stored ProviderOrderId and callback RefId must resolve the stored attempt, mismatch → `payment.callbackRejected`, NO verify call, no activation, audited. Verify→settle chain: verify 0 → settle; 43 already-verified / 45 already-settled idempotent; definitive failures (17 cancel, 48 reversed, merchant-config 21/23/24/62/421, documented declines incl. 25/32/34/41/42/44/46/47/51/54/55/61) fail without entitlement; unknown outcomes reconcile via Inquiry instead of blind retry; still-unknown stays Pending with reversal available ≤ ~3h post-verify and never post-settle (`ReverseAsync` on the concrete gateway only). Bounded §19 response-code classifier. Typed options extended with config-overridable ServiceUrl/PaymentPageUrl/ServiceNamespace defaults per §6.1/§8.2; `.env.example`, docker-compose passthroughs and appsettings.json aligned; no real credentials in Git; credentials/PAN/full payloads never logged.

**Tests:** Billing unit suite 119/119 including new BehpardakhtMellatTransportTests — envelope parameter/XML-escape assertions; malformed pay parsing ("", nonsense, bare code, empty RefId); SOAP-fault/non-XML extraction; full §19 classification table; gateway flow scripts (disabled fail-closed; pay ok/non-zero/empty-ref/timeout; verify→settle; 43+45 AlreadyVerified; 48 definitive without settle; verify-timeout→inquiry→settle; inquiry-definitive failed; inquiry-timeout Unavailable pending; settle failure/timeout never invent success; ReverseAsync); use-case callback validation (wrong SaleOrderId rejected with ZERO verify calls; missing stored order id rejected; unknown authority NotFound; matching identity activates exactly once across duplicate POSTs; CANCEL never activates). API e2e over real host + real PostgreSQL + scripted SOAP fake (CI never touches bpm.shaparak.ir): Mellat checkout persists ProviderOrderId and returns the jump redirect with exact-case REF; jump page renders the auto-submitting form carrying exact RefId and zero credential material; form-POST callback activates subscription exactly once (verify+settle called once, duplicate replay harmless, single period); forged SaleOrderId → payment.callbackRejected with zero bank calls and no entitlement. Full backend solution suite green (458 tests across all projects incl. Testcontainers billing persistence).

**Residual (operational, not CI):** Live Zarinpal and live Mellat transports unproven against production credentials from CI/dev machines by design. Mellat go-live prerequisites recorded in docs/08 §6 and the release checklist: real terminal credentials, Shaparak registration of the deployment public host (IP allowlist; callback path + jump page inside the registered domain for Referer compliance), staging smoke incl. deliberate cancel + duplicate replay, reconciliation runbook (Inquiry for unknown outcomes; reversal ≤ ~3h post-verify, never post-settle).

**Outcome:** Provider adapters, idempotent attempt persistence, checkout/callback/status/history endpoints, and Penpot-synchronized billing UI.

**Completion evidence:** Human directive 2026-08-24 fixed providers = Zarinpal + Bank Melli/SADAD; a later human decision the same day (ADR-009) CANCELLED Bank Melli/SADAD and selected Behpardakht Mellat instead. Shipped: `PaymentAttempt` aggregate with Pending→Verified|Failed transitions, xmin optimistic concurrency (`IsRowVersion`) and unique filtered Authority index (anti-replay); `IPaymentGateway` port (Application) with `ZarinpalPaymentGateway` implementing the CURRENT official v4 REST contract (request.json → data.code 100 + authority, StartPay redirect, verify.json code 100=verified-first/101=previously-verified, masked card_pan) over direct typed HttpClient — no community packages, no secrets/logging of merchant id/payloads/PAN; `BehpardakhtMellatPaymentGateway` (`providerId="mellat"`) fail-closed boundary naming exactly which CURRENT official documents are required (service endpoints/WSDL, payment/verify/settle operation contracts, response-code table, callback field schema); historical bpPayRequest/bpVerifyRequest/bpSettleRequest flow treated as background only, nothing copied into transport; `PaymentGatewayResolver` treating "melli" as unknown; checkout/finalize use cases where callback queries alone can never activate a subscription and verified payments extend entitlement exactly once under concurrent replay (loser reloads → idempotent answer); endpoints GET /billing/plans, workspace subscription/checkout(202)/payments/{id}/payments history, public provider callback → 302 to `/dashboard/billing/result`; migration `AddPaymentsAndPlanPrices` (+Plan.AmountIrr, canonical IRR per ADR-008); env contracts in .env.example/docker-compose/deployment guide §6 (typed MELLAT_* options); ADR-008 accepted + ADR-009 accepted. Billing UI synced from `Qasedak · Billing & Payments` boards with provider labels updated in-file via MCP to «به‌پرداخت ملت»: plans/subscription/checkout/result pages render server-authoritative amounts only, no تومان↔ریال conversion anywhere. Tests: Billing unit 60/60 (attempt invariants, Zarinpal fixture contracts incl. timeout/malformed/rejection-masking, finalize exactly-once/duplicate-replay/NOK/verify-failed/outage-retry/concurrency-winner, resolver fail-closed incl. cancelled melli → unknown, Mellat boundary refusal on create AND verify), Billing integration Testcontainers 9/9 (roundtrip, unique refs, concurrent verify exactly-once), Api.IntegrationTests 46/46 incl. 9 billing e2e (auth isolation 401/403, server-owned price, duplicate callbacks, NOK cancellation, workspace isolation). No live payment calls in CI.

**Residual (honest partial — SUPERSEDED by the Mellat transport completion above):** Live Zarinpal transport unproven against production credentials (staging smoke test requires real merchant account; deliberately not done from CI/dev machine). ~~Behpardakht Mellat live transport cannot be implemented without the CURRENT official Behpardakht merchant technical documents listed above~~ — resolved: the vendor reference arrived and the transport is implemented (see top of this block).

**Completion contract:** Graphify evidence recorded (healthy); scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported above.

**Suggested commit:** `feat(billing): complete behpardakht mellat transport`

## M09-003 — Enforce entitlements server-side
**Status:** DONE

**Outcome:** Apply limits/feature access in application/server boundaries with tests.

**Completion evidence:** `EntitlementGate` (Billing.Application): server-owned decisions computed only from persisted subscription/plan state — fail-closed on missing subscription/expired period/missing plan (`billing.subscriptionRequired`), count limits with unlimited(-1)/disabled(0)/cap semantics (`billing.limitExceeded`); callers never pass claims in. Enforcement seam: `IAutomationActivationPolicy` port in Automations with permissive module default, overridden at the composition root by `BillingActivationPolicyAdapter` gating activation on the plan's `automations.active` count; new `ActivateAutomationUseCase` consults the policy before mutating (foreign workspace treated as not found), registered in Automations DI and Program.cs. Tests: 7 new unit tests — gate semantics (no-subscription denial, cap/unlimited/disabled, expired-period fail-closed) and activation enforcement (allowed path persists, denial surfaces stable code leaving Draft untouched, pending automation excluded from active count, foreign-workspace 404-equivalence). Build 0 warnings; architecture check passed (35 projects). No HTTP activation endpoint existed before this task; the UI-facing surface lands with M08.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(billing): enforce server-side entitlements`

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(billing): enforce server-side entitlements`

## M10-001 — Add structured telemetry/correlation
**Status:** DONE

**Outcome:** Standardize logging, tracing, metrics, correlation and privacy redaction.

**Completion evidence:** `BuildingBlocks.Infrastructure.Diagnostics`: `CorrelationMiddleware` (first in the API pipeline) honors well-formed inbound `X-Correlation-Id` (8–128 chars of [A-Za-z0-9_-]), otherwise mints a URL-safe GUIDv7 id; pushes `CorrelationId`+`RequestPath` into the ILogger scope so every structured line carries them; echoes the header on every response. Scoped `ICorrelationContextAccessor` exposes the identity to application code. `Sensitive` redaction helpers: full redaction preserving length class (`[redacted:len=N]`), tail-masking for identifiers (short values never leak), deterministic salted SHA-256 fingerprints for correlating repeated secrets without storing them. Registered via `AddQasedakBuildingBlocks`/`UseQasedakCorrelation`. Tests: 12 BuildingBlocks unit tests (id validation incl. XSS/oversize rejection, generation safety, redaction/mask/fingerprint properties) + 2 API e2e tests through the real host (fresh-id echo, inbound honored verbatim, malformed replaced). Build 0 warnings; format + architecture green.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(observability): add structured tracing and correlation`

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(observability): add structured tracing and correlation`

## M10-002 — Add rate limits/abuse controls
**Status:** DONE

**Outcome:** Protect public/authenticated/webhook paths based on risk and external quotas.

**Completion evidence:** `RateLimitPolicies` over ASP.NET Core's partitioned fixed-window limiter: risk classes Public (240/min/IP), Authenticated (600/min per user `sub`, IP fallback), Webhook (2000/min/IP — highest budget for provider bursts), Sensitive (30/min/IP on login/register). Limits configurable via `Qasedak:RateLimits:{Class}:{Limit,WindowSeconds}` for deployment tuning without code changes. Rejections answer 429 with `Retry-After` and stable code `ratelimit.exceeded`; per-partition keys ensure one abusive tenant cannot starve others. Global limiter registered in Program.cs after correlation middleware. Tests: API e2e with a purpose-built factory configured to a 3-request budget hammering a public endpoint until 429+Retry-After appears (regression test fails without the limiter). Build 0 warnings.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(security): enforce rate limits and abuse controls`

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(security): enforce rate limits and abuse controls`

## M10-003 — Add sensitive-action audit trail
**Status:** DONE

**Outcome:** Record security/billing/account/automation sensitive actions with immutable intent.

**Completion evidence:** Append-only audit trail: `IAuditTrail` port + `AuditEntry` in BuildingBlocks.Application (module-reachable without boundary violations); `AuditDbContext` (`audit` schema, `audit_entries`, write-once rows, indexes on (WorkspaceId, AtUtc) and (Action, AtUtc)), `EfAuditTrail` adapter, design-time factory, migration `InitialAuditCreation` (LF-normalized), bound via `AddQasedakAuditTrail` when the composition root configures `ConnectionStrings:Audit`. Emissions wired: identity login success/failure (failures store only a salted email fingerprint + reason code — never credentials or verbatim emails), automation activation granted/denied (denials include the entitlement reason code), subscription start. Privacy helpers centralized (`Sensitive` redaction/mask/fingerprint + application-level `AuditRedaction.Fingerprint`). Tests: 3 PostgreSQL e2e tests through the real host — failed-login audit with leakage assertions (raw email/password absent, fingerprint present), successful-login actor attribution, and append-only semantics (write-once ids, no duplication/mutation path). Module unit suites unaffected and green (79 Identity / 38 Automations / 18 Billing). Build 0 warnings; format + architecture green.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(audit): record sensitive actions append-only`

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(security): add sensitive action audit trail`

## M10-004 — Validate PostgreSQL backup/restore/migrations
**Status:** DONE

**Outcome:** Document and rehearse backup, restore, migration and rollback-safe procedures.

**Completion evidence:** `scripts/rehearse_backup_restore.py` executed successfully (REHEARSAL PASSED): boots two throwaway `postgres:18-alpine` containers, replays all seven module migrations through the API composition root (Identity, Instagram, Conversations, Automations, Contacts, Billing, Audit) against the source, seeds a data row, `pg_dump`s the database, restores into the second container with `ON_ERROR_STOP`, then verifies per-schema table parity for all seven module schemas (`identity` 4 tables, `instagram` 3, `conversations` 3, `automations` 5, `contacts` 6, `billing` 5, `audit` 2), seeded-row survival and identical EF migration history across the restore. Containers are removed on exit. Rollback-safety note: migrations are additive and the rehearsal validates forward replay; point-in-time recovery remains a deployment-time concern documented in M11.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `ops(postgres): validate backup restore and migrations`

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `ops(postgres): validate backup restore and migrations`

## M10-005 — Add mutation/security/load gates
**Status:** DONE

**Outcome:** Use mutation testing on critical rules, targeted security tests and representative load tests.

**Completion evidence:**
- **Mutation gate:** Stryker.NET 4.16.0 installed as a local dotnet tool (`.config`-style manifest `dotnet-tools.json`); config `backend/tests/Qasedak.Modules.Billing.UnitTests/stryker-config.json` targets the billing critical rules (`Plan`/`Subscription`, migrations excluded). Initial run exposed weak boundaries → added `BillingBoundaryTests` (code/name length edges, 32-feature cap edge, re-grant replacement across case/whitespace, invalid-limit edges, period entitlement boundaries). Final score **75.73%** (79 killed / 40 survived; pure exception-message string mutants excluded by recorded policy — rule CODES are asserted by tests; remaining survivors are documented NoCoverage branches). Reports under `StrykerOutput/…/reports/`. Honest note: score is above the configured low threshold (70) but below high (85); raising it further means covering the remaining NoCoverage branches.
- **Security gates (regression found + fixed):** new `SecurityGateTests` discovered a real cross-workspace authorization gap — workspace-scoped endpoints trusted the route parameter for any authenticated user. Fixed with a composition-root `workspace-member` authorization policy (`WorkspaceMemberRequirement` + handler over Identity's `IWorkspaceAccessChecker` port) applied to all contacts/conversations groups; existing endpoint tests updated to the new uniform-403 semantics and given proper seeded memberships. Gates now assert: anonymous → 401, forged webhook HMAC → 401, foreign-workspace contact read/mutate → 403, own-workspace absence → 404.
- **Load gates:** `LoadGateTests.WebhookIngestSustainsABurstWithinBudget` — 40 signed webhook events through the real host inside a time budget plus an authenticated inbox list answered under 2s; budgets intentionally loose to catch order-of-magnitude regressions only.
- Full API e2e suite green (37/37) with the policy in place; build 0 warnings; format + architecture green.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `test(hardening): add mutation security and load gates`

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `test(hardening): add mutation security and load gates`

## M11-001 — Finalize production environment contract
**Status:** DONE

**Outcome:** Freeze production configuration/secrets/network/storage/probe requirements.

**Completion evidence:** `docs/ops/PRODUCTION_ENVIRONMENT.md` — the normative v1 contract: runtime topology (API + Web images, one PostgreSQL, seven module schemas); all seven required connection strings with failure semantics; application settings table (token signing key, Meta app secret/verify token, token-protection key, CORS, rate-limit overrides) with fail-closed behavior notes; secrets policy (orchestrator-injected, rotation expectations incl. reconnect cost for Meta token re-encryption); probe semantics (`/health/live` process-only vs `/health/ready` dependency-backed) with orchestrator wiring guidance; networking/storage (TLS at reverse proxy, PostgreSQL as the only persistent state, no object storage/queues/caches in v1); deployment-time migration + rollback procedure. Enforced by new `scripts/check_environment_contract.py`, which extracts every `GetConnectionString(...)` and `Qasedak:*` key read from code and fails when the document does not list it — run now: **ENVIRONMENT CONTRACT IN SYNC (8 keys)**.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `ops(prod): document production environment contract`

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `ops(prod): document production environment contract`

## M11-002 — Rehearse deployment and rollback
**Status:** DONE

**Outcome:** Perform release candidate migration/deploy/smoke/rollback exercise.

**Completion evidence:** `scripts/rehearse_deployment.py` executed successfully (DEPLOYMENT REHEARSAL PASSED): builds the API release-candidate image from source (`backend/Dockerfile`), boots an isolated `postgres:18-alpine`, applies all seven module migrations, deploys the RC container wired exactly per the production contract (all seven connection strings, signing key, Meta secret/verify token, token-protection key, Production environment), gates on `/health/live` + `/health/ready`, then smokes over real HTTP: `/api/v1/system`, user registration (201) and login issuing a token. Rollback drill: stops the RC, redeploys the previous tag and repeats health + smoke. **Honest scope note (also printed by the script):** v1 has no predecessor image, so the drill redeploys the identical image to prove the stop/redeploy/health procedure — not binary drift; and DNS/TLS termination, public Meta webhook reachability, managed-Postgres behavior and real secret-store injection remain externally unverified.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `ops(release): rehearse deployment and rollback`

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `ops(release): rehearse deployment and rollback`

## M11-003 — Prepare v1 release baseline
**Status:** DONE

**Outcome:** Close release checklist, docs/state, image provenance and operational handoff.

**Completion evidence:** `docs/ops/RELEASE_CHECKLIST.md` — every gate with its evidence status (all repository-executable gates green at freeze: architecture, format, backend suites, 37/37 API e2e incl. security/load/audit gates, full toolchain verify, mutation gate running at 75.73%, backup/restore + deployment rehearsals PASSED, environment contract in sync) and an explicit "externally NOT claimed" section (DNS/TLS, Meta reachability, managed Postgres, secret store, payment processing, Penpot screens). `docs/ops/sbom/bom.xml` — CycloneDX SBOM for `Qasedak.Api`. `docs/ops/RELEASE_BASELINE.json` — source commit at freeze, rehearsal image id, artifact pointers. `HANDOFF.md` rewritten for the v1 baseline: human decisions required (provider ADR, Penpot MCP), verification status, agent continuation paths. No tag/push performed (agent contract).

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `chore(release): prepare qasedak v1 production baseline`

## M12-001 — Server-side inbox search
**Status:** DONE (2026-08-28)

**Outcome:** Enable the Penpot-marked-disabled inbox search with a real server-owned query: case-insensitive contains-search over the counterpart identity and every message body, with LIKE-wildcard escaping so user input matches literally.

**Completion evidence:** `SearchPattern` (Conversations Application) normalizes terms (trim; escape `%`/`_`/`\`; blank → no filter) pinned by 8 new unit tests (`InboxSearchTests`, Conversations suite 23/23). `EfConversationQueries.ListAsync` applies the term as `EF.Functions.ILike` over `ParticipantId` or any message body (EXISTS translation; wildcard injection impossible). HTTP surface: optional `search` query param on `GET /api/v1/workspaces/{id}/conversations` — backward compatible, blank = unfiltered, composes with `status` and paging. API e2e coverage added in `ConversationInboxEndpointTests` (participant match, case-insensitive body match, Persian terms, bare `%` → zero results, search+status composition, blank term = unfiltered) — added but NOT executed this session: Testcontainers needs the Docker daemon, which was down (honest residual; the suite previously ran green at 458). Frontend: `conversationsApi().list` forwards `search`; `/dashboard/inbox` search input is live with 250 ms debounce, the «فعلاً غیرفعال» badge is removed, and the empty state distinguishes no-results from empty inbox; client contract tests updated (search serialized with URLSearchParams encoding, blank omitted). Enabled-state placeholder «جستجو در گفتگوها…» recorded as a divergence in `docs/design/sync/M12-001-inbox-search.md` (the design only defined the disabled state). Frontend `npm run verify` green (lint/typecheck/37 tests/build); backend Release build 0 warnings, `dotnet format --verify-no-changes` clean, all unit suites green (380 tests: BuildingBlocks 12, Automations 44, Billing 119, Contacts 23, Conversations 23, Identity 79, Instagram 80).

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(conversations): add server-side inbox search`

## M12-002 — Enable inbox thread context panel
**Status:** DONE (2026-08-29)

**Outcome:** Replace the inbox thread's future-CRM placeholder with the real M07 contacts surface (contact name, tags, notes) behind the existing workspace-scoped Contacts APIs; the warning «Tags و Notes تا تکمیل M07 قابل ویرایش نیستند» is no longer true and must be removed with sync evidence.

**Completion evidence:** Backend gains a read-only workspace-scoped lookup `GET /api/v1/workspaces/{workspaceId}/contacts/by-identity?channel=…&identity=…` (`IContactQueries.FindByIdentityAsync` → `EfContactQueries`, resolving `MergedIntoId` chains to the absorbing primary) with the same detail payload as the by-id endpoint; the inbox resolves a conversation's `(channel, participantId)` to its CRM contact through it. New e2e `ContactEndpointTests.ContactResolvesByProviderIdentityAndReturnsCrmSurface` (resolve → tag/note mutations reappear on re-resolve; unknown identity 404; missing params 400; foreign workspace 403). Frontend: `src/shared/api/contacts.ts` client + `src/features/contacts/presentation.ts` (copy + tag/note bounds), and the thread page renders the «اطلاعات گفتگو» panel as a live CRM surface — display name, removable tag chips + add-tag, notes timeline + add-note, and a neutral (non-disabled) empty state when no contact exists yet; the design's «غیرفعال» badge and the «تا تکمیل M07» warning are gone. Sync: penpot-sync `inbox.conversations` notes updated (no fresh MCP read this session — MCP client unavailable; reconciled against the extracted 2026-08-24 contract), SCREEN-INVENTORY row updated, sync record `docs/design/sync/M12-002-thread-context-panel.md`. Gates: `dotnet build -c Release` 0 warnings/0 errors; full backend suite 471/471 (Contacts unit 23, Contacts integration 9, ContactEndpoint e2e 3 of the API integration suite); `npm run verify` 47/47; validate_penpot_sync/check_architecture/check_environment_contract → PASS.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(web): enable inbox thread context panel`

## M12-003 — Workspace dashboard overview
**Status:** TODO

**Outcome:** Implement the dashboard content area from the surveyed Admin Dashboard reference once a Qasedak-native design is approved (currently `reference surveyed; pending sync` in SCREEN-INVENTORY.md). Per the Penpot sync contract, no content is invented while no approved mapping exists; this task stays BLOCKED/TODO until the design source is approved.

**Completion contract:** Graphify evidence recorded; Penpot sync contract applies; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(web): add workspace dashboard overview`

## M12-004 — Consolidate duplicate local repository clones
**Status:** DONE (2026-08-30)

**Outcome:** Make `C:\Users\Hamed\Documents\Qasedak` the canonical GitHub-connected
clone and transfer the unique work from `C:\Users\Hamed\Documents\Python\qasedak`
without overwriting the canonical clone's newer backend, CI/CD, automations, billing,
Instagram, Inbox search/context, API clients or tests. The older clone was snapshotted
byte-for-byte to `C:\Users\Hamed\Documents\Python\qasedak-archive-20260830` before
selective transfer. Landing, dashboard shell/overview, server session/proxy adapters,
feature routes, design primitives, Penpot sync records and visual-review artifacts are
now present in the canonical tree. The `/api/v1` proxy bridges legacy bearer headers and
new HttpOnly cookies; login/workspace responses establish server-owned cookies and
logout clears both session stores. Historical Inbox UI files that predate M12 remain
only in the recovery archive because the canonical M12 implementation is newer.

**Completion evidence:** Main clone `master` and `origin/master` resolve to GitHub SHA
`0cd57876b3a672fffc5b773bf7c40e2bfd00dbf9`; the Python clone's push dry-run was rejected
as behind (`fetch first`). The archive and source each contain 27,029 files and
915,560,857 bytes. Frontend lint, typecheck, 56 tests and production build pass; the
manifest remains valid v1 with a new `landing.main` mapping; architecture checks and
backend gates are re-run at finalization. No fresh Penpot MCP read was available in this
merge session; imported M08-006/M08-007 evidence is retained and `penpotRevision` stays
`null`. Docker-dependent integration tests remain not-run when the daemon is absent.

**Completion contract:** Graphify evidence recorded for this consolidation; state,
manifest and file inventory updated; the original duplicate is removed only after the
archive/source verification and final gates pass. The M12-003 standalone dashboard
design approval remains an explicit follow-up rather than being invented here.

**Suggested commit:** `chore(repo): consolidate duplicate qasedak clones`

## M12-005 — Repair GitHub Actions repository manifest gate
**Status:** DONE (2026-08-30)

**Outcome:** Repair the failed `ci.yml` run for the consolidation push. The failure is
the repository-contract `FILE_MANIFEST.txt` freshness gate: the consolidation commit
added tracked files after the manifest had been generated, so CI's clean checkout sees
641 tracked files while the committed manifest still describes the pre-staging set.
Regenerate the manifest from the final tracked tree, verify all CI gates, and push the
minimal repair commit without weakening the contract or product tests.

**Completion contract:** Graphify evidence recorded; the manifest check passes from a
clean checkout; frontend/backend/repository-contract gates pass; state, handoff,
manifest and task tracker are updated; the repaired commit is pushed and its GitHub
Actions run is observed to completion.

**Completion evidence:** Regenerated `FILE_MANIFEST.txt` after the consolidation tree
was fully tracked (641 files); local `python scripts/generate_manifest.py --check`,
static verify, frontend verify and backend Release build/format passed. GitHub Actions
CI run `33286334704` for `b177542` passed all four jobs: repository-contracts, backend,
frontend and Docker. CodeQL `33286334733`, Publish Images `33286464960` and Deploy
Production `33286506764` also completed successfully.
The final local `python scripts/verify.py --full` passed after Docker became available,
including 471 backend tests and both Docker image builds.

**Suggested commit:** `fix(ci): refresh repository manifest after clone consolidation`

## M12-006 — Repair registration/login session flow
**Status:** DONE (2026-08-30)

**Outcome:** Make the active login and registration screens use the server-owned
HttpOnly session flow consistently, while preserving the short-lived bearer compatibility
value required by the still-client-side M12 feature screens. A successful account
creation/login must be visible to the server-side dashboard guard and every failure must
be rendered instead of being swallowed by the legacy browser-token path.

**Completion contract:** Graphify evidence recorded; regression coverage proves the
active auth screens call the web-owned auth handlers and preserve visible failure handling; frontend
verification and repository architecture/state gates pass; state, handoff and manifest
are updated; no unrelated auth or design contract is weakened.

**Completion evidence:** The active `/login` and `/register` pages now call the
same-origin Web handlers, so the
server establishes `qasedak_session`/`qasedak_workspace` cookies before dashboard
navigation. The short-lived access token remains in the existing client session store
only as a compatibility bridge for M12 client feature APIs; the server proxy prefers the
HttpOnly cookie. Missing token payloads, backend failures, duplicate emails and workspace
creation failures all render a Persian form-level error. Added a regression contract in
`frontend/Qasedak.Web/tests/auth.test.mjs`. Frontend `npm run verify` passed (57 tests,
lint, typecheck and production build); full `python scripts/verify.py --full` passed,
including 471 backend tests, architecture/document/state/Penpot/environment checks and
both Docker image builds.

**Suggested commit:** `fix(auth): restore visible login session flow`

## M12-007 — Repair production auth proxy routing and deploy
**Status:** IN PROGRESS (2026-08-30)

**Outcome:** Make the M12-006 server-owned session flow reachable through the production
reverse proxy. Web-owned auth/workspace handlers must live outside the public `/api/`
prefix because that prefix is routed directly to ASP.NET Core. Publish the corrected
release and verify the complete CI, image and production deployment chain.

**Completion contract:** Graphify evidence recorded; the handlers and all active callers
use `/web-api/*`; regression coverage prevents auth callers from returning to the
production `/api/` proxy prefix; the production deploy script proves the public auth
route returns the expected invalid-credentials response; frontend/full verification,
CI, image publishing and production deployment pass; project state and manifest are
current.

**Suggested commit:** `fix(auth): route web sessions outside production api proxy`
