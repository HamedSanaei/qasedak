# Project status

**Project:** Qasedak
**Current milestone:** M13 — Instagram OpenReply Parity & Production Integration
**Current task:** M13-012 — Extend automations with post scope, DM triggers, public replies and follow-ups (TODO)
**Last completed:** M13-011 (2026-09-07)
**Product implementation:** Instagram connection lifecycle, media catalog, insights, follower history, interactive webhook normalization, comment-automation Private Reply semantics (global semantic effect claim, exact-account comment-ID replies, public-reply boundary, Live/7-day policy, crash-safe replay), interactive messaging adapters (typed plain-text/button-template content with verified limits, typed provider success, safe zero-call rejection of unverified Private Reply interactive variants) and the durable reveal-flow capability (PlainText opening Private Reply → user-response correlation → Direct gate prompt → validated postback → optional follow gate → single Direct reveal) complete; M13-012 automation configuration mapping not started

## 2026-09-07 — M13-011 DONE: follow gate, opening DM and postback reveal flow

- Fresh first-party verification (retrieved 2026-09-07 — Instagram Messaging webhooks,
  "Send a Private Reply to a Commenter", "Send Messages with IG Login", User Profile
  with IG Login): the provider-correct sequence is comment → PlainText Private Reply →
  the user replies → consent + 24h Direct window proven → Direct button-template gate
  prompt → validated postback → optional follow check → ONE Direct reveal. A comment
  alone does NOT open the normal messaging window; the historical comment→postback→
  reveal assumption was corrected in TASKS/HANDOFF/contract. The current messaging
  webhook exposes `reply_to:{mid}` — the smallest official correlation field — which is
  now normalized (`RepliedToProviderMessageId`) and used to correlate the user's reply
  to the exact opening message; without it, the deterministic single-pending rule
  applies and ambiguity fails closed with ZERO provider calls.
- Durable `instagram.reveal_flows` (additive migration `20260907012837_AddRevealFlows`):
  state machine Starting → OpeningAttempted → AwaitingUserResponse → PreparingGatePrompt
  → AwaitingPostback → Revealing → Revealed / Expired / TerminalFailed / Uncertain;
  PostgreSQL-enforced one-flow-per-origin (ConnectedAccountId+CommentId), globally
  unique correlation-token hashes (raw rv1 token never persists — SHA-256 only), and
  atomic compare-and-swap transitions; the Revealing CAS is the single-reveal authority.
  Bounded invocation-owned content is persisted for deterministic restart continuation;
  no tokens, no raw webhook/provider bodies. M13-009's one-reply key is untouched and
  the opening reuses `CommentPrivateReplyCoordinator` (AlreadyClaimed adopts the stored
  provider identity — still exactly one opening message).
- Provider identity: the participant IGSID is adopted from the provider-confirmed
  Private Reply `recipient_id` when the comment carried no FromId (never fabricated).
  Consent/window anchor = latest qualifying inbound user message (monotonic; a postback
  does NOT refresh it; a new user message while awaiting postback refreshes it).
- Follow gate: focused `IInstagramRelationshipClient` port
  (`GET /{IGSID}?fields=is_user_follow_business`, Bearer-only, tri-state
  Follows/DoesNotFollow/UnknownUnavailable — errors never collapse into false) called
  ONLY after a proven user message; `FollowGateMode.Disabled` (default) keeps the
  provider-independent core working, `EnabledWhenSupported` holds on blocked/unknown
  and reuses the existing prompt (no polling, no fabrication, no scraping).
- Read-receipt fallback NOT implemented: `read.mid` proves read only (no click/follow/
  reveal signal) — recorded as the tracker's truthful "not implemented" verdict.
- Crash safety: every provider mutation is preceded by a durable Attempting-style
  marker; after it, NO automatic second call — timeouts/crashes/redeliveries replay
  or fail closed; a valid postback is the ONLY rescue for an ambiguous gate attempt
  (the tap itself proves delivery). Postback tamper/wrong-account/wrong-sender fail
  closed with zero provider calls.
- Tests: 51 new (39 deterministic unit incl. correlation/policy/coordinator state
  machine, 7 real-PostgreSQL store tests incl. concurrent single-reveal authority and
  restart/crash windows, 5 signed-webhook E2E incl. full flow, redelivery, tamper,
  follow-gate block→reveal, default no-op). Full backend 1031/1031 green.

## 2026-09-07 — M13-010 DONE: interactive messaging adapters

