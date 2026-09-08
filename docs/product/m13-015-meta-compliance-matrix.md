# M13-015 — Meta compliance and production parity matrix

**Contract retrieval date:** 2026-09-08
**Qasedak Graph configuration:** Instagram API with Instagram Login; `graph.instagram.com`; `v26.0`; Instagram User access token; Business Login for Instagram.
**External approval evidence in repository:** none. App Review, Advanced Access, and Business Verification therefore remain external/unknown until an operator supplies Meta evidence.

## 1. Parity definition

Qasedak OpenReply parity means **parity for current-Meta-supported capabilities that Qasedak intentionally includes**. It does not mean copying every historical or current OpenReply feature. A feature that the current Instagram Login contract cannot expose, or that Qasedak intentionally excludes, is not a parity failure when classified truthfully below.

No parity path may use scraping, private Instagram APIs, browser/credential automation, fabricated follow state/history, or unsupported Ads/Tagging access.

## 2. Canonical classification vocabulary

Every capability below has exactly one primary classification, chosen only from:

- `Supported and implemented`
- `Supported but intentionally out of Qasedak scope`
- `Unsupported by current Meta contract`
- `Requires App Review / Advanced Access`
- `Requires production-only verification`
- `Unverified / externally blocked`

## 3. Final capability matrix

| Capability | Primary classification | Qasedak / provider note |
|---|---|---|
| Instagram professional account connection | Requires App Review / Advanced Access | Implemented with Business Login, state binding, protected long-lived token lifecycle. Standard Access is suitable only for operator-owned/managed test assets; external-customer SaaS use requires Meta approval. |
| Profile identity | Requires App Review / Advanced Access | Implemented, exact provider identity match required; no username-based durable routing. |
| Media catalog | Requires App Review / Advanced Access | Implemented, exact-account and bounded cursor traversal; no raw `paging.next` fetch. |
| Account insights | Requires App Review / Advanced Access | Implemented; `instagram_business_manage_insights`; permission loss degrades analytics only. |
| Media insights | Requires App Review / Advanced Access | Implemented with verified metric registry and truthful `NoData`/zero/unsupported states. |
| Follower snapshots/history | Requires App Review / Advanced Access | Implemented from the supported follower-count read; local history is Qasedak snapshots, not fabricated provider history. |
| Comment webhooks | Requires App Review / Advanced Access | Implemented through signed durable webhook inbox and exact-account normalization. |
| Live comment webhook handling | Requires App Review / Advanced Access | `live_comments` consumed and normalized; Private Reply remains broadcast-time constrained. |
| Private Reply | Requires App Review / Advanced Access | Implemented; one semantic effect per account/comment, 7-day creation window, Live-only-during-broadcast, Attempting/Uncertain crash boundary. |
| Public Comment Reply | Requires App Review / Advanced Access | Implemented and ledger-separated from Private Reply. |
| Normal Direct messaging | Requires App Review / Advanced Access | Implemented only after user-initiated messaging basis; a comment alone never opens the Direct window. |
| Inbound-DM automation | Requires App Review / Advanced Access | Implemented: signed webhook → exact account → conversation → automation → durable outbound effect. |
| Postback / reveal | Requires App Review / Advanced Access | Implemented; correlation token is hash-only at rest; postback redelivery yields one semantic reveal. |
| FollowGate relationship check | Unverified / externally blocked | A provider relationship adapter exists and is deterministic, but the shipped capability read model reports `FollowGate = Unsupported`. Ordinary template-postback consent as a sufficient profile-read basis is not treated as proven; Qasedak fails closed. |
| Delayed follow-up | Requires App Review / Advanced Access | Implemented as durable identifier-only work with participant/account/window revalidation and no resend after an interrupted Attempting state. |
| Comment reconciliation | Requires App Review / Advanced Access | Implemented as bounded owned-media recovery; converges with webhook semantic identity and never self-loops/makes unsafe-author triggers. |
| Conversation-history sync | Requires App Review / Advanced Access | Implemented within provider limits; imports never trigger automations. |
| Requests older inactivity coverage | Unsupported by current Meta contract | Inactive Requests can disappear from provider API coverage; omission is never modeled as deletion. |
| Ads | Unsupported by current Meta contract | Instagram API with Instagram Login cannot access Ads. No workaround is permitted. |
| Tagging | Unsupported by current Meta contract | Instagram API with Instagram Login cannot access Tagging. No workaround is permitted. |
| Publishing | Supported but intentionally out of Qasedak scope | Meta supports `instagram_business_content_publish`, but M13 implements no publishing. The permission is **not requested** by Qasedak. |
| Human Agent automation | Supported but intentionally out of Qasedak scope | Human Agent is not exposed as automated behavior. Qasedak does not use it to bypass the normal automation window. |
| Quick Replies | Supported but intentionally out of Qasedak scope | Incoming `message.quick_reply.payload` is normalized when Meta sends it; Qasedak does not expose a Quick Reply authoring product surface. |
| Generic templates | Supported but intentionally out of Qasedak scope | Not part of the M13 authoring surface; no parity requirement to expose every Meta template. |
| Private Reply interactive template | Unsupported by current Meta contract | Qasedak Private Reply uses the verified comment-addressed text operation; it does not invent an interactive-template variant. |
| Full lifetime conversation history | Unsupported by current Meta contract | Provider message-detail availability is bounded; Qasedak explicitly does not claim full lifetime sync. |
| Post-broadcast Live reconciliation | Unsupported by current Meta contract | Live Private Reply eligibility is during the broadcast; Qasedak does not fabricate post-broadcast eligibility. |
| Reply-tree reconciliation | Supported but intentionally out of Qasedak scope | Current reconciliation is the bounded owned-media comment scope needed by active automations; recursive reply-tree crawling is not an M13 feature. |

