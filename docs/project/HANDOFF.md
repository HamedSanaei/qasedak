# Current handoff

## 2026-09-07 — M13-010 DONE; M13-011 packet ready (do not start M13-011)

M13-010 added interactive messaging capabilities. Fresh first-party verification
(retrieved 2026-09-07) and the provider-conditioned subset are recorded below.

### M13-010 delivery

- **Provider support matrix (normative, contract §3.11):**

  | Operation | Plain text | Postback button | Web-URL button |
  |---|---|---|---|
  | Direct Message (`recipient.id`) | Supported (≤ 1000 UTF-8 bytes) | Supported (template, ≤ 640 chars, 1–3 buttons) | Supported (http/https URL) |
  | Private Reply (`recipient.comment_id`) | Supported | **Not verified / not implemented** | **Not verified / not implemented** |
  | Public Comment Reply (`/replies`) | Supported (`message=` text) | Not applicable | Not applicable |

  The current first-party Private Reply guide documents `message:{text}` ONLY — button
  templates on the comment-ID path are NOT independently proven, so they are modeled as
  unsupported and never reach Meta (zero provider calls; never inferred from Direct
  support). This is the truthful §10/§12 provider-conditioned subset.
- **Qasedak-owned message model** (`Application/Messaging/MessageContentContracts.cs`):
  `InstagramMessageContent.PlainText` / `.ButtonTemplate` with closed
  `InstagramMessageButton.Postback` / `.WebUrl` — no raw Meta DTOs, no stringly-typed
  payloads; button declaration order is preserved by serialization.
- **Central limits** (`MessageValidationPolicy`): text 1000 UTF-8 bytes; template text
  640 chars; buttons 1–3; button title ≤ 20 chars; postback payload ≤ 1000 chars; URL
  http/https via `Uri.TryCreate` (no `StartsWith("http")`), bounded 2000-char safety
  cap (no official numeric URL max on current pages). Over-limit/unsupported content →
  `LocalValidation` failure with ZERO provider calls; never truncates payloads/URLs/
  titles (truncation changes correlation/behavior).
- **Direct Message:** `IInstagramMessagingClient.SendDirectAsync` (typed content,
  `recipient.id`, `POST me/messages`, Bearer; 24h window classifier 10/2534022
  preserved); `SendTextAsync` delegates (M05 conversation replies unchanged); typed
  success `{recipient_id, message_id}` required (missing → MalformedResponse);
  token-echo redaction; cancellation propagates as `OperationCanceledException`
  (never TransportFailure).
- **Private Reply:** `CommentPrivateReplyCoordinator` now takes typed content and
  enforces PlainText-only BEFORE the claim (unsupported →
  `privateReply.policyRejected.unsupportedContent`, zero claim + zero provider calls);
  the global claim key stays (ConnectedAccountId + CommentId + PrivateReply) — content
  never partitions it; after the Attempting marker NO fallback/second call ever
  (timeouts, 5xx, rate limits, malformed, crashes all replay terminal states with zero
  traffic). Safe local fallback (choosing plain text before the claim) is possible
  semantically but M13-010 ships no automatic fallback — M13-011/M13-012 own content
  selection.
- **Postback round trip:** outbound opaque postback payload survives the M13-008
  `messaging_postbacks` normalizer unchanged (`InstagramPostbackReceived.Payload` ==
  outbound payload, mid/account routing preserved) — the M13-011 correlation boundary.
- **Observability:** `MessageSendMetrics` low-cardinality counters
  (`content`/`button`/`outcome`/`category` only — never ids, payloads, URLs, text).
- **Verification:** backend 980/980 — Instagram unit 441, PG integration 93, API E2E
  117; `verify.py --full` green (incl. frontend + both Docker image builds); no schema
  change; frontend untouched. State: M13-010 DONE, currentTask=M13-011 TODO.

### Deployment evidence — M13-010 (2026-09-07, UTC)

- Task SHA `3c6cc0098d47dfd10a185b7848a269f799d59bfa` pushed to `origin/master`;
  production runtime is the immutable image
  `ghcr.io/hamedsanaei/qasedak-api|web:sha-3c6cc0098d47` (no rollback).