- Fresh first-party verification (retrieved 2026-09-07 — "Send Messages" and "Button
  Template" with IG Login, "Send a Generic Template" IG Login, "Send a Private Reply to
  a Commenter", Messenger Buttons reference linked by the IG template docs): Direct
  Message button templates ARE supported (`POST /<IG_ID>/messages`, `recipient.id`,
  `attachment:{type:template,payload:{template_type:button}}`, text ≤ 640 UTF-8 chars,
  1–3 buttons, types `postback`/`web_url`, postback payload ≤ 1000 chars, title ≤ 20
  chars, success `{recipient_id, message_id}`); the Private Reply guide documents text
  ONLY — interactive variants on `recipient.comment_id` are **NOT independently
  verified** and are implemented as unsupported with ZERO provider calls (never inferred
  from Direct support; per-task §10/§12 deviation recorded in HANDOFF + contract §3.11
  matrix).
- Qasedak-owned discriminated contracts: `InstagramMessageContent.PlainText` /
  `.ButtonTemplate` + closed `Postback`/`WebUrl` buttons (no raw Meta DTOs, no
  stringly-typed payloads); `IInstagramMessagingClient` gains typed `SendDirectAsync`
  (M05 `SendTextAsync` delegates — conversation replies stay text + recipient.id + 24h
  classifier); typed success identity required on every 2xx (`recipient_id` +
  `message_id`, missing = MalformedResponse, never fabricated).
- Central `MessageValidationPolicy`: plain text 1000 UTF-8 bytes; template text 640
  chars; buttons 1–3; title 20; postback payload 1000; URL http/https via real URI
  parsing with a bounded 2000-char safety cap (current IG pages state no numeric URL
  max). Over-limit/unsupported content → `LocalValidation` failure with ZERO provider
  calls; payloads/URLs/titles are never silently truncated.
- Private Reply integration: `CommentPrivateReplyCoordinator` takes typed content,
  enforces PlainText-only BEFORE the global claim (`privateReply.policyRejected.
  unsupportedContent`, zero claim/provider calls); the one-reply key
  (ConnectedAccountId+CommentId+PrivateReply) is untouched — content never partitions
  it; after the Attempting marker there is NO fallback of any kind (timeout/5xx/rate
  limit/malformed/crash all replay with zero second calls, M13-009 semantics intact).
- Direct templates preserve the 24h window classifier (10/2534022), Bearer-only token
  (never URL), token-echo redaction, cancellation propagation, declaration-order
  preservation, and low-cardinality `MessageSendMetrics` (content/button/outcome/
  category only). Postback round trip proven: an outbound opaque payload survives the
  M13-008 `messaging_postbacks` normalizer unchanged (M13-011 correlation boundary).
- Verification: backend 980/980 — Instagram unit 441 (+43: 20 policy, 13 adapter
  limit/typed-success/cancellation/redaction, 1 round-trip, coordinator content-gating),
  PG integration 93, API E2E 117; `verify.py --full` green (docs/state/arch/env/Penpot,
  restore, Release 0 warnings, format, Testcontainers, frontend `npm run verify`,
  both Docker image builds); no schema change; frontend untouched.

## 2026-09-07 — M13-010 deployed; production on immutable task image

- Task commit `3c6cc0098d47` (`feat(instagram): add interactive messaging adapters`)
  pushed to `origin/master`; production runtime is the immutable image
  `ghcr.io/hamedsanaei/qasedak-api|web:sha-3c6cc0098d47`.
- CI `34071181578` success; CodeQL `34071181608` success; Publish Images
  `34071382037` success; Deploy Production `34071445443` success — DB backup
  `qasedak-20260907T005752Z-sha-3c6cc0098d47.dump`; **no schema change** (all eight
  schemas already up to date; M13-010 adds no migration — the M13-009
  `instagram.comment_effects` ledger is sufficient); api Healthy; in-workflow health +
  public-web-auth-routing smoke passed ~00:58Z; no rollback.
- DI/startup evidence: api Healthy on the full composition root with the new
  `MessageSendMetrics`/typed messaging client registered (host boots the real API
  integration suite); token-refresh/follower-snapshot/webhook handlers unchanged and
  healthy.
- Independent public smoke (2026-09-07): `/` 200, `/api/v1/system` 200; webhook edge
  negatives — wrong verify token → 403, unsigned `POST` → 401 (zero inbox/business
  effects).
- Live Meta interactive messaging smoke: NOT RUN — no designated production test
  conversation/comment; no customer account touched; no Direct template or Private
  Reply issued merely to prove deployment.

## 2026-09-07 — M13-009 DONE: comment automation uses Meta Private Reply semantics

- Fresh first-party verification ("Send a Private Reply to a Commenter" and the
  IG Comment reference, developers.facebook.com, retrieved 2026-09-07): Private Reply
  is `POST graph.instagram.com/<VER>/<IG_ID>/messages` with `recipient:{comment_id}`
  + `message:{text}`, Bearer IG User token, `instagram_business_basic` +
  `instagram_business_manage_comments`, success `{recipient_id, message_id}`, one
  message per commenter, 7 days from **comment creation time**, Live during broadcast
  only. The stale `POST /<COMMENT_ID>/private_replies` handoff assumption is disproven
  and corrected in HANDOFF.md + contract §3.4. The commenter IGSID is NOT required
  for addressing — `FromId == null` never blocks a Private Reply.
- Three distinct outbound operations: DirectMessage (`recipient.id`, M05 unchanged),
  PrivateReply (`recipient.comment_id`), PublicCommentReply (`/{comment_id}/replies`
  boundary; no automation consumer yet — M13-012). Comment-origin automation actions
  route to Private Reply in the composition root; the M06-005 `recipient.id` first-
  contact bug is gone (regression: normal conversation replies still use the DM path).
- Global semantic claim: PostgreSQL-unique `instagram.comment_effects`
  (ConnectedAccountId + ProviderCommentId + EffectType; additive migration
  `20260906232403_AddCommentEffects`; M13-008 runtime stays bootable). Claim acquired
  before any Meta mutation; irreversible Attempting marker persisted before the call;
  Reserved → Attempting → Succeeded/TerminalFailed/Uncertain with same-owner resume
  and zero second provider calls after any attempt (timeouts, 5xx, crashes, redelivery,
  two matching automations, future reconciliation).
- Policy: exact 7-day math via the official IG Comment `timestamp` read (focused
  `ICommentReferenceReader` port, Bearer header only, never in the webhook HTTP path),
  one-sided notification guard as fallback (documented: `entry.time` is notification
  time, never comment creation time), Live never uses the 7-day rule (attempt-once,
  Meta is the broadcast authority).
- AutomationRun: truthful terminal slot statuses Suppressed / Uncertain /
  TerminalFailed plus a Finished run state — already-claimed or uncertain outcomes are
  never re-dispatched; a run whose provider effect succeeded but whose row was lost
  converges from the ledger replay with zero second Meta calls (E2E-proven).
- Verification: backend 935/935 — Instagram unit 398 (incl. 69 effect tests: 20
  private-reply adapter, 9 public-reply adapter, 8 reference reader, 13 policy,
  19 coordinator), PG 48 (13 claim-ledger: concurrency single-winner, owner resume,
  restart, secrets), API E2E 117 (11 reworked comment-automation flow: two automations
  one reply, envelope redelivery, crash-after-success, Live, FromId null, expired,
  wrong account, zero DM). Shared M13-003 parser fixed for `null` code/subcode
  envelopes (regression added). Format/architecture clean; frontend untouched.

## 2026-09-07 — M13-009 deployed; production on immutable task image

- Task commit `801096f2dea6` (`fix(instagram): use private replies for comment
  automations`) pushed to `origin/master`; production runtime is the immutable
  image `ghcr.io/hamedsanaei/qasedak-api|web:sha-1bb9e18d2465` following the
  manifest-correction redeploy below (code-identical tree).
- CI `34068123904` success; CodeQL `34068123934` success; Publish Images
  `34068295393` success; Deploy Production `34068396870` success — DB backup
  `qasedak-20260906T235949Z-sha-801096f2dea6.dump`; migration
  `20260906232403_AddCommentEffects` applied to schema `instagram` before the
  image switch (additive; M13-008 binary remains bootable; `Down()` drops only
  the new table); api Healthy; in-workflow health + public-web-auth-routing
  smoke passed ~00:00Z; no rollback.
- Independent public smoke (2026-09-07): `/` 200, `/api/v1/system` 200;
  webhook edge negatives — wrong verify token → 403, unsigned `POST` → 401
  (signature-before-persist, zero inbox/business mutation).
- Live Meta Private Reply smoke: NOT RUN — no designated production test
  account/comment; no customer account or comment touched; the one-reply rule
  means no disposable test comment existed for this deployment.
- Manifest-correction redeploy `1bb9e18` (FILE_MANIFEST.txt only): CI
  `34068667176`, CodeQL `34068667162`, Publish `34068881350`, Deploy
  `34068955677` all success — backup
  `qasedak-20260907T001046Z-sha-1bb9e18d2465.dump`, all eight schemas already
  up to date (zero migrations applied), api Healthy, health + public-web
  smoke passed ~00:11Z, no rollback.

## 2026-09-06 — M13-008 DONE: interactive webhook normalization

- Fresh first-party verification (Webhook Notification Examples — Instagram
  Platform, developers.facebook.com, retrieved 2026-09-06): comments
  `changes[]:{field,value:{id,text,media:{id,media_product_type},from:{id,username}}}`;
  messaging `message{mid,text?,is_echo?,is_deleted?,is_unsupported?,is_self?,quick_reply?,attachments?}`;
  postback `{mid,title,payload}`; read `{mid}` — never a watermark.
  `messaging[].timestamp` and `entry.time` are **milliseconds** (official
  13-digit values); the old seconds-only read (wrong dates / throw for
  realistic values) is fixed. `original_media_id` is documented on the
  FB-Login ad/boosted shape only; preserved separately when present, never
  fabricated. Contract §3.6 updated.
- Normalization architecture: raw-HMAC → durable inbox (SHA-256 raw-body key)
  → pure side-effect-free `MetaPayloadNormalizer` (explicit fragment-type
  dispatch; the message-only-first bug is gone) → per-entry exact-account
  resolution via the M13-002 `ResolveActiveAccountAsync` contract → enrichment
  (`ConnectedAccountId`/`WorkspaceId`) in `ProcessPendingWebhookEventsUseCase`
  → composition-root fan-out. No Graph calls, no module cross-references,
  no `UtcNow` fallback — provider time only (fragment → entry.time → ignored).
- Events: `InstagramPostbackReceived` (bounded mid/title/payload) and
  `InstagramMessageRead` (read.mid, no watermark concept) added;
  `InstagramCommentCreated` enriched with exact ConnectedAccountId, media.id,
  optional original_media_id, nullable from.id/username, provider time;
  deterministic fragment identity `{inboxId}:e{entry}:m{item}`.
- Inbound safety: echo/self/deleted/unsupported/attachment-only messages are
  observable non-triggering fragments; edits/reactions/referrals/unknown
  future fields never become inbound text; oversized text/payloads surface as
  non-triggering fragments, never truncated; unknown/ambiguous accounts fail
  closed with zero dispatch; multi-entry fan-out resolves each entry
  independently; identical redelivery is exactly-once at the inbox boundary.
- Bridges (Conversations/Contacts/Automations) now consume the enriched event
  directly — no downstream re-resolution of provider ids.
- Verification: backend suite 838/838 (Instagram unit 325 incl. 51 webhook;
  PG integration 35; API E2E 110 incl. 13 signed interactive-webhook tests:
  postback/read/comment/message, echo/self/deleted/unsupported/attachment
  filters, two-account fan-out, unknown+ambiguous fail-closed, SHA-256
  redelivery), format clean, architecture check passed, frontend untouched.

## 2026-09-06 — M13-008 deployed; production on immutable task image

- Task commit `b223862495a3` (`fix(instagram): lock gate list in overview
  concurrency tests`, the final SHA after the manifest-correction `452ef04`
  and race-fix commits) pushed to `origin/master`; production runtime is the
  immutable image `ghcr.io/hamedsanaei/qasedak-api|web:sha-b223862495a3`.
- CI `34052037934` success; CodeQL `34052037874` success; Publish Images
  `34052212371` success; Deploy Production `34052272304` success — DB backup
  `qasedak-20260906T183700Z-sha-b223862495a3.dump`; **no schema change** —
  all eight schemas already up to date (M13-008 is normalization-only, no
  migration added); api Healthy; in-workflow health + public-web-auth-routing
  smoke passed; no rollback.
- First CI run `34051040335` failed only on the stale `FILE_MANIFEST.txt`
  gate (manifest generated before the six new M13-008 files were staged —
  the M13-005 lesson, again); regenerated via `452ef04`. Second run
  `34051429792` exposed a lock-free enumeration race in the M13-007
  `BoundedConcurrencyNeverExceedsConfiguredMaximum` test under CI scheduling
  (`Collection was modified`); fixed with locked gate snapshots + drain
  loop, stable across 5 repeated runs and full verify — shipped in
  `b223862`.
- Webhook route evidence: `GET /api/v1/webhooks/instagram` challenge
  validation (wrong token → 403) and unsigned `POST` → 401 at the edge
  (signature-before-persist, zero inbox/business mutation) — both smoked
  live; `/` 200, `/api/v1/system` 200.
- Live Meta webhook smoke: NOT RUN — no designated production test account.
- Scheduler/processor startup: api Healthy with post-ingest processor,
  dispatcher, `instagram.token-refresh` and `instagram.follower-snapshot`
  handlers registered (host boots the full API integration suite with the
  real composition root; no DI/startup exceptions in the deploy log).

## 2026-09-06 — M13-007 DONE: Instagram insights + follower history

- Fresh first-party verification (developers.facebook.com, retrieved 2026-09-06):
  Instagram Login insights need `instagram_business_basic` +
  `instagram_business_manage_insights`; account `GET /<IG_ID>/insights`
  (period=day, metric_type=total_value, since/until = one UTC day, 90-day
  retention), media `GET /<MEDIA_ID>/insights` (lifetime period, 2-year
  retention, `saved` spelling on media vs `saves` on account, carousel
  containers are feed posts, album children have no insights); the
  account-insights `follower_count` metric is a **daily delta** (archived
  official v21.0 reference), never an absolute total — the absolute current
  follower total is the IG User `followers_count` field (IG Login Get
  Started, `instagram_business_basic` only). Empty data sets are NoData,
  never 0. Contract §3.8 updated with retrieval date + metric tables.
- Insights architecture: focused `IInstagramInsightsClient` Application port
  + `GraphInstagramInsightsClient` adapter over the shared M13-003 transport
  (Bearer header, versioned `MetaGraphUris`, `MetaGraphClassifier` taxonomy,
  central redaction); central verified `InsightMetricRegistry` (account set;
  feed = Image/Video/Carousel, reel set; unknown media kind → empty set → no
  provider request); first-class `MetricAvailability` states — a real
  provider 0 is Available(0), omitted/empty metrics are NoData, permission
  loss is PermissionRequired, rate limits/5xx are TemporarilyUnavailable.
- Backfill verdict: **NOT IMPLEMENTED** — no verified absolute historical
  series on Instagram Login (`follower_count` is a delta,
  `follows_and_unfollows` is combined, not net); history is durable direct
  daily observation only, never labeled as Instagram's complete series.
- Follower persistence: additive migration `20260906035357_AddFollowerSnapshots`
  → `instagram.follower_snapshots` (ConnectedAccountId + SnapshotDateUtc
  unique index, provenance enum column Observed/Derived/Backfilled, no FK —
  history survives disconnect, no token/provider blob); PostgreSQL-native
  conditional upsert enforces provenance precedence under concurrency
  (Observed > Derived > Backfilled; observed-vs-observed keeps freshest
  observation; derived/backfilled keep first write); M13-006 runtime stays
  bootable (additive schema only, Down reviewed).
- Daily scheduling (M13-004): `instagram.follower-snapshot` handler with
  identifier-only payload (ConnectedAccountId + snapshotDateUtc, secret-free
  — asserted by tests), occurrence-specific idempotency key
  `instagram-follower-snapshot:{account}:{yyyy-MM-dd}`, exactly one next-day
  job chained per settled occurrence, retryable vs terminal outcome mapping
  (no fake rows, no 0 fallback, disconnected/missing-token accounts do zero
  provider work), `FollowerSnapshotScheduleBootstrap` hosted service ensures
  pre-existing active accounts get today's job on startup (idempotent per
  account/day key, bounded 500/run, DB-only, no provider calls, no startup
  locks over network). Connect-time enqueue covers newly connected accounts.
