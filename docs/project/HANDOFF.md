# Current handoff

## 2026-09-07 — M13-012 CORRECTION deployed; M13-013 still TODO (do not start M13-013)

M13-012 received its surgical correction: automation authoring validation now mirrors the
shipped M13-010 provider message contract, and all six originally-required Graphify
queries A–F are executed and recorded. M13-012 remains DONE; the task pointer was NOT
advanced. The next task remains M13-013 (comment reconciliation / provider history
synchronization) — not authorized here.

### M13-012 correction evidence (2026-09-07)

- **Provider-bound authoring limits (channel-neutral Automations Domain):**
  - PlainText-mapped kinds (DirectMessage, SendPrivateReply, legacy comment
    SendDirectMessage, StartRevealFlow opening, RevealText, ScheduleFollowUp text):
    `Encoding.UTF8.GetByteCount(text) <= 1000` — NOT character count. Boundary
    matrices for ASCII (1000/1001), Persian (500 chars = 1000 bytes valid; 1000 chars
    = 2000 bytes rejected) and emoji (250 = 1000 bytes valid; 251 rejected).
  - `GatePromptText <= 640` characters (maps to M13-010 ButtonTemplate.Text);
    640 valid / 641 rejected (`automation.gatePromptTooLong`).
  - Postback + Follow button titles `<= 20` characters (was 40); 20/21 boundary.
  - `FollowUrl <= 2000` characters (was 2048), no control characters,
    `Uri.TryCreate(value, UriKind.Absolute)` with scheme exactly http/https — the
    prefix-only `StartsWith` validator is gone. `https://`, `https://[`,
    `https:// example.com`, `javascript:`, `file:`, relative paths and
    control-character URLs all rejected at authoring. 2000-char URL valid.
  - `SendPublicReply` deliberately keeps its distinct 1000-character product cap —
    public comment reply is a different provider operation and must never inherit
    Direct-template/byte bounds (regression-proven both ways).
- **Backward compatibility:** the JSON converter materializes stored rows without
  re-running authoring validation — frozen v1 and already-persisted v2 rows that
  exceed the tightened bounds stay readable (`HistoricalV1RowExceedingNewAuthoringBoundsStaysReadable`,
  `HistoricalV2RowExceedingNewAuthoringBoundsStaysReadable`). No destructive rewrite,
  NO MIGRATION. Execution of such old rows still fails closed through the existing
  downstream M13-010 local validation before any provider call.
- **Cross-boundary parity regressions** (`AuthoringProviderParityTests`, test-only
  Automations→Instagram.Application reference): maximum Direct / Private Reply /
  legacy comment SendDirectMessage / delayed follow-up / full reveal configuration
  (opening + gate template with Postback+WebUrl + final text, incl. 2000-char URL)
  all map to `InstagramMessageContent` exactly as the composition root does and pass
  `MessageValidationPolicy.Validate`; every downstream constraint (over-byte text,
  >640 gate, >20 title, >2000 URL, malformed/control-char/unsupported-scheme URL)
  is rejected at authoring BEFORE persistence. No production module references
  Instagram from Automations — architecture check passes.
- **Graphify correction:** all six originally-required M13-012 queries A–F executed
  (budget 1200 each, code-only fallback, graph 516 communities) and appended as
  `M13-012-correction` evidence; the original one-query row is preserved untouched;
  `PROJECT_STATE.graphify.lastEvidence` updated truthfully (correction noted, not
  back-dated).
- **Verification:** backend full suite 1145/1145 green (Automations unit 181 incl.
  boundary/parity/read-preservation; API E2E 134 incl. M13-009/10/11/12 regressions);
  `dotnet format --verify-no-changes` clean; `check_architecture.py` PASSED;
  frontend `npm run verify` green; `verify.py --full` PASSED with the handbook parked
  and restored byte-identical (`9e15231d…`).
- **Deployment:** correction SHA `03d820416f118c9655f1c64fb0fd87f63fc9cf9e`;
  CI `34084562287`, CodeQL `34084562327`, Publish Images `34084806270`, Deploy
  Production `34084889439` all success; production image `sha-03d820416f11` (was
  `sha-a26af311bb68`); DB backup
  `qasedak-20260907T045543Z-sha-03d820416f11.dump`; zero schema change (all
  schemas already up to date — no migration); containers Healthy, health + smoke
  passed ~04:56Z, no rollback; public smoke 200/200/403/401; live Meta correction
  smoke NOT RUN — no designated test account.

## 2026-09-07 — M13-012 DONE; M13-013 packet ready (do not start M13-013)