## 4. Permissions and least privilege

Qasedak's default Business Login scope set is now exactly:

1. `instagram_business_basic`
2. `instagram_business_manage_messages`
3. `instagram_business_manage_comments`
4. `instagram_business_manage_insights`

`instagram_business_content_publish` remains a known Meta permission constant for classification only and is excluded from `InstagramAuthorizationScopes.Default`.

| Permission | Qasedak feature need | Standard Access path | External-customer production | Degradation if absent |
|---|---|---|---|---|
| `instagram_business_basic` | connection, identity, media and common IG account reads | operator-owned/managed professional account added as app test asset | Requires App Review / Advanced Access | provider account cannot support the normal Instagram surface; reconnect/re-grant required |
| `instagram_business_manage_messages` | Direct messaging, inbound DM automation, Conversations/history, postbacks/reveal, delayed follow-up | app-role/test account messaging flow | Requires App Review / Advanced Access | messaging/reveal/history capabilities become permission-required; comments/insights remain independently usable when their scopes remain valid |
| `instagram_business_manage_comments` | comment/live webhook behavior, Private Reply, Public Reply, comment reconciliation | app-role/test media/comment flow | Requires App Review / Advanced Access | comment automation/replies/reconciliation degrade; messaging/insights remain independently usable |
| `instagram_business_manage_insights` | account/media insights and follower snapshots | app-role/test professional account | Requires App Review / Advanced Access | analytics/follower collection degrades only; account connection and messaging/comment capabilities remain usable |
| `instagram_business_content_publish` | none | not requested | not requested | no Qasedak feature degrades because publishing is intentionally out of scope |

No repository evidence proves any of these external approvals are currently Approved.

## 5. Production Meta HTTP adapter inventory

Shared authority: `MetaGraphTransport` provides timeout/caller-cancellation separation, structured Meta envelope parsing, bounded/redacted error prose and generic network failure results. Resource adapters build configured known-host/version paths and use Bearer authorization unless the official OAuth exchange contract itself requires query parameters.