- Exact-account API: `GET .../connections/{accountId}/overview` (analytics
  availability, current followers with value/state/observedAt/provenance,
  follower history, bounded media section with sum-safe totals + per-metric
  availability, account insights) and `GET .../followers/history?limit`;
  foreign/unknown accounts 404 with zero token reads + zero provider calls
  (asserted via call counters); permission loss degrades only analytics —
  media catalog and follower data survive; account-level permission failure
  stops the per-media fan-out (no provider amplification); one media's
  failure degrades only that media; overview uses bounded recent media (25)
  with max 4 concurrent insight calls, cancellation-aware, no DB transaction
  held during provider calls.
- Frontend: overview/history data contract only — `shared/api/insights.ts`
  client + `features/instagram/insights.ts` normalization (fail-closed
  availability/provenance, real-zero preservation, secret-free). No UI, no
  Penpot change.
- Tests: Instagram unit 286/286 (+60), Instagram PG 35/35 (+11
  real-PostgreSQL: migration survival, uniqueness, concurrent same-day
  writes, provenance races, isolation, retry/restart, secret-free rows),
  API E2E 97/97 (+10 overview/history auth + degradation + redaction),
  frontend 78/78 (+5 contract); full backend 786/786 (14 projects,
  Testcontainers); format/architecture gates green.
