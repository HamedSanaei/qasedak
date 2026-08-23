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
**Status:** TODO

**Outcome:** Implement endpoint challenge/signature/authenticity rules per verified Meta contract.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(instagram): verify meta webhook requests`

## M04-002 — Create durable idempotent webhook inbox
**Status:** TODO

**Outcome:** Persist event identity/body metadata and guarantee duplicate-safe ingestion.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(instagram): add idempotent webhook inbox`

## M04-003 — Normalize integration events
**Status:** TODO

**Outcome:** Translate raw Meta payloads to explicit integration events without leaking transport models into Domain.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(instagram): normalize webhook integration events`

## M04-004 — Instrument webhook processing
**Status:** TODO

**Outcome:** Add correlation, structured logs, metrics and failure/retry visibility.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(observability): instrument webhook processing`

## M05-001 — Model conversations/messages
**Status:** TODO

**Outcome:** Define conversation and message state/identity ownership/invariants.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(conversations): model conversation and message state`

## M05-002 — Project inbound Instagram messages
**Status:** TODO

**Outcome:** Consume normalized events idempotently into conversation state.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(conversations): project inbound instagram messages`

## M05-003 — Expose inbox queries
**Status:** TODO

**Outcome:** Add workspace-scoped pagination/filter/detail APIs.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(conversations): expose workspace inbox queries`

## M05-004 — Send compliant replies
**Status:** TODO

**Outcome:** Implement reply use case and Instagram messaging adapter with policy/error tests.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(conversations): send replies through instagram`

## M06-001 — Model automation aggregate
**Status:** TODO

**Outcome:** Define trigger/conditions/actions/status/version invariants.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(automations): model automation aggregate`

## M06-002 — Persist versioned automations
**Status:** TODO

**Outcome:** Persist definitions without losing execution reproducibility/history.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(automations): persist versioned automation definitions`

## M06-003 — Implement deterministic evaluator
**Status:** TODO

**Outcome:** Evaluate trigger/conditions deterministically with exhaustive unit/property-style cases.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(automations): implement deterministic rule evaluation`

## M06-004 — Orchestrate idempotent actions
**Status:** TODO

**Outcome:** Guarantee at-most-intended-effect under webhook redelivery/retry/concurrency.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(automations): orchestrate idempotent action execution`

## M06-005 — Deliver comment-to-DM flow
**Status:** TODO

**Outcome:** Implement first policy-compliant comment trigger → DM action based on current Meta capability matrix.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(automations): add comment to dm automation flow`

## M07-001 — Model workspace contact identity
**Status:** TODO

**Outcome:** Define contact/social identity ownership and merge invariants.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(contacts): model workspace contact identity`

## M07-002 — Project interactions into contacts
**Status:** TODO

**Outcome:** Idempotently maintain contacts from supported social activity.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(contacts): project social interactions into contacts`

## M07-003 — Add tags, notes and queries
**Status:** TODO

**Outcome:** Implement lightweight CRM behavior and workspace-scoped queries.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(contacts): add lead tags notes and queries`

## M08-001 — Implement Penpot design foundation
**Status:** TODO

**Outcome:** Translate approved tokens/components/layout primitives to reusable Next.js UI foundation.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(web): implement penpot design foundation`

## M08-002 — Implement auth/workspace UI
**Status:** TODO

**Outcome:** Build approved authentication/workspace screens and behavior.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(web): add authentication and workspace flows`

## M08-003 — Implement Instagram account UI
**Status:** TODO

**Outcome:** Build connection/state/revocation management screens.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(web): add instagram account management ui`

## M08-004 — Implement inbox UI
**Status:** TODO

**Outcome:** Build responsive conversation inbox/detail/reply experience.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(web): implement conversation inbox`

## M08-005 — Implement automation builder v1
**Status:** TODO

**Outcome:** Build approved automation list/editor/validation/state UX.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(web): implement automation builder v1`

## M09-001 — Model subscriptions/entitlements
**Status:** TODO

**Outcome:** Define plans, subscription lifecycle and server-owned entitlements.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(billing): model subscriptions and entitlements`

## M09-002 — Integrate payment provider
**Status:** TODO

**Outcome:** Implement provider adapter/webhooks/idempotency after provider selection ADR.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(billing): integrate payment provider`

## M09-003 — Enforce entitlements server-side
**Status:** TODO

**Outcome:** Apply limits/feature access in application/server boundaries with tests.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(billing): enforce server-side entitlements`

## M10-001 — Add structured telemetry/correlation
**Status:** TODO

**Outcome:** Standardize logging, tracing, metrics, correlation and privacy redaction.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(observability): add structured tracing and correlation`

## M10-002 — Add rate limits/abuse controls
**Status:** TODO

**Outcome:** Protect public/authenticated/webhook paths based on risk and external quotas.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(security): enforce rate limits and abuse controls`

## M10-003 — Add sensitive-action audit trail
**Status:** TODO

**Outcome:** Record security/billing/account/automation sensitive actions with immutable intent.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `feat(security): add sensitive action audit trail`

## M10-004 — Validate PostgreSQL backup/restore/migrations
**Status:** TODO

**Outcome:** Document and rehearse backup, restore, migration and rollback-safe procedures.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `ops(postgres): validate backup restore and migrations`

## M10-005 — Add mutation/security/load gates
**Status:** TODO

**Outcome:** Use mutation testing on critical rules, targeted security tests and representative load tests.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `test(hardening): add mutation security and load gates`

## M11-001 — Finalize production environment contract
**Status:** TODO

**Outcome:** Freeze production configuration/secrets/network/storage/probe requirements.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `ops(prod): document production environment contract`

## M11-002 — Rehearse deployment and rollback
**Status:** TODO

**Outcome:** Perform release candidate migration/deploy/smoke/rollback exercise.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `ops(release): rehearse deployment and rollback`

## M11-003 — Prepare v1 release baseline
**Status:** TODO

**Outcome:** Close release checklist, docs/state, image provenance and operational handoff.

**Completion contract:** Graphify evidence recorded; scoped tests/gates pass; project state/handoff/manifest updated; residual not-run gates explicitly reported.

**Suggested commit:** `chore(release): prepare qasedak v1 production baseline`