| Application port | Infrastructure adapter / operation | Method and known path | Auth | Main deterministic authorities |
|---|---|---|---|---|
| `IMetaOAuthClient` | `GraphInstagramOAuthClient.ExchangeCodeAsync` | POST `api.instagram.com/oauth/access_token` | documented form includes client secret/code | `MetaOAuthAdapterTests`: exact form endpoint/body, rejection/non-JSON/transport/redaction |
| `IMetaOAuthClient` | `GraphInstagramOAuthClient.ExchangeShortLivedForLongLivedAsync` | GET `graph.instagram.com/access_token` | official OAuth query grant includes `client_secret` and short-lived `access_token` | `MetaOAuthAdapterTests`: exact grant/path, malformed/transport/redaction |
| `IMetaOAuthClient` | `GraphInstagramOAuthClient.RefreshLongLivedAsync` | GET `graph.instagram.com/refresh_access_token` | official OAuth query grant includes current `access_token` | `MetaOAuthAdapterTests`; refresh use-case + real-PG CAS tests |
| `IMetaTokenInspector` | `GraphInstagramTokenInspector.InspectAsync` | GET `/{version}/me?fields=id` | **Bearer header only** | `MetaGraphTransportTests.InspectorProbesVersionedMeAndKeepsTokenOutOfDetails`; `MetaTokenRedactionGateTests` |
| `IAccountProfileClient` | `GraphAccountProfileClient.GetProfileAsync` | GET `/{version}/me?fields=id,username,name,profile_picture_url` | Bearer | `GraphAccountProfileClientTests`: identity match, malformed, transient/permanent, no raw detail |
| `ISubscriptionClient` | `GraphSubscriptionClient.SubscribeAsync` | POST `/{version}/{IG_ID}/subscribed_apps` | Bearer + form fields | `GraphSubscriptionClientTests`: exact edge/fields, permission/rate/missing proof, token absent URL/body |
| `IMediaCatalogClient` | `GraphMediaCatalogClient.GetPageAsync/GetRecentAsync` | GET `/{version}/{IG_ID}/media` | Bearer | `GraphMediaCatalogClientTests`: path, parser, permission/auth/rate/malformed/redaction/cancellation/caps/loop/SSRF matrix |
| `IInstagramInsightsClient` | `GraphInstagramInsightsClient` account/media/follower reads | GET versioned account/media insights and account node | Bearer | `GraphInstagramInsightsClientTests`: exact metric sets, zero vs NoData, unsupported, permission/rate/5xx/malformed |
| `IInstagramMessagingClient` | `GraphInstagramMessagingClient.SendDirectAsync` | POST `/{version}/me/messages` | Bearer + JSON | `GraphInstagramMessagingClientTests`: exact payload, window signal, malformed, transport, redaction, cancellation, local content bounds |
| `ICommentPrivateReplyClient` | `GraphCommentPrivateReplyClient.SendPrivateReplyAsync` | POST `/{version}/{IG_ID}/messages` | Bearer + `recipient.comment_id` JSON | `GraphCommentPrivateReplyClientTests`: exact path/body, success IDs, permission/rate/5xx/timeout/network/cancellation/redaction |
| `ICommentPublicReplyClient` | `GraphCommentPublicReplyClient.SendPublicReplyAsync` | POST `/{version}/{comment_id}/replies` | Bearer + form message | `GraphCommentPublicReplyClientTests`: exact edge/body, malformed/rejection/timeout/cancellation/redaction |
| `ICommentReferenceReader` | `GraphCommentReferenceReader.ReadCreatedAtUtcAsync` | GET `/{version}/{comment_id}?fields=timestamp` | Bearer | `GraphCommentReferenceReaderTests`: exact path/timestamp, 404/provider/transport/no-token-URL |
| `IInstagramCommentHistoryClient` | `GraphInstagramCommentHistoryClient.ListCommentsPageAsync` | GET `/{version}/{media_id}/comments?...&after=` | Bearer | `GraphInstagramCommentHistoryClientTests`: exact fields/cursor, permission/auth/rate/5xx/malformed/network/cancel/redaction, hostile `paging.next` never fetched |
| `IInstagramConversationHistoryClient` | list conversations / messages / message detail | GET versioned account/conversation/message nodes | Bearer | `GraphInstagramConversationHistoryClientTests`: exact paths/platform, history-window classification, permission/rate/5xx/malformed/cancel, hostile `paging.next` never fetched |
| `IInstagramRelationshipClient` | `GraphInstagramRelationshipClient.GetFollowStateAsync` | GET `/{version}/{IGSID}?fields=is_user_follow_business` | Bearer | `GraphInstagramRelationshipClientTests`: host/version/path, true/false/malformed, permission/rate/auth/cancellation/no-token-URL |
| webhook verifier ports | `HmacWebhookSignatureVerifier` / `MetaWebhookSubscriptionValidator` | GET handshake; POST HMAC over exact raw bytes | verify token / app-secret HMAC | `MetaWebhookVerificationTests` + `MetaWebhookEndpointTests` complete HTTP matrix |