- Live Meta insights smoke: NOT RUN — no designated production test account;
  no customer token touched. (Deployment evidence follows in the deployment
  commit below.)

## 2026-09-06 — M13-006 deployed; production on immutable task image

- Task commit `55900dcc9373` (`feat(instagram): add media catalog queries`)
  pushed to `origin/master`.
- CI `34008906033` success; CodeQL `34008905991` success; Publish Images
  `34009058974` success (`ghcr.io/hamedsanaei/qasedak-api|web:sha-55900dcc9373`);
  Deploy Production `34009105710` success for the exact SHA (previous
  `sha-a68762e139f3`; backup
  `qasedak-20260906T032923Z-sha-55900dcc9373.dump`; **no schema change** — all
  schemas already up to date, no migration added; api Healthy; in-workflow
  health + public-web-auth-routing smoke passed ~03:29Z; no rollback).
- Scheduler startup evidence: no DI/startup exceptions; api Healthy with the
  dispatcher + `instagram.token-refresh` handler still registered; media
  dependencies resolve (host boots the full API integration suite with the
  real composition root).
- Public smoke (independent): `/` 200, `/api/v1/system` 200;
  unauthenticated media route returns 401 at the edge (route registered,
  auth enforced before any provider call — zero Meta calls possible
  without a token).