M13-012 completed automation parity: comment + inbound-DM triggers, post/original-post
scope, every-event/keyword/whole-word matching, explicit actions (Private Reply, Direct,
Public Reply, reveal, durable follow-up) on a backward-compatible schema-v2 definition.

### M13-013 packet (read-only; do NOT implement reconciliation/history sync)

- **Final TriggerContext:** `EventId` (provider semantic identity: comment id for
  comments, provider mid for DMs), `Kind` (TriggerKind.CommentCreated=1 /
  InboundDirectMessage=2), `ProviderEventIdentity` (same semantic id), `SenderId`
  (comment FromId / DM sender id; null never fabricated), `Text`, `OccurredAtUtc`,
  `MediaId`, `OriginalMediaId`, `IsLiveComment`. Deterministic evaluator consumes it;
  no I/O/clock/random anywhere in evaluation.
- **Comment trigger identity:** run ledger keys on `automationId + comment id` —
  duplicate fragments, re-enveloped deliveries and future provider-history imports
  converge on one logical trigger. DM trigger identity: `automationId + mid` (message
  without mid fails closed, zero traffic).
- **Exact account binding:** every execution requires the event's resolved
  ConnectedAccountId == automation.ChannelAccountId (refusal BEFORE evaluation and
  ledger); bridges consume only normalized `InstagramCommentCreated` /
  `InstagramMessageReceived`; no workspace-first search, no first-account fallback.
- **Source scope:** SourceScope.AnySource / SpecificSource + `SourceMediaId` (opaque
  bounded ≤64, never resolved); comment matching honors `media.id` and
  `media.original_media_id` (ad originals). DM triggers reject media scope at authoring.
- **Text matching:** TextMatchMode.EveryEvent (keywords ignored) / Keywords (ANY-of
  case-insensitive substring); `WholeWord` (Keywords only) uses `WholeWordMatcher`
  (Unicode boundary algorithm, diacritics-safe). Conditions: CommentText/SenderId ×
  Contains/Equals, all must hold.
- **Final ActionKinds (append-only):** 1 SendDirectMessage (LEGACY origin-aware —
  comment ⇒ Private Reply, DM ⇒ Direct), 2 SendPrivateReply (comment only),
  3 DirectMessage (DM only), 4 StartRevealFlow (comment only; Extras.Reveal required),
  5 SendPublicReply (comment only), 6 ScheduleFollowUp (comment or DM;
  Extras.Delay 1m–7d). Matrix and conflicts enforced at authoring (≤1
  Private-Ready-consumer per comment definition).
- **Legacy v1 compatibility:** schema-versioned JSON converter; version-1 rows
  evaluate identically to pre-M13-012 (`LegacyV1BehavesIdenticallyToPreM13012Evaluation`
  fixture); no enum renumbering; frozen versions never rewritten.
- **Private/Public effects:** `comment_effects` ledger stays provider-effect authority
  for Private/Public replies (public reply outcome replayed into AutomationRun with
  zero provider call on Succeeded; Attempting/Uncertain/TerminalFailed replay
  terminally; foreign ownership truthfully Suppressed).
- **Reveal mapping:** StartRevealFlow → `AutomationRevealBridge` → M13-011 coordinator
  with automation-owned bounded content; opening Private Reply, gate prompt, postback
  and single reveal keep M13-011's CAS authorities.
- **Scheduled follow-up model:** `FollowUpJobPolicy.WorkType`, payload
  `{runId, actionIndex}` only (no secret, no message text — resolved from the pinned
  frozen version at due time), idempotency key = run+index, MaxAttempts default;
  `AutomationFollowUpScheduledHandler` revalidates lifecycle → version → exact account
  → participant → 24h window in that order.
- **Window lookup:** `AutomationDirectEligibilityAdapter` → Conversations
  `GetLatestInboundOccurredAtUtcAsync(workspace, channel, account, participant)` — the
  latest locally-projected inbound user message is the only 24h anchor; comment /
  Private Reply / public reply / postback / schedule timestamps never anchor it;
  transient projection failure ⇒ Unknown (retryable, zero provider call); Meta stays
  final authority (`direct.windowExpired` terminal at due time).
- **Outbound crash safety:** every non-repeatable mutation is preceded by a durable
  Attempting marker (PG CAS single UPDATE for follow-ups; run ledger for immediate
  actions); after the marker NO automatic second call — crash/restart/timeout settles
  Uncertain (`action.attemptInterrupted` / `followUp.attemptInterrupted`), zero resend;
  explicit provider rejection after an external attempt ⇒ TerminalFailed; local
  pre-provider rejection (ExternalAttempt=false) stays retryable.