### OAuth URL credential exception

The “provider token must not appear in resource request URLs” rule is enforced for all normal Graph resource adapters. The Meta-owned OAuth long-lived-token exchange and refresh operations are a documented protocol exception: their current official contract places `access_token` (and exchange client secret) in the query. Qasedak therefore never logs those request URLs and returns only bounded/redacted failure metadata. It does **not** rewrite those OAuth endpoints into an invented Bearer contract.

## 6. Pagination / SSRF authority

- Media catalog reads only `paging.cursors.after`, wraps it in an account-bound Qasedak cursor, and ignores raw `paging.next`.
- Comment and conversation history treat the existence of `paging.next` as a has-more signal only; they extract the bounded `cursors.after` component and construct the next request from configured `GraphHost`, configured API version, and known resource path.
- Tests explicitly inject foreign HTTPS hosts, `localhost`, `127.0.0.1`, `169.254.169.254`, and unsupported schemes. None becomes a request target.
- Oversized cursors are rejected locally; repeated cursors terminate; traversal has explicit page/item caps and honors cancellation.

## 7. Webhook contract and subscription matrix

The server-owned subscription set is exactly `InstagramSubscriptionFields.Required`:

| Subscribed field | Why Qasedak needs it | Normalization / handler | Safe verification | Production expectation |
|---|---|---|---|---|
| `comments` | comment automations, Private/Public Reply origin, reconciliation convergence | `MetaPayloadNormalizer` → `InstagramCommentCreated` | signed sanitized comment fixture + endpoint HMAC tests | subscribed; comments access must be approved for external accounts |
| `live_comments` | distinguish Live eligibility from ordinary 7-day comment behavior | `MetaPayloadNormalizer` → `InstagramCommentCreated(IsLiveComment=true)` | current Live fixture semantic assertion | subscribed when comment capability is approved |
| `messages` | inbox projection, inbound-DM automation, reply correlation; `quick_reply.payload` is embedded here | `MetaPayloadNormalizer` → `InstagramMessageReceived` | text, `reply_to`, quick-reply and unsupported/share fixtures | subscribed when messaging access is approved |
| `messaging_postbacks` | reveal/CTA continuation | `MetaPayloadNormalizer` → `InstagramPostbackReceived` | sanitized current postback fixture + redelivery E2E | subscribed when messaging access is approved |
| `messaging_seen` | read state only; must never open reveal or trigger an automation | `MetaPayloadNormalizer` → `InstagramMessageRead` | current `read:{mid}` fixture | subscribed when messaging access is approved |

Unused published fields are not added merely because Meta exposes examples. Quick Reply has no separate Qasedak subscription field; it is normalized from a `messages` payload when present.

### HTTP security matrix