- Live Meta media smoke: NOT RUN — no designated production test account;
  no customer token touched.
- Production runtime is now immutable `sha-55900dcc9373`.

## 2026-09-06 — M13-007 deployed; production on immutable task image

- Task commit `7409dfb9057b` (`feat(instagram): add account and media insights`)
  pushed to `origin/master`.
- CI `34013214316` success; CodeQL `34013214397` success; Publish Images
  `34013373535` success (`ghcr.io/hamedsanaei/qasedak-api|web:sha-7409dfb9057b`);
  Deploy Production `34013423453` success for the exact SHA (previous
  `sha-55900dcc9373`; backup
  `qasedak-20260906T051314Z-sha-7409dfb9057b.dump`; migration
  `20260906035357_AddFollowerSnapshots` applied to schema `instagram` —
  `CREATE TABLE instagram.follower_snapshots` + unique
  `IX_follower_snapshots_ConnectedAccountId_SnapshotDateUtc`; api Healthy;
  in-workflow health + public-web-auth-routing smoke passed ~05:13Z; no
  rollback).
- Manifest gate: first CI run `34013059596` failed on `FILE_MANIFEST.txt`
  staleness (manifest predated the cancellation-race fix and the 27 new
  M13-007 files); diagnosed locally, regenerated, `--check` green, corrected
  via `docs(manifest): include M13-007 files in repository manifest` — no
  product-code change; final CI/CodeQL green on `7409dfb9057b`.