- **AutomationRun states:** Pending/Scheduled/Attempting/Succeeded/Suppressed/Uncertain/
  TerminalFailed/ContinuationStarted slots; run Completed/Finished/Running/Failed;
  additive columns AttemptedAtUtc, CompletedAtUtc, ProviderRecipientId,
  ProviderMessageId (migration `20260907023218_AddAutomationActionAttemptColumns`,
  additive; Down drops only new columns).
- **Definition persistence:** `AutomationDefinitionJsonConverter` writes
  `{"schemaVersion":2,...}` and reads v1 legacy rows; `AutomationDefinition` JSON column
  unchanged; frozen version pinning in runs unchanged.
- **Known webhook/history gaps (M13-013 material):** no comment listing/history
  recovery; no Conversations history sync; no missed-webhook sweep; `entry.time` is
  notification time, never comment creation time (M13-009 `ICommentReferenceReader`
  remains the only creation-time authority); every-event DM triggers are at-least-once
  via the run ledger.

### M13-012 delivery (implementation evidence)

- **Backend 1128/1128:** unit 890 (Automations 135: evaluator v1+v2, whole-word Unicode,
  serializer legacy fixtures, definition validation matrix/conflicts, follow-up CAS,
  execute-use-case terminal outcomes); PostgreSQL + API E2E 238 (Automations ledger 11
  incl. concurrent attempt-marker race; API E2E 134 incl. 12 signed-webhook capability
  flows: DM trigger once + redelivery no-repeat, legacy origin-aware routing, missing
  mid fail-closed, specific-source MediaId/OriginalMediaId, public+private exactly once
  each, reveal mapping, follow-up due-time single send, disabled suppression, window
  expired terminal, interrupted attempt Uncertain, §48 comment-alone suppression,
  §47 no-from-id schedule fail-closed). Frontend `npm run verify` green (string-typed
  DTOs source-compatible; no UI changes).
- **Migration:** `20260907023218_AddAutomationActionAttemptColumns` (Automations
  schema, additive; old binary bootable; applied before image switch).
- **Gates:** `dotnet format --verify-no-changes` clean; architecture check passed;
  Graphify 0.9.26 healthy (code-only refresh + cluster-only + bounded query recorded);
  `verify.py --full` green except the known local-only `check_docs.py` deviation from
  the human-owned untracked handbook (preserved byte-identically, never staged, absent
  in CI). No live Meta calls; no production effects.
- **Deployment (2026-09-07):** task SHA `a26af311bb68b3a7706660c8f938b338d42b395c`;
  CI `34079389171` / CodeQL `34079389141` / Publish `34079619338`
  (`sha-a26af311bb68`) / Deploy `34079679915` success; backup
  `qasedak-20260907T032739Z-sha-a26af311bb68.dump`; migration
  `20260907023218_AddAutomationActionAttemptColumns` applied; api Healthy + health/
  smoke passed ~03:27Z, no rollback; public smoke 200/200/403/401; live Meta
  automation smoke NOT RUN (no designated test account). Evidence commit `[skip ci]`
  triggered zero runs. Next: M13-013 only.

## 2026-09-07 — M13-011 DONE; M13-012 packet ready (do not start M13-012)

M13-011 added the durable Instagram reveal-flow capability (follow gate, opening DM,
postback reveal) behind the provider-correct sequence verified 2026-09-07.

### M13-011 delivery

- **Provider-correct sequence (normative, contract §3.12):** comment → PlainText Private
  Reply (M13-009 one-shot effect) → the user replies → consent + 24h Direct window proven
  → Direct button-template gate prompt (rv1 postback correlation token) → validated
  postback → optional tri-state follow check → ONE Direct reveal. The historical
  comment→postback→reveal assumption is corrected everywhere (TASKS/HANDOFF/contract):
  a comment alone does NOT open the normal messaging window.
- **User-response correlation:** the current messaging webhook documents `reply_to:{mid}`;
  `InstagramMessageReceived` now carries `RepliedToProviderMessageId` (smallest focused
  field; bounded 256; oversized dropped, never truncated). Exact correlation wins; without
  it the deterministic single-pending candidate applies; multiple pending candidates with
  no reply_to fail closed (`reveal.participantAmbiguous`, ZERO provider calls — never an
  arbitrary pick).