| Case | Expected / authority |
|---|---|
| GET valid mode/token/challenge | 200, challenge echoed as `text/plain` |
| wrong/missing token | 403 |
| wrong/missing/malformed mode | 403 |
| missing challenge | 403 |
| POST valid `sha256=` signature | accepted after raw-byte verification |
| invalid/missing/malformed signature | 401, empty body/no oracle |
| body modified after signing | 401 |
| valid HMAC over empty body | HMAC passes, JSON contract fails with 400; no bypass/crash |
| signed invalid JSON | 400 |
| valid JSON exactly at body bound | normal ingestion, 200 |
| body over bound | 413 before business effects |

Webhook receipt is at-least-once. The durable inbox and downstream semantic ledgers, not “exactly once HTTP delivery”, provide idempotent business outcomes.

## 8. Exact-account / cross-workspace negative matrix

Authorization is account-before-token/provider. Foreign/unknown resources use repository-standard 404 semantics after workspace membership authorization.

| Surface | Negative authority | Token/provider authority |
|---|---|---|
| connection/profile, repair, disconnect | `ConnectionEnrichmentEndpointTests.WorkspaceAndAccountOwnershipIsEnforced` | sibling workspace cannot repair/disconnect; account repository ownership check precedes token/provider |
| capabilities | `CapabilityEndpointTests.ExactAccountReturnsTokenFreeLocalProjectionAndForeignWorkspaceFailsClosed` | explicit zero token, media, insights, relationship, message, Private/Public Reply calls |
| media | `MediaCatalogEndpointTests.ForeignWorkspaceAccountIs404WithZeroTokenReadsAndZeroProviderCalls` | explicit zero token/provider |
| overview/account+media insights | `OverviewEndpointTests.ForeignWorkspaceOverviewIs404WithZeroTokenReadsAndZeroProviderCalls` | explicit zero token/provider |
| follower history | `OverviewEndpointTests.FollowerHistoryIsExactAccountBoundedAndSecretFree` | foreign real account returns 404 with explicit zero token/media/insights calls |
| history sync GET/POST | `ReconciliationAndHistorySyncFlowTests.ForeignWorkspaceHistorySyncGetAndPostAre404BeforeTokenOrProviderAccess` | real account from a different workspace; zero token reads and conversation-history calls |
| automation create account binding | `AutomationAccountBindingEndpointTests.ForeignWorkspaceBindingIsRejectedBeforeTokenOrProviderAccess` | `IChannelAccountBindingValidator` proves exact active account ownership; zero token/provider calls |
| automation edit binding | account binding is immutable; `BindingChangeThroughPutIsRejectedAsImmutable`; existing resolved binding is revalidated through `IChannelAccountBindingValidator` before draft revision | no token/provider access in authoring validator |
| outbound conversation reply | `ExactAccountRoutingTests.ForeignWorkspaceAccountReplyIsRejectedWithoutSend` | exact channel account and workspace must match before token/send |

The authoring validator is channel-neutral in Automations Application; `AutomationChannelAccountBindingValidator` is the composition-root Instagram ownership adapter. This preserves module boundaries while preventing arbitrary/foreign ConnectedAccount IDs from becoming durable automation bindings.

## 9. Real-PostgreSQL parity / concurrency authority