- Scheduler startup evidence: no DI/startup exceptions; api Healthy with the
  dispatcher + `instagram.token-refresh` and new `instagram.follower-snapshot`
  handlers + `FollowerSnapshotScheduleBootstrap` registered; overview/history
  dependencies resolve (host boots the full API integration suite with the
  real composition root; snapshot handler/scheduler unit + PG suites green).
  No manual snapshot job executed against production accounts (§88).
- Public smoke (independent): `/` 200, `/api/v1/system` 200;
  unauthenticated overview + follower-history routes return 401 at the edge
  (routes registered, auth enforced before any token read/provider call —
  zero Meta calls possible without a token).
- Live Meta insights smoke: NOT RUN — no designated production test account;
  no customer token touched.
- Production runtime is now immutable `sha-7409dfb9057b`.

## 2026-09-06 — M13-006 DONE: media catalog + post-selection APIs

- Fresh first-party verification (developers.facebook.com, 2026-09-06, IG User
  Media + IG Media references): `GET /<IG_ID>/media` on `graph.instagram.com`
  (IG Login, `instagram_business_basic`, Bearer User token, versioned path,
  read-only); returns most recent media max 10K; **Stories are not supported
  on this edge** (separate `/stories`) → excluded from the post picker;
  pagination via `paging.cursors.after` (forward only) + time-based params;
  IG-Login `media_type` IMAGE/VIDEO/CAROUSEL_ALBUM (REELS tolerated;
  `media_product_type` is **FB-Login only**, never requested); `thumbnail_url`
  VIDEO-only; `permalink` absent for album children; `media_url` omitted for
  copyrighted media; `like_count`/`comments_count` are basic metadata
  (missing = unknown, never zero). Contract §3.7 updated with retrieval date.
- Media architecture: focused `IMediaCatalogClient` Application port
  (Qasedak-owned `MediaCatalogItem/Page/Result/MediaKind/Cursor` records) +
  `GraphMediaCatalogClient` Infrastructure adapter reusing M13-003
  `MetaGraphUris/Transport/Classifier` (no raw HttpClient, no Graph DTO
  outside Infrastructure); `MediaCatalogPolicy` single source of bounds
  (default page 25, hard max 50, recent-N ceiling 200, max 20 provider pages,
  max 10 carousel children, cursor bounds, verified field set).
- Cursor: Qasedak-owned opaque base64url envelope v1 bound to
  `ConnectedAccountId` + provider `after` component; server always rebuilds
  the provider URL (host/version/path server-owned) — a cursor can never
  become a Graph URL or bypass exact-account authorization; malformed/
  oversized/repeated/looping cursors fail stably (no infinite traversal).
- Exact-account security: `ListMediaPageUseCase` resolves the exact account
  by `(WorkspaceId, ConnectedAccountId)`, 404 on foreign/unknown, safe local
  failure on disconnected/missing-token (zero provider call, zero token
  read — asserted via token-store call counters in E2E), token only read for
  the validated account; subscription health does not gate media reads
  (independent capabilities); GET endpoint is read-only toward Meta (no
  repair, no publishing, no insights, no scheduler use, no media DB — no
  schema change).
- API: `GET /api/v1/workspaces/{workspaceId}/instagram/connections/{accountId}/media?limit&cursor`
  → `{items, nextCursor, hasMore}`; failures mapped via M13-003 taxonomy
  through `ConnectionsFailureMapper` (401/404/400/409/503 stable codes, no
  provider body/token).
- Frontend: post-picker data contract only — `shared/api/media.ts` client +
  `features/instagram/media.ts` normalization (no UI, no Penpot change).
- Tests: Instagram unit 226/226 (+36 media contract/pagination/recent-N/
  cancellation/cursor/policy), API E2E 87/87 (+13 media endpoint auth,
  bounds, redaction, isolation), frontend 73/73 (+6 contract); full backend
  705/705 (14 projects); format/architecture/manifest gates green.
- Live Meta media smoke: NOT RUN — no designated production test account;
  no customer token touched.

## 2026-09-06 — M13-005 deployed; production on immutable task image

- Task commits pushed to `origin/master`: `13a930ec7d16` (`feat(instagram):
  complete account connection operations`) + `a68762e139f3` (`docs(manifest):
  include M13-005 files in repository manifest` — first CI run exposed that
  `FILE_MANIFEST.txt` had been generated before `git add` staged the 26 new
  files; process lesson: regenerate the manifest after staging).