- **Durable state machine** (`instagram.reveal_flows`, additive migration
  `20260907012837_AddRevealFlows`; old M13-010 runtime stays bootable; Down() drops only
  the new table): Starting → OpeningAttempted → AwaitingUserResponse →
  PreparingGatePrompt → AwaitingPostback → Revealing → Revealed / Expired /
  TerminalFailed / Uncertain. PostgreSQL enforces one flow per
  (ConnectedAccountId, ProviderCommentId), globally unique correlation-token hashes, and
  every transition is an atomic CAS (`EfRevealFlowStore`); the Revealing CAS is the
  single-reveal authority. Bounded invocation-owned content (opening/gate/reveal text,
  button title, optional follow URL) is persisted ONLY for deterministic restart
  continuation; raw tokens never persist (SHA-256 hash + purpose `rv1` only).
- **Opening Private Reply:** reuses `CommentPrivateReplyCoordinator` with deterministic
  owner `reveal|{flowId}` (flowId = `RevealFlowId.For(account, comment)`, stable across
  redelivery/restart). AlreadyClaimed → adopt the stored provider identity (recipient_id +
  message_id) with ZERO new calls. Participant IGSID is adopted from the provider-confirmed
  recipient_id when the comment carried no FromId (never fabricated).
- **Gate prompt:** ONE Direct button-template (M13-010 typed content, postback button
  `payload = rv1.<token>` + optional web_url follow button), persisted Attempting marker +
  token hash BEFORE the call; success → AwaitingPostback; ambiguous (timeout/5xx/
  malformed) → Uncertain, zero resend. A valid postback tap is the ONLY rescue for an
  ambiguous gate attempt (the tap itself proves delivery).
- **Reveal:** ONE Direct PlainText after atomic Revealing authority; outcome persisted in
  a single update (Revealed + provider message id). Crash before outcome → replay
  `reveal.reveal.uncertain`, ZERO second call; postback redelivery → replay, zero calls.
- **Follow gate:** `IInstagramRelationshipClient` (GET `/{IGSID}?fields=is_user_follow_business`,
  Bearer-only; tri-state Follows/DoesNotFollow/UnknownUnavailable with bounded reason;
  errors never become false) called ONLY after a proven inbound user message — never on a
  raw comment and never on a bare postback (official consent list: sent message /
  icebreaker / persistent menu). `FollowGateMode.Disabled` (default; provider-independent
  core always works) / `EnabledWhenSupported` (Blocked → hold + reuse the existing prompt,
  same button may be tapped again; UnknownUnavailable → hold; no polling, no scraping).