| Required invariant | Real PostgreSQL / API authority |
|---|---|
| Private Reply claim concurrency | `CommentEffectLedgerTests.ConcurrentSameKeyHasExactlyOneWinner`, `TwoMatchingAutomationsConcurrentlyProduceOneGlobalWinner` |
| Private/Public effect independence | `CommentEffectLedgerTests.PrivateReplyAndPublicReplyAreIndependent`; API `AutomationCapabilityWebhookFlowTests.PublicAndPrivateEffectsCoexistExactlyOnceEach` |
| Attempting crash boundary | `CommentEffectLedgerTests.AttemptMarkerIsIrreversibleAcrossRestart`; unit coordinator `AttemptingStateReplaysUncertainWithoutSecondCall` |
| comment webhook → reconciliation convergence | `ReconciliationAndHistorySyncFlowTests.SignedWebhookThenReconciliationConvergesOnOneLogicalTrigger` |
| reconciliation → webhook convergence | `ReconciliationAndHistorySyncFlowTests.ReconciliationThenSignedWebhookConvergesOnOneLogicalTrigger` |
| comment → opening → user response → postback → reveal | `RevealFlowWebhookFlowTests.FullRevealFlowThroughSignedWebhooksSendsExactlyOneOfEachEffect` |
| postback redelivery | `RevealFlowWebhookFlowTests.PostbackRedeliveryNeverSendsTheRevealTwice`; reveal store authority survives restart |
| reveal correlation/authority | `RevealFlowStoreTests.ConcurrentRevealAuthorityHasExactlyOneWinner`, `ReplyToCorrelationAndPendingScansAreDurableAcrossRestarts`, `CrashWindowAfterAttemptNeverYieldsASecondAuthority` |
| inbound DM → automation → Direct | `AutomationCapabilityWebhookFlowTests.InboundDmTriggerSendsDirectMessageOnceAndRedeliveryDoesNotRepeat` |
| history import never triggers automations | `ReconciliationAndHistorySyncFlowTests.HistorySyncImportsConversationsButNeverTriggersDmAutomations` |
| conversation backfill idempotency | `HistoryRepeatImportIsIdempotentWithoutUnreadInflation` |
| webhook first ↔ history second | `WebhookFirstThenHistoryImportKeepsOneRowAndOneUnread` |
| history first ↔ webhook second | `HistoryFirstThenWebhookAccountsUnreadExactlyOnce` |
| same provider MID across exact accounts | `SameProviderMidOnTwoExactAccountsCoexists` |
| scheduled follow-up restart | `AutomationCapabilityWebhookFlowTests.InterruptedFollowUpAttemptNeverResends` plus generic scheduled lease persistence |
| token refresh race | `InstagramPersistenceTests.ConcurrentTokenRefreshCommitsOneAuthoritativeRotation` (one `Rotated`, one `Stale`, winner ciphertext retained) |
| subscription repair race | `InstagramPersistenceTests.ConcurrentSubscriptionRepairPersistsOneAuthoritativeOutcome` |
| sentinel token at rest | `InstagramPersistenceTests.SentinelTokenPersistsOnlyAsCiphertextAndNeverInAccountOrOperationRows` |

Qasedak does not claim exactly-once Meta HTTP. It claims idempotent semantic outcomes backed by PostgreSQL uniqueness/CAS/attempt markers. A provider success followed by a process crash after persisted `Attempting` is ambiguous and is surfaced as `Uncertain`; automatic redelivery does not blindly repeat the mutation.

## 10. Scheduled work matrix

Actual M13 work types are:

- `instagram.token-refresh`
- `instagram.follower-snapshot`
- `instagram.comment-reconciliation`
- `instagram.conversation-history-sync`
- the composition-root automation follow-up work type (`FollowUpJobPolicy.WorkType`)

Database/platform authority is shared by `ScheduledWorkStoreTests` and `ScheduledWorkDispatcherTests`: idempotency-key collapse, concurrent multi-worker disjoint claims, lease expiry/reclaim, lost-lease rejection, retry/backoff/dead-letter, terminal completion/permanent failure, cancellation, faulting-handler retry, and settled-record non-reclaim.

Job-specific authorities:

| Job | Restart/retry/provider behavior | Payload contract |
|---|---|---|
| token refresh | `TokenRefreshScheduledHandlerTests`: malformed terminal, success chains one next occurrence, transient retryable, permanent/disconnected terminate | account ID only; `TokenRefreshPolicyTests.PayloadRoundTripsTheAccountIdAndNothingElse` |
| follower snapshot | `FollowerSnapshotScheduledHandlerTests`: daily chain, redelivery dedupe, NoData, transient retry, account/token terminal cases | account ID + UTC snapshot date; no token/text |
| comment reconciliation | real-PG `CommentReconciliationScheduledWorkTests`: bootstrap restart idempotency, fresh chained key, redelivery non-overlap, rate-limit chain, permanent stop | exact account ID only; bounded policy constants control traversal |
| conversation history | durable operation + continuation stage; operation coalescing, bounded cursor checkpoint, retryable/permanent outcomes | operation/account identifiers and stage; no message text/token/raw URL |
| delayed follow-up | `AutomationCapabilityWebhookFlowTests`: identifiers-only scheduling, due send once, disabled/window suppression, interrupted Attempting never resends | `runId` + `actionIndex`; content is reloaded from durable automation/run state, never placed in Platform payload |