- CI `34071181578` success; CodeQL `34071181608` success; Publish Images
  `34071382037` success; Deploy Production `34071445443` success — DB backup
  `qasedak-20260907T005752Z-sha-3c6cc0098d47.dump`; **no schema change** (zero
  migrations applied; M13-010 adds no migration); api Healthy; in-workflow health +
  public-web-auth-routing smoke passed ~00:58Z.
- Independent public smoke (2026-09-07): `/` 200, `/api/v1/system` 200; webhook edge
  negatives — wrong verify token → 403, unsigned `POST` → 401 (zero inbox/business
  effects).
- Live Meta interactive messaging smoke: NOT RUN — no designated production test
  conversation/comment (never consume a customer's single allowed Private Reply or
  send a Direct template without a designated test thread).

### M13-011 packet (read-only handoff)

M13-011 adds the follow gate, opening DM and postback reveal flow. NOT authorized yet.
Everything below is shipped and verified; M13-011 consumes it, never rebuilds it.

- **Final Direct Message contracts:** `IInstagramMessagingClient.SendDirectAsync(token,
  recipientProviderUserId, InstagramMessageContent, ct)` (+ `SendTextAsync`
  convenience); content = `PlainText` | `ButtonTemplate(Text ≤ 640, Buttons 1–3)`;
  buttons = `Postback(Title ≤ 20, Payload ≤ 1000)` | `WebUrl(Title ≤ 20, Url
  http/https ≤ 2000)`; success requires `recipient_id` + `message_id`.
- **Final Private Reply contracts:** `CommentPrivateReplyCommand.MessageContent` — only
  `PlainText` is supported (interactive variants unsupported, zero calls);
  `ICommentPrivateReplyClient.SendPrivateReplyAsync(token, igId, commentId, text, ct)`
  unchanged.
- **Provider support matrix:** see table above (contract §3.11, retrieved 2026-09-07).
- **Verified Direct button-template shape:** `POST {graph}/{ver}/me/messages` with
  `{"recipient":{"id":IGSID},"message":{"attachment":{"type":"template",
  "payload":{"template_type":"button","text":T,"buttons":[{"type":"postback"|
  "web_url","title":...,"payload":...|"url":...}]}}}}`; Bearer IG User token;
  `instagram_business_basic` + `instagram_business_manage_messages`; webhooks
  `messages` + `messaging_postbacks`.
- **Postback button limits:** title ≤ 20 chars; payload ≤ 1000 chars (Messenger
  Buttons reference, linked by IG template docs); payload is opaque application data.
- **Web-URL button limits:** title ≤ 20 chars; http/https only; no webview/extension
  fields (not in the IG spec).
- **Private interactive support verdict:** NOT VERIFIED — first-party Private Reply
  guide is text only; do not implement interactive Private Replies until a current
  first-party page proves them; until then use the text opening reply for M13-011.
- **Typed provider success:** `MessagingSendResult`/`PrivateReplySendResult` carry
  `ProviderRecipientId` + `ProviderMessageId` from the provider response only — never
  fabricated; malformed 2xx is a failure.
- **Safe/unsafe fallback rules:** safe = deterministic local content selection BEFORE
  the Private Reply claim / before a Direct provider call (one provider request);
  unsafe = ANY second provider call after an attempt began (timeout/5xx/rate
  limit/malformed/crash) — forbidden for Private Reply under all circumstances.
- **M13-009 effect-ledger invariant:** `instagram.comment_effects` UNIQUE
  (ConnectedAccountId, ProviderCommentId, EffectType) with Reserved → Attempting
  (irreversible, pre-send) → Succeeded/TerminalFailed/Uncertain; same-owner Reserved
  resume; every other state replays with zero provider calls. Interactive content must
  never bypass or partition this key.
- **One-provider-call rule:** Private Reply semantic operation ⇒ provider request count
  ≤ 1 under every failure branch (proven by coordinator tests + E2E two-automation
  flow).
- **Postback webhook round trip:** outbound payload → M13-008 normalizer →
  `InstagramPostbackReceived.Payload` unchanged (tested). M13-011 signs/correlates
  payloads it defines; the messaging layer stays opaque.
- **24h Direct window semantics:** Direct messages (text or template) require an open
  customer-service window; Graph 10/2534022 maps to `MessagingWindowExpired`; a
  comment alone never opens it.
- **Private Reply timing/live semantics:** 7 days from comment CREATION time (exact via
  `ICommentReferenceReader`; one-sided notification guard otherwise); Live during
  broadcast only, attempt-once, never the 7-day rule; Meta is final authority.