- **Read receipt:** `messaging_seen`/`read:{mid}` proves read ONLY — read→reveal fallback
  NOT implemented (tracker's truthful verdict).
- **24h anchor:** latest qualifying inbound user message (monotonic GREATEST); a postback
  does NOT refresh it; a new user message while AwaitingPostback/PreparingGatePrompt
  refreshes it (and never re-sends the prompt). Meta remains final authority.
- **Security:** postback validation order = bounds → rv1 parse → hash lookup → exact
  ConnectedAccount → exact sender → expected state → window → follow check → reveal CAS.
  Tampered/foreign payloads, wrong account and wrong sender → `correlation.invalid` /
  `correlation.bindingMismatch`, zero provider calls. Observability is low-cardinality
  (operation/outcome only — never ids, tokens, content).
- **Composition root:** `RevealFlowStartBridge` (content-provider seam
  `IRevealFlowStartContentProvider`; production default `NoRevealFlowStartProvider` =
  flows not auto-started until M13-012 maps configuration) + `RevealFlowContinuationBridge`
  (user message / postback continuations) in the fan-out.
- **Tests:** 51 new. Deterministic unit (39): correlation token bounds/entropy/tamper,
  follow-gate tri-state policy, coordinator state machine (happy path 1+1+1 sends, reply_to
  wins over pending ambiguity, participant adoption, already-succeeded adoption, tamper/
  wrong-account/wrong-sender zero calls, 24h expiry, anchor refresh, gate ambiguity no
  resend, crash-after-reveal no resend, follow block→reveal, local validation zero rows,
  second start suppressed, disconnected account). Real PostgreSQL (7): concurrent one-row
  origin, concurrent single-reveal authority, crash window never yields second authority,
  success replay across restart, token-hash uniqueness backstop, durable reply_to scans,
  terminal irreversibility. Signed-webhook E2E (5): full flow through the real pipeline
  (comment → opening → reply_to response → gate → postback → reveal, exact account/token
  assertions, zero relationship reads when Disabled), postback redelivery, tamper + wrong
  sender, follow block→reveal, default no-op regression. Full backend 1031/1031 green.
- **Limitations recorded:** Live comments follow M13-009 attempt-once (no special reveal
  path); no scheduled follow-ups; no generic DM/postback triggers (M13-012); postback
  does not refresh the 24h anchor (no first-party statement); ordinary-postback consent
  for User Profile reads remains unverified (fail closed); read→reveal fallback absent.

### M13-012 handoff (read-only — do NOT implement)

- **Provider-correct reveal sequence:** comment → PlainText Private Reply → explicit user
  response (the consent/window hinge) → Direct gate prompt → validated postback →
  optional follow check → single Direct reveal. An explicit user response is REQUIRED
  before any Direct message; a comment/read/postback alone never opens the window.
- **Flow initiation contract:** `StartRevealFlowCommand` (WorkspaceId, ConnectedAccountId,
  ProviderCommentId, ParticipantIGSID?, IsLiveComment, NotificationOccurredAtUtc,
  RevealFlowContent {OpeningPrivateReplyText, GatePromptText, PostbackButtonTitle,
  FollowUrl?, FollowButtonTitle?, RevealText}, FollowGateMode, optional automation owner
  metadata). Start via `RevealFlowCoordinator.StartFromCommentAsync` or the
  `IRevealFlowStartContentProvider` seam (M13-012 maps automation config into it). Do NOT
  add content to AutomationDefinition — persist config in M13-012's own model and pass
  invocation-owned values.
- **Schema/state machine:** `instagram.reveal_flows` (migration
  `20260907012837_AddRevealFlows`); states + status columns as above; unique
  (ConnectedAccountId, ProviderCommentId); unique CorrelationTokenHash; indexes on
  OpeningPrivateReplyMessageId and (ConnectedAccountId, ParticipantIGSID, State).
- **Correlation format/version:** `rv1.` + base64url(36 bytes) = 51 chars ≤ 64 bound;
  purpose-bound (reject other prefixes); only SHA-256 hash persisted; ≥128-bit entropy
  enforced on parse; bind to exact ConnectedAccount + sender; tamper → zero calls.
- **Gate/reveal safety:** durable Attempting markers before every provider call; after a
  marker NO automatic second call (timeout/5xx/rate-limit/crash → Uncertain/terminal,
  zero traffic); a valid postback rescues ONLY the ambiguous gate-prompt state.
- **24h anchor:** LastUserMessageAtUtc (monotonic; postback never refreshes; a new user
  message refreshes even while awaiting postback); local expiry is a guard, Meta is final.
- **Read-receipt verdict:** NOT implemented — `read.mid` proves read only.
- **Relationship port:** `IInstagramRelationshipClient` tri-state +
  `FollowGatePolicy` (Disabled / EnabledWhenSupported); `is_user_follow_business`
  verified 2026-09-07; consent rule enforced (only after proven user message).
- **Exact-account security:** continuations resolve account/token from the flow row's
  ConnectedAccountId (never workspace-first); wrong account/sender → zero calls.
- **Direct content contracts:** M13-010 `InstagramMessageContent` (PlainText /
  ButtonTemplate + Postback/WebUrl; limits in `MessageValidationPolicy`); reveal is
  PlainText only; gate prompt is a ButtonTemplate with ONE postback + optional web_url.
- **Residual provider/App Review:** ordinary-postback consent for User Profile reads
  unverified (fail closed); no numeric URL max on current pages (2000-char Qasedak cap);
  no live Meta calls in CI; production test account required for live smoke.

### Deployment evidence — M13-011 (2026-09-07, UTC)

- Task SHA `7a67e327939ba983f90ed6f548644b5332144624` pushed to `origin/master`;
  production runtime is the immutable image
  `ghcr.io/hamedsanaei/qasedak-api|web:sha-7a67e327939b` (no rollback).
- CI `34074401388` success; CodeQL `34074401451` success; Publish Images
  `34074619302` success; Deploy Production `34074684388` success — DB backup
  `qasedak-20260907T015758Z-sha-7a67e327939b.dump`; additive schema change:
  migration `20260907012837_AddRevealFlows` applied pre-image-switch (old M13-010
  runtime booted through the migration); api Healthy; in-workflow health +
  public-web-auth-routing smoke passed ~01:58Z.
- Independent public smoke (2026-09-07): `/` 200, `/api/v1/system` 200; webhook edge
  negatives — wrong verify token → 403, unsigned `POST` → 401 (zero inbox/business
  effects). Reveal-flow services DI-resolve through the healthy API; no startup
  provider calls, no follow polling.
- Live Meta reveal/follow smoke: NOT RUN — no designated production test account /
  disposable test comment / test participant (never consume a customer's Private Reply,
  gate prompt, postback or reveal to prove deployment).

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