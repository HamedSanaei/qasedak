# Current handoff

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

- Task SHA `M13_009_SHA_PENDING` pushed to `origin/master`; production runtime
  is the immutable image `ghcr.io/hamedsanaei/qasedak-api|web:sha-M13_009_SHA_PENDING`.
- CI/CodeQL/Publish/Deploy run IDs `PENDING`; DB backup `PENDING`;
  migration `20260906232403_AddCommentEffects` (schema `instagram`, additive,
  applied by the official workflow before image switch).
- Safe public smoke `PENDING`; live Meta Private Reply smoke:
  NOT RUN — no designated production test account/comment (never consume a
  customer's single allowed reply).

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