- **Tests to extend, not rewrite:** `MessageValidationPolicyTests` (20),
  `GraphInstagramMessagingClientTests` (+13 template/limit/typed-success/cancel/
  redaction), `PostbackRoundTripTests` (1), `CommentPrivateReplyCoordinatorTests`
  (+3 content gating), API E2E 117 (M13-009 flows intact).
- **Residual Meta/App Review constraints:** Advanced Access for third-party accounts;
  live interactive Private Reply smoke requires a designated test account + disposable
  test comment; no live Meta calls in CI; the one-reply rule means no second automatic
  send under any ambiguity.

## 2026-09-07 — M13-009 DONE; M13-010 packet ready (do not start M13-010)

M13-009 corrected comment automation to Meta **Private Reply** semantics.
Fresh first-party verification (developers.facebook.com, retrieved 2026-09-07 —
"Send a Private Reply to a Commenter" + IG Comment reference) pinned the
contract: Private Reply is `POST graph.instagram.com/<VER>/<IG_ID>/messages`
with `recipient:{comment_id}` + `message:{text}`, Bearer IG User token,
`instagram_business_basic` + `instagram_business_manage_comments`, success
`{recipient_id, message_id}`, one message per commenter, 7 days from COMMENT
CREATION time, Live during broadcast only.

**Documentation correction (mandatory §6/§11):** the stale handoff residual
`POST /<COMMENT_ID>/private_replies` was DISPROVEN by the current first-party
contract and is removed; the commenter IGSID is NOT part of the request, so
`FromId == null` never blocks a valid Private Reply. Both corrected in this
handoff, in `docs/product/meta-instagram-platform-contract.md` §3.4, and in the
M13-009 TASKS entry.

### M13-009 delivery

- **Operations:** three distinct outbound operations — `DirectMessage`
  (`recipient.id`, M05 conversation replies unchanged), `PrivateReply`
  (`recipient.comment_id`, first contact from comments), `PublicCommentReply`
  (`/{comment_id}/replies` edge — adapter + ledger independence only; no
  automation config consumer; M13-012 owns that surface). No merged
  `SendMessage(recipient)` primitive exists.
- **Routing correction:** `AutomationChannelDispatcher` routes
  `TriggerKind.CommentCreated` + `ActionKind.SendDirectMessage` to
  `AutomationPrivateReplyBridge` → Instagram `CommentPrivateReplyCoordinator`.
  `ActionDispatch` now carries trigger origin semantics (TriggerKind,
  TriggerEntityId=CommentId, TriggerOccurredAtUtc, IsLiveComment, ActionIndex,
  ActionKind); frozen M06 definitions are untouched (`SendDirectMessage = 1`
  preserved, version reproducibility green).
- **Global semantic claim:** `instagram.comment_effects` table
  (`20260906232403_AddCommentEffects`, additive — M13-008 runtime stays
  bootable; `Down()` drops only the new table). PostgreSQL-unique
  `(ConnectedAccountId, ProviderCommentId, EffectType)`; owner identity
  `{automationId}|{version}|{triggerEventId}|{actionIndex}`. States:
  Reserved → Attempting (irreversible, persisted BEFORE the provider call) →
  Succeeded / TerminalFailed / Uncertain. Same-owner Reserved resume only;
  after Attempting NO automatic second Meta call ever (timeouts, 5xx, rate
  limits, crashes, redeliveries, envelope changes, future M13-013
  reconciliation all replay from the ledger with zero provider traffic).
- **Policy:** exact 7-day math from the official IG Comment `timestamp` read
  (focused `ICommentReferenceReader` — `GET /{comment_id}?fields=timestamp`,
  Bearer header only, never in the webhook HTTP path); one-sided notification
  guard fallback (documented: `entry.time` is NOT comment creation time);
  Live never uses the 7-day rule (attempt-once; Meta is the broadcast
  authority); future/invalid provider times cannot prove expiry (Meta final).
- **AutomationRun:** terminal slot statuses `Suppressed` (already claimed —
  never marked delivered), `Uncertain`, `TerminalFailed`, and run status
  `Finished` (immutable, never re-dispatched). Crash-after-provider-success
  converges via ledger replay with zero second Meta calls (E2E-proven).