- CI `34002028688` success (4 jobs incl. docker); CodeQL `34002028682`
  success; Publish Images `34002179444` success
  (`ghcr.io/hamedsanaei/qasedak-api|web:sha-a68762e139f3`); Deploy Production
  `34002231420` success for the exact SHA (previous `sha-0a3fbc0ac295`, DB
  backup `qasedak-20260906T004815Z-sha-a68762e139f3.dump`, instagram migration
  `20260905204310_AddConnectionEnrichment` applied in production, api
  Healthy, in-workflow health + public-web-auth-routing smoke passed
  ~00:48Z). No rollback. (An earlier Deploy run `34001101225` on `13a930e`
  correctly skipped: CI had failed on the stale manifest.)
- Independent public smoke at `https://qasedak.tofanservice.ir`: `/` 200,
  `/api/v1/system` 200, register 201 + valid login 200 (real accounts).
  Invalid-credential and duplicate-register failure paths return an empty
  400 at the edge instead of the code/CI-proven 401/409 JSON bodies
  (`LoginWithWrongPasswordIsUnauthorized` passes in CI on this exact tree),
  so the alteration happens upstream of the app (proxy/front door), is
  outside the M13-005 diff (Identity/pipeline untouched), and needs a
  human/infra follow-up — recorded here, not hidden.
- Scheduler startup evidence: no DI/startup exceptions in deploy log; api
  Healthy with the dispatcher hosted and the first production handler
  (`instagram.token-refresh`) registered; no fake jobs enqueued.
- Live Meta smoke: NOT RUN — no designated production test account; no
  customer token touched.

## 2026-09-06 — M13-005 DONE: connection enrichment, subscriptions, token refresh

- Fresh first-party verification (developers.facebook.com, 2026-09-06):
  refresh `GET /refresh_access_token` (`ig_refresh_token`, ≥24h old,
  unexpired, `instagram_business_basic`,
  `{access_token,token_type:bearer,expires_in}`); subscribe
  `POST /{IG_ID}/subscribed_apps` with `subscribed_fields` CSV →
  `{success:true}`; IG User reference (2026-04-22) exposes
  `id/username/name/profile_picture_url` only (no `user_id`, no
  `account_type` — adapter requests exactly the verified set and proves
  identity with `id`); webhook field table confirms the required
  `comments/live_comments/messages/messaging_postbacks/messaging_seen` set.
- Implementation: SHA-256-persisted, workspace/redirect-bound, 10-minute
  single-use OAuth state (`instagram.oauth_states`) with atomic conditional
  consume + opportunistic expired purge; profile proof-before-write (identity
  mismatch/unavailable persists nothing); central required-field set,
  truthful Unknown/Healthy/Partial/NeedsRepair health, exact-account
  `repair-subscription` endpoint; first production scheduled-work handler
  `instagram.token-refresh` (identifier-only payload, 7-day pre-expiry +
  24-hour minimum-age policy, per-generation idempotency keys, domain-owned
  `Version` CAS bumped by rotation AND disconnect, one atomic save of
  aggregate + ciphertext, transient-retry vs permanent-dead-letter via live
  inspection); workspace+account ownership on disconnect/repair, token-free
  enriched projection, outcome-only endpoint logs (no state/code/token).
- Migration `20260905204310_AddConnectionEnrichment` (additive nullable
  columns + `oauth_states`; legacy defaults Version 0 / Unknown; `Down`
  reviewed; M13-004 runtime stays bootable). Known audit fixes during the
  task: refresh payload camelCase roundtrip, `/me` → documented `/{IG_ID}`
  subscribe path, `IsRowVersion` → domain-owned concurrency token,
  disconnect bumps `Version` (stale rotation cannot resurrect tokens),
  refresh health-writes tolerate lost races as Stale.
- Tests: Instagram unit 190 (profile/subscription/refresh/repair/handler/
  policy/mapper), PG integration 24 (state incl. concurrent single winner,
  legacy defaults, enrichment roundtrip, CAS rotation, disconnect race,
  rollback), API E2E 5 (state roundtrip/replay/ownership/redaction/
  degraded→repair/disconnect); backend 656/656, frontend 67/67 + `npm run
  verify` green, architecture 36 projects, format clean, Graphify 0.9.26
  healthy. Commit/push/CI/deploy/smoke/evidence follow in this same
  instruction. State: M13-005 DONE, currentTask=M13-006 TODO.

## 2026-09-05 — M13-004 DONE: durable scheduled work infrastructure

- Platform-owned mechanism in BuildingBlocks (ADR-012): contracts
  (`ScheduledWorkItem/Status/Handler/Store/Options/Backoff/PayloadGuard`),
  PostgreSQL `platform.scheduled_jobs` (migration
  `20260905132336_InitialScheduledWorkCreation`, idempotency unique + due-scan
  indexes), atomic single-statement `UPDATE..RETURNING` claim with
  `FOR UPDATE SKIP LOCKED`, lease-checked settlement, deterministic capped
  backoff, dead-letter terminal states, poll/claim/dispatch hosted loop with
  per-scope handlers and metrics.
- `ConnectionStrings:Platform` wired through migrator, compose files, fixture,
  rehearsal scripts and environment contract (eight schemas). Delivery is
  at-least-once; consumers own external-effect idempotency.