`ScheduledWorkStoreTests.SecretShapedPayloadsAreRejectedAtEnqueue` explicitly rejects `M13_015_SENTINEL_TOKEN_DO_NOT_LEAK` and other token-shaped payloads.

## 11. Time/rate/permission truthfulness

- Private Reply uses provider comment creation time, not webhook receipt time. Boundary policy tests use an explicit clock around the 7-day rule; Live comments require live-broadcast eligibility rather than falling back to seven days.
- normal Direct/follow-up uses the 24-hour user-message window; delayed work revalidates at execution time.
- token refresh uses explicit expiry/minimum-age policy and CAS generation; rate/transport noise is retryable and does not mark the account disconnected.
- shared Meta taxonomy distinguishes auth/token invalid, permission loss, rate limit, transient/5xx, messaging-window expiry and malformed/contract drift.
- loss of `manage_insights` degrades analytics only; comment/message permission loss is capability-scoped rather than destroying unrelated valid state.
- revoked/expired token produces truthful health and prevents inappropriate provider traffic/retry storms.

## 12. Observability and redaction inventory

Operational logs use stable outcomes/IDs and never require raw tokens or arbitrary DM/comment/reveal/postback content. Message/comment observation logs use text length rather than text. No raw Graph response is logged.

| Area | Existing signal |
|---|---|
| Graph/provider failures | shared classified/bounded `MetaGraphError`; adapter-specific stable failure results; no raw body/token detail |
| OAuth/connect | structured connect failure/degraded subscription logs; account health/subscription state |
| token refresh | scheduled-work state + source-generated refresh outcome logs |
| subscription health/repair | persisted `SubscriptionHealth`/stable failure code + connection/repair logs |
| webhook receive/inbox | `WebhookMetrics`: received outcome, duration, duplicate deliveries, normalized/ignored/unrecognized fragments, backlog gauge; safe correlation logs |
| scheduled work | `scheduled_work.claimed`, `scheduled_work.finished`, `scheduled_work.unknown_type` plus handler logs |
| Private/Public Reply | `CommentEffectMetrics`: acquired/conflicted/rejected/attempted/succeeded/failed/uncertain with categorical effect/outcome/failure category |
| Direct Message | `MessageSendMetrics` categorical send outcome/content kind/failure category |
| postback/reveal | `RevealFlowMetrics` attempted/succeeded/uncertain/failed/suppressed with bounded operation/code |
| insights/follower snapshots | `InsightsMetrics` request outcomes, snapshot outcomes, contract drift; metric/media-kind only |
| comment reconciliation | sweep/comment counters + `scan_volume` histogram; no per-account/media IDs |
| conversation sync | operation/message counters + `volume` histogram; no per-account/conversation/message IDs |
| automation runs/effects | durable run/effect status plus safe status logs; no arbitrary message text in operational log templates |

Forbidden high-cardinality metric labels (`WorkspaceId`, `ConnectedAccountId`, provider/account/comment/message/conversation/participant/media IDs, token, arbitrary text) are absent. `MetricCardinalityTests` regression-protects this; counts such as media/pages/conversations/details are measurements, not tag values.

The sentinel `M13_015_SENTINEL_TOKEN_DO_NOT_LEAK` is regression-tested against request URI and provider-result detail across success, 401, 403, rate limit, 5xx, malformed JSON, timeout and unexpected transport exception. It is also rejected from scheduled payloads and verified absent from plaintext database/account/sync-operation rows; access-token persistence is encrypted ciphertext. API surfaces consume stable failure codes rather than raw provider bodies.

## 13. Conversation-history limitations