- **Security:** token only in the Authorization header (never URL/query/ledger/
  logs/error details); adapter-level token-echo redaction added; the shared
  M13-003 parser was fixed for `null` code/subcode envelopes (regression test).
- **Verification:** backend 935/935 — Instagram unit 398 (incl. 69 effect
  tests), Instagram PG 48 (incl. 13 claim-ledger: concurrent single-winner,
  owner resume, restart, secrets), API E2E 117 (incl. 11 reworked
  `CommentToPrivateReplyAutomationFlowTests`: two matching automations →
  exactly one Private Reply HTTP call, envelope redelivery, crash-after-success,
  Live once, FromId null, definitely-expired, wrong-account zero-call,
  zero-DM guarantee). `dotnet format` + `check_architecture.py` clean;
  frontend untouched. State: M13-009 DONE, currentTask=M13-010 TODO.

### Deployment evidence — M13-009 (2026-09-07, UTC)

- Task SHA `801096f2dea636ad10baf5f06189e2bc6b906c3c` pushed to
  `origin/master` (no rollback).
- CI `34068123904` success; CodeQL `34068123934` success; Publish Images
  `34068295393` success; Deploy Production `34068396870` success — DB backup
  `qasedak-20260906T235949Z-sha-801096f2dea6.dump`; migration
  `20260906232403_AddCommentEffects` applied to schema `instagram` before the
  image switch (additive — M13-008 runtime stays bootable; `Down()` drops only
  the new table); api container Healthy; in-workflow health + public-web-auth-
  routing smoke passed ~00:00Z.
- Manifest-correction redeploy: `1bb9e18d24657d8451e5850ee9ac1df9d6adf`
  (`docs(manifest): include M13-009 deployment evidence files in repository
  manifest` — FILE_MANIFEST.txt only, code-identical tree) went through the
  full chain green — CI `34068667176`, CodeQL `34068667162`, Publish Images
  `34068881350`, Deploy Production `34068955677` (backup
  `qasedak-20260907T001046Z-sha-1bb9e18d2465.dump`; all eight schemas
  already up to date, zero migrations applied; api Healthy; health +
  public-web-auth-routing smoke passed ~00:11Z; no rollback). Production
  runtime is therefore the immutable image
  `ghcr.io/hamedsanaei/qasedak-api|web:sha-1bb9e18d2465`.
- Independent public smoke (2026-09-07): `/` 200, `/api/v1/system` 200;
  webhook edge negatives — wrong verify token → 403, unsigned `POST` → 401
  (signature-before-persist, zero inbox/business effects).