- Tests: 12 unit (backoff/guard) + 15 PG integration (unique/race enqueue,
  disjoint claims, lease reclaim, retry→dead-letter, restart, cancel,
  lost-lease refusal, secret refusal, dispatcher settlement incl. cancelled-poll
  and settled-non-reclaim). 573/573 backend, `verify.py --full` green. No
  provider handlers yet (M13-005+).

## 2026-09-05 — M13-004 deployed; production on immutable task image

- Task commit `0a3fbc0ac295c8fc0be5ee7eb834c3fb5bb130bf` pushed to
  `origin/master`. CI `33985619872` success (4 jobs); CodeQL `33985619861`
  success; Publish Images `33985798254` success
  (`ghcr.io/hamedsanaei/qasedak-api|web:sha-0a3fbc0ac295`); Deploy Production
  `33985867065` success for the exact SHA (previous `sha-205018dfdb16`, DB
  backup `qasedak-20260905T190215Z-sha-0a3fbc0ac295.dump`, platform migration
  `20260905132336_InitialScheduledWorkCreation` applied in production,
  api/web Healthy, in-workflow smoke passed ~19:02Z). No rollback.
- Scheduler startup evidence: no DI/startup exceptions in deploy log; api
  Healthy with the dispatcher hosted (zero handlers registered yet, polls
  no-op). Independent public smoke at `https://qasedak.tofanservice.ir`: `/`
  200, `/api/v1/system` 200, invalid-login `/web-api/auth/login` 401.
- Live Meta smoke: not applicable (mechanism only; no provider calls).

## 2026-09-05 — M13-004 DONE: durable scheduled work infrastructure

## 2026-09-05 — M13-003 DONE: versioned Graph transport + failure taxonomy

- New `Graph/` foundation in Instagram Infrastructure: `MetaGraphOptions`
  (host/version/timeout on `Instagram:Meta`; defaults graph.instagram.com,
  v26.0, 100s), `MetaGraphUris`, canonical `MetaGraphError` envelope/parser
  (nested + flat OAuth shapes, 300-char redaction, fbtrace), `MetaGraphFailure`
  taxonomy + classifier (official 10/2534022 window; retryability), and
  `MetaGraphTransport` executor (timeout vs caller-cancel, safe reads).
- OAuth, token inspector and messaging adapters converged without merging:
  versioned `me/messages` + `me` probe; OAuth token endpoints stay unversioned
  per the Business Login contract; stale code-490 mapping deleted; inspector
  keeps OQ-3 health mapping (window stays Transient); DI passes graph options;
  env doc keys added. No schema change — rollback trivially safe.
- Tests: Instagram unit 82→122 (envelope/classifier/retry/redact/URIs/timeout/
  cancel/version-switch/official-window/inspector-URL/trace); all pre-existing
  OAuth/health/messaging pins green. 546/546 backend, `verify.py --full`
  green.

## 2026-09-05 — M13-003 deployed; production on immutable task image

- Task commit `205018dfdb160a79d8238b18eb2bf58dff2d7c6e` pushed to
  `origin/master`. CI `33948478046` success (4 jobs); CodeQL `33948478023`
  success; Publish Images `33948604734` success
  (`ghcr.io/hamedsanaei/qasedak-api|web:sha-205018dfdb16`); Deploy Production
  `33948661815` success for the exact SHA (previous `sha-3c3c721bfa61`, DB
  backup `qasedak-20260905T060054Z-sha-205018dfdb16.dump`, no schema change to
  migrate, api/web Healthy, in-workflow smoke passed ~06:01Z). No rollback.
- Independent public smoke at `https://qasedak.tofanservice.ir`: `/` 200,
  `/api/v1/system` 200, invalid-login `/web-api/auth/login` 401.
- Live Meta smoke: NOT RUN (no designated test accounts; deterministic
  contract tests cover the transport).

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

## 2026-09-05 — M13-002 routing correction deployed (runtime `sha-3c3c721bfa61`)

- Correction commit `3c3c721bfa61df7de56c1eea6415dceef272e5c8` pushed to
  `origin/master`. CI `33938422386` success (4 jobs; docker job slow ~9.5m but
  green); CodeQL `33938422377` success; Publish Images `33938965616` success
  after one rerun — first attempt failed on a transient Ubuntu archive mirror
  `Hash Sum mismatch` inside the API image `apt-get` layer (infrastructure
  flake, unrelated to the change); rerun green, both images tagged
  `sha-3c3c721bfa61`. Deploy Production `33939832514` success for the exact
  SHA (previous `sha-2fd1b3205d87`, DB backup
  `qasedak-20260905T024243Z-sha-3c3c721bfa61.dump`, routing-index migration
  replayed, api/web Healthy, in-workflow smoke passed ~02:42Z). No rollback.
- Independent public smoke at `https://qasedak.tofanservice.ir`: `/` 200,
  `/api/v1/system` 200, invalid-login `/web-api/auth/login` 401.
- Live Meta identity/reconnect smoke: NOT RUN — no designated production test
  accounts. Server logs not directly accessible; workflow health/smoke green.

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