Qasedak intentionally models provider-limited history, not “full sync”:

- recent provider message detail may become inaccessible outside Meta's detail window; inaccessible old detail is **not deletion**;
- inactive Requests may be omitted by the provider; omission is **not deletion**;
- imported provider history updates Conversations projections but never emits the real-time automation trigger path;
- repeat import and webhook/history order converge on one logical message and correct unread semantics;
- same MID strings are scoped so different exact accounts/conversations can coexist;
- UI/docs must never claim full lifetime conversation history.

## 14. CI zero-live-Meta gate

M13-015 makes the default/CI contract executable rather than aspirational:

- `.github/workflows/ci.yml` step `Deny live Meta endpoints` maps `graph.instagram.com`, `api.instagram.com`, `graph.facebook.com`, and `www.instagram.com` to loopback before backend tests;
- `scripts/check_meta_ci_isolation.py` fails if the deny step/hosts disappear or CI begins consuming a production Meta/Instagram token secret;
- `scripts/verify.py` invokes that static gate;
- deterministic provider tests use scripted `HttpMessageHandler`/test stand-ins, never a customer account;
- real concurrency/persistence claims use local Testcontainers PostgreSQL, not live Meta;
- any future real-Meta harness must remain explicit operator opt-in, outside CI, with a designated test account. There is no fallback to a customer account.

Expected static success marker: `META CI ISOLATION CHECK PASSED`.

## 15. Official first-party sources

Facts above were freshly verified on **2026-09-08**; the Meta-owned Instagram Postman workspace showed an update on 2026-09-04. M13-015 reuses that evidence rather than substituting third-party protocol descriptions.

- Meta Instagram official workspace: <https://www.postman.com/meta/instagram/overview>
- Instagram API with Instagram Login: <https://www.postman.com/meta/instagram/folder/1z5vxzu/instagram-api-with-instagram-login>
- Meta Instagram API collection/documentation: <https://www.postman.com/meta/instagram/collection/6yqw8pt/instagram-api>
- Send API folder: <https://www.postman.com/meta/instagram/folder/uxudqu0/send-api>
- Private Reply current documentation/request: <https://www.postman.com/meta/instagram/documentation/6yqw8pt/instagram-api?entity=request-23987686-23eacf45-3728-4e41-bcc7-6d164959327c>
- Instagram Login overview / Send API limitations and access model: <https://www.postman.com/meta/instagram/documentation/6yqw8pt/instagram-api?entity=request-23987686-af579d08-121e-4897-8f45-5fd41ace49df>
- Conversations API with Standard/Advanced Access rules: <https://www.postman.com/meta/instagram/documentation/6yqw8pt/instagram-api?entity=request-23987686-ab559ffb-8e2c-4b0a-b43a-5737b6d2f672>
- Insights: <https://www.postman.com/meta/instagram/folder/23987686-f659d7d1-d74c-44e4-9192-9b1e8694c511>
- Webhook message/postback/seen examples: <https://www.postman.com/meta/instagram/documentation/6yqw8pt/instagram-api?entity=request-23987686-1ff01566-3509-48bd-a0f4-8571a91ccfdf>
- Business Login for Instagram: <https://developers.facebook.com/docs/instagram-platform/instagram-api-with-instagram-login/business-login>

The broader contract and revision history remain in `docs/product/meta-instagram-platform-contract.md`.

## 16. Final external status

| Gate | M13-015 status |
|---|---|
| code/provider-contract verification | Automated by repository tests/gates |
| deployment of the exact M13-015 SHA | Production-only; recorded after official deployment workflow succeeds |
| Meta App Review approval | **Unknown / externally blocked** — no external evidence in repository |
| Advanced Access approval | **Unknown / externally blocked** — no external evidence in repository |
| Business Verification | **Unknown / externally blocked** — no external evidence in repository |
| designated production Meta end-to-end smoke | Requires production-only verification; run only with an explicitly supplied production **TEST** Instagram account |

A green CI/deploy does not change any Meta approval status.