- Live Meta Private Reply smoke: NOT RUN — no designated production test
  account/comment (never consume a customer's single allowed reply).

### M13-010 packet (read-only handoff)

M13-010 adds interactive messaging capabilities. It is NOT authorized yet.
Everything below is shipped and verified; M13-010 consumes it, never rebuilds it.

- **Final `DirectMessage` operation:** existing `IInstagramMessagingClient`
  (`POST /me/messages`, `recipient.id`, Bearer; 24h-window classifier
  code 10/subcode 2534022). Used ONLY by M05 conversation replies and
  non-comment dispatches — never by comment automation.
- **Final `PrivateReply` operation:** `ICommentPrivateReplyClient` →
  `POST {graph}/{ver}/{IG_ID}/messages`, `{"recipient":{"comment_id":C},
  "message":{"text":T}}`, Bearer IG User token; success requires
  `recipient_id` + `message_id` (missing identity = malformed success →
  Uncertain); permission rejection/rate limit/5xx = `RejectedByMeta` →
  terminal `privateReply.terminalFailed.<category>`; timeout/unreachable =
  `TransportFailure` → `privateReply.uncertain`. Token never in URL; provider
  token-echo redaction enforced.
- **Final `PublicCommentReply` operation:** `ICommentPublicReplyClient` →
  `POST {graph}/{ver}/{comment_id}/replies` form `message={text}`, Bearer;
  success requires `{"id": ...}`. No automation consumer yet (M13-012).
- **Current Meta endpoints/permissions (verified 2026-09-07):**
  Private Reply `POST /<IG_ID>/messages` (`instagram_business_basic` +
  `instagram_business_manage_comments`); public reply
  `POST /<COMMENT_ID>/replies`; comment creation time
  `GET /{comment_id}?fields=timestamp` (ISO 8601; live-media comments readable
  only during broadcast).
- **Private Reply policy** (`PrivateReplyPolicy`, pure): window = 7 days from
  comment creation; exact time preferred; one-sided notification guard
  fallback (notification > 7d ⇒ definitely expired; younger proves nothing —
  Meta final authority); future times never prove expiry; Live NEVER uses the
  7-day rule (attempt-once; notification older than 7d cannot be an active
  broadcast); missing CommentId ⇒ MissingRequiredOrigin.
- **Comment-time limitation verdict:** exact comment creation time IS
  available via the focused IG Comment read; the webhook `entry.time` is
  notification time only and is never presented as creation time.
- **Live policy:** live_comments field or `media_product_type == "LIVE"`
  marks `IsLiveComment` on `InstagramCommentCreated`; live replies are
  attempted once immediately and Meta decides broadcast validity; never
  retried; never falls back to the 7-day rule.
- **Semantic effect table** (`instagram.comment_effects`): Id,
  ConnectedAccountId, ProviderCommentId (≤128), EffectType (1=PrivateReply,
  2=PublicCommentReply), OwnerOperationId (≤256), Status
  (1=Reserved, 2=Attempting, 3=Succeeded, 4=TerminalFailed, 5=Uncertain),
  AttemptedAtUtc, CompletedAtUtc, ProviderRecipientId (≤128),
  ProviderMessageId (≤256), FailureCode (≤128), CreatedAtUtc, UpdatedAtUtc;
  UNIQUE (ConnectedAccountId, ProviderCommentId, EffectType); no token/body
  columns (asserted by test).
- **Effect key:** `ConnectedAccountId + ProviderCommentId + EffectType` —
  global across automations, inbox envelopes, process restarts, app instances,
  future M13-013 reconciliation. Private and public effects are independent.
- **Claim ownership:** deterministic `{automationId}|{version}|
  {triggerEventId}|{actionIndex}`; same-owner resume of Reserved only;
  competitor never steals; unknown statuses fail closed (zero calls).
- **Crash/uncertain semantics:** after the durable Attempting marker, ALL
  outcomes replay with ZERO second provider calls: Succeeded (stored
  recipient_id/message_id reused), TerminalFailed (stored code replayed),
  Uncertain/Attempting (terminal uncertain). Delivery may be sacrificed in the
  ambiguous window — that is the deliberate one-reply-compliance tradeoff.
- **Provider success identity:** `recipient_id` + `message_id` persisted
  (bounded, no token, no raw body) for crash-after-success reconciliation.
- **AutomationRun integration:** terminal dispatcher verdicts
  (`ActionResult.TerminalSuppressed/Uncertain/TerminalFailed`) map to slot
  statuses Suppressed/Uncertain/TerminalFailed; runs with all-terminal slots
  close as `Finished` (immutable, never re-dispatched, never marked delivered);
  `ExecutionStatus.Finished` surfaces to bridges.
- **Exact-account/token pattern:** coordinator resolves the exact
  ConnectedAccount by id + workspace, refuses disconnected/wrong-workspace/
  missing-token BEFORE the claim, and uses only that account's ProviderUserId
  and token; no workspace-first fallback, no reconnect.
- **Plain-text limits:** private/public replies are plain text (automation
  action text ≤1000); no buttons/templates/quick replies — M13-010's scope.
- **Tests to extend, not rewrite:** `GraphCommentPrivateReplyClientTests`
  (20), `GraphCommentPublicReplyClientTests` (9), `GraphCommentReferenceReaderTests`
  (8), `PrivateReplyPolicyTests` (13), `CommentPrivateReplyCoordinatorTests`
  (19), `CommentEffectLedgerTests` (13 PG), `CommentToPrivateReplyAutomationFlowTests`
  (11 E2E), `ExecuteAutomationTests` terminal-outcome block (7).
- **Migration:** `20260906232403_AddCommentEffects` (additive; production
  applies it before the image switch; M13-008 binary remains bootable).
- **Residual Meta/App Review limits:** Advanced Access applies to DM/comment
  automation where required; live Private Reply smoke requires a designated
  production test account + disposable test comment; the one-reply rule means
  no second automatic send under any ambiguity.

## 2026-09-06 — M13-008 DONE; M13-009 packet ready (do not start M13-009)