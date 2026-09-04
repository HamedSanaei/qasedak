# Qasedak — Current Meta Instagram Platform Contract (M13-001)

**Task:** M13-001 · **Status:** Normative for M13-002 onward · **Verified:** 2026-09-04
**Method:** Fresh audit against current official Meta documentation on
`developers.facebook.com` (page revision dates recorded per row). Direct host
fetches are bot-blocked from automation (HTTP 400); pages were retrieved same-day
through full-text search-index retrieval of the official pages, which is
first-party Meta content, not community material. No blog, forum, Stack Overflow,
unofficial API or scraping source establishes any claim below. The official
Meta-owned Postman collection (`https://www.postman.com/meta/instagram/`,
retrieved 2026-09-04) was located as a supplementary source; it was not needed
because the documentation pages were reachable.

**Headline verdict:** Instagram Login is Qasedak's primary integration path for
every M13 capability (messaging, Conversations, Private Replies, public replies,
webhooks, media, insights). The August-2026 ADR-006 assumption that messaging
requires Facebook Login + Messenger Platform is **superseded** (see ADR-010).
Facebook Login is retained deliberately for a small set of extras, not as the
messaging default.

**Latest Graph API version observed:** `v26.0` (stated as latest on the Facebook
Login long-lived-token page, updated 2026-06-30; used across current examples).
Meta permits versioned (`https://<HOST>/<API_VERSION>/…`) calls; Qasedak's
current adapters call unversioned hosts. M13-003 centralizes one configured
version — this document does **not** freeze a version, it records v26.0 as the
latest observed on 2026-09-04.

## 1. Authority order used here

1. Current official Meta documentation (cited per row).
2. Official Meta-owned Postman collection (supplementary only).
3. Qasedak architectural invariants.
4. Existing Qasedak implementation (audited, not changed).
5. OpenReply reference (behavior only; never overrides Meta policy).

## 2. Connection paths (current)

| Path | Login | Token | Host | Page required |
|---|---|---|---|---|
| Instagram API with Instagram Login (**primary**) | Business Login for Instagram | Instagram User access token (long-lived, 60-day, refreshable) | `graph.instagram.com` (+ `api.instagram.com` for code exchange, `www.instagram.com` for authorize) | No |
| Instagram API with Facebook Login (retained extras) | Facebook Login for Business (incl. `IG_API_ONBOARDING` channel) | Facebook User / Page access token (Page tokens from long-lived User tokens carry no scheduled expiry) | `graph.facebook.com` | Yes (Page linked to the professional account) |
| Instagram Messaging via Messenger Platform | Facebook Login | Page access token | `graph.facebook.com` | Yes |

Sources: Instagram Platform overview (updated 2026-04-17); Business Login for
Instagram (updated 2026-03-13); Facebook Login long-lived tokens (updated
2026-06-30); Instagram Messaging overview (updated 2026-06-26).

## 3. Provider-contract matrix

Columns: capability · path · login · token · host · endpoint/shape · permission(s) ·
access level · App Review · identity · webhook field(s) · window · status ·
Qasedak implication · source (verification date 2026-09-04 unless noted).

### 3.1 Authentication and identity

| Capability | Path / login / token / host | Endpoint/shape · permissions · access | Status · Qasedak implication |
|---|---|---|---|
| Authorize (code) | IG Login · Business Login · n/a · `www.instagram.com` | `GET /oauth/authorize?client_id&redirect_uri&response_type=code&scope&state`; scopes `instagram_business_basic`, `instagram_business_content_publish`, `instagram_business_manage_messages`, `instagram_business_manage_comments`; old `business_*` names deprecated 2025-01-27; optional `state`, `force_reauth`, `enable_fb_login` | Supported · matches `InstagramAuthorizationUrlBuilder`; scope set already current — no code change in M13-001 |
| Code exchange | IG Login · server-side · app secret · `api.instagram.com` | `POST /oauth/access_token` form (`client_id`, `client_secret`, `grant_type=authorization_code`, `redirect_uri`, `code`) → `{data:[{access_token,user_id,permissions}]}`; code valid 1h, single use | Supported · matches `GraphInstagramOAuthClient` |
| Short→long-lived | IG Login · server-side · `graph.instagram.com` | `GET /access_token?grant_type=ig_exchange_token&client_secret&access_token` → `{access_token,token_type:"bearer",expires_in}` (~60 days) | Supported · matches implementation |
| Refresh | IG Login · `graph.instagram.com` | `GET /refresh_access_token?grant_type=ig_refresh_token&access_token`; preconditions: token ≥24h old, still valid, `instagram_business_basic` granted; refreshed token valid 60 days; unrefreshed 60-day tokens expire permanently | Supported · matches lifecycle doc + `EvaluateAccountHealthUseCase` window; M13-004/005 schedule the job |
| FB User long-lived | FB Login · `graph.facebook.com` | `GET /oauth/access_token?grant_type=fb_exchange_token&client_id&client_secret&fb_exchange_token` (~60 days) | Supported · retained FB-path lifecycle unchanged |
| FB Page token | FB Login · `graph.facebook.com` | From long-lived User token via `GET /{user-id}/accounts`; no scheduled expiry; invalidation is event-driven (password change, revocation, role loss) | Supported · matches `ConnectedAccount` null-expiry semantics |
| `/me` identity | IG Login · IG User token · `graph.instagram.com` | `/me` = the app user's Instagram professional account; `/me/conversations`, `/me/messages` aliases for `/<IG_ID>/…` | Supported · M13-005 must persist IG_ID distinctly from the app-scoped `user_id` returned at code exchange |
| IG professional ID | Either path | Numeric IG-scoped professional account ID (`IG_ID`); distinct from app-scoped user id and from conversation-partner IGSIDs | Supported · M13-002 exact-account key input |
| IGSID (recipient identity) | IG Login | Instagram-scoped ID of the conversation partner; used as `recipient.id`, `user_id` lookup, webhook `sender.id`/`from.id` | Supported · participant identity for M13-002/008/013 |

Sources: Business Login for Instagram (2026-03-13); Access Token reference
(2026-03-09); Refresh Access Token reference (2025-07-17); Conversations API
with IG Login; Quick Replies with IG Login (2026-06-30).

### 3.2 Messaging (Instagram Login — primary)

| Capability | Endpoint/shape · permissions | Window / constraint | Status · Qasedak implication |
|---|---|---|---|
| Send text | `POST /<IG_ID>/messages`, `recipient:{id:<IGSID>}`, `message:{text}`; Bearer IG User token; `instagram_business_basic` + `instagram_business_manage_messages` | Conversations begin **only** when the IG user messages first; app has **24h** to respond to each user message; window error is code `10` + `error_subcode` `2534022` ("sent outside of allowed window") | Supported · M13-003 must replace the stale code-`490` mapping (no official 490 exists in current tables); M13-010 builds templates on this |
| Quick replies | Same endpoint; `message:{text,quick_replies:[{content_type,title,payload}]}`; max **13** buttons, **20** chars each; `text`/`user_phone_number`/`user_email` | 24h window; tap delivers a `messages` event with `quick_reply.payload` + `mid` + title text | Supported · M13-010 |
| Button template | Same endpoint; `message.attachment:{type:template,payload:{template_type:button,text≤640 chars,buttons:[1..3 × postback|web_url]}}` | 24h window; postback tap delivers `messaging_postbacks` | Supported · M13-010 |
| Media/link/sticker/reaction/attachment sends | Same endpoint family; audio, images, owned-post `MEDIA_SHARE`, links, stickers, reactions, video, PDF | 24h window; app user must own shared posts; one customer per conversation (no groups) | Supported · M13-010 scope is text + postback/URL buttons (+ safe fallbacks); richer attachments stay out unless a later task justifies them |
| Human Agent extension | `human_agent` tag on a human-operator response | Up to **7 days** from the user's message; **human agents only** — automation use is a policy violation; requires the Human Agent feature (App Review + Business Verification) | Supported as **operator-only** · Qasedak keeps Human Agent out of automation (M13-010/011/014 must not expose it as automation) |
| Sponsored / One-Time / Marketing / News messages; non-human tags (`ACCOUNT_UPDATE`, `CONFIRMED_EVENT_UPDATE`, … — rejected with error 100 since 2026-04-27) | n/a | n/a | **Not available for Instagram Messaging** · excluded from M13 scope; no workaround designs permitted |

Sources: Send Messages with IG Login (2026-05-06); Quick Replies with IG Login
(2026-06-30); Button Template with IG Login (2026-06-30); Human Agent
features-reference; Send a Message — Messenger Platform (2026-08-11);
Messenger Common Error Codes (2026-06-25).

### 3.3 Conversations API (Instagram Login — supported)

| Capability | Endpoint/shape · permissions | Limits | Status · Qasedak implication |
|---|---|---|---|
| List conversations | `GET /<IG_ID>/conversations` or `/me/conversations?platform=instagram`; IG User token of a person who can manage messages; `instagram_business_basic` + `instagram_business_manage_messages`; Standard (own accounts in dashboard) vs Advanced (third-party) | Requests-folder threads inactive 30+ days are excluded; shares return URL only | Supported · M13-013 Phase B |
| Find thread for a person | Same endpoint + `user_id=<IGSID>` | Same | Supported · exact-account + participant resolution for M13-002/013 |
| List messages | `GET /<CONVERSATION_ID>?fields=messages` | Returns message IDs; **only the 20 most recent** messages have retrievable details (older read as deleted) | Supported with hard history limit · M13-013 must document/test the 20-message bound |
| Message details | `GET /<MESSAGE_ID>?fields=id,created_time,from,to,message` | Default fields `id`, `created_time` | Supported · direction/body/provider-ID mapping for M13-013 |
| Creator pre-condition | n/a | A Creator account must call the Conversations API before it can receive webhooks | External prerequisite · M13-005 connection checklist |

Sources: Get Conversations with IG Login (examples at v25.0/v26.0); Conversations
API for Messenger Platform (2026-02-11); Instagram Messaging overview
(2026-06-26).

### 3.4 Comment Private Replies (both paths — current rules)

| Item | IG Login | FB Login / Messenger |
|---|---|---|
| Endpoint | `POST https://graph.instagram.com/<VER>/<APP_USERS_IG_ID>/messages`, `recipient:{comment_id}`, `message:{text}`, Bearer IG User token | `POST /<PAGE_ID>/messages`, `recipient:{comment_id}`, Page access token |
| Permissions | `instagram_business_basic` + `instagram_business_manage_comments` | `instagram_basic` + `instagram_manage_comments` + `pages_messaging` (+ `pages_read_engagement`; `ads_management`/`ads_read` when Page role came via Business Manager) |
| Rules | **One** message per commenter; within **7 days** of comment creation (posts, reels, stories, ad posts); **Live: during broadcast only**; follow-ups only after the recipient responds, within **24h**; reply lands in Inbox (follower) or Requests; includes a link to the post | Same rules; additionally requires the Human Agent feature + Advanced Access per the Messenger guide |
| Webhooks | `comments`, `live_comments` (payload carries IG professional ID, commenter IGSID + username, comment ID, media id/product-type, text; ads add `ad_id`/`ad_title`; boosted posts may duplicate) | Same fields |
| Response | `{recipient_id:<IGSID>, message_id:<mid>}` | Same |

**Consequence:** a comment does **not** open the 24h DM window — the normal
`recipient.id` send fails with 10/2534022; the Private Reply endpoint is the
only first-contact route. Qasedak's M06-005 normal-DM route is therefore
incorrect and M13-009 replaces it. No code changes in M13-001.

Sources: Send a Private Reply to a Commenter — Instagram Platform
(2026-06-30); Private Replies — Instagram Messaging (2026-07-02); community
field report of 10/2534022 on comment-triggered normal send (2026-02,
terminology corroboration only).

### 3.5 Public comment replies (both paths — current)

`POST /<IG_COMMENT_ID>/replies?message={text}` (also `GET …/replies`,
`POST …?hide=`, `DELETE …`, `POST /<IG_MEDIA_ID>` to disable/enable,
`GET /<IG_MEDIA_ID>/comments`). IG Login: IG User token on
`graph.instagram.com`, `instagram_business_basic` +
`instagram_business_manage_comments`. Limits: top-level comments only (replies
attach to the top-level comment); no replies to hidden comments; no replies on
live video (use Private Reply); `username` field requires the manage-comments
permission (since 2024-08-27); restricted-user comments hidden until approved.
M13-009/M13-012 implement this as the operation distinct from Private Reply.

Sources: Comment Moderation guide (2025-06-02); IG Comment reference;
IG Comment Replies reference.

### 3.6 Webhooks (Instagram Login — subscription fields + payload shapes)

Subscribe: `POST https://graph.instagram.com/v26.0/<IG_ID>/subscribed_apps?subscribed_fields=comments,messages`
→ `{success:true}` (example from the official setup guide, updated 2026-03-03).
App must be **Live** to receive notifications; testers need app + professional-account roles.

| Field | IG Login permission | Shape (current) | Qasedak implication |
|---|---|---|---|
| `comments` / `live_comments` | basic + manage_comments; **Advanced Access** | `changes[]:{field,value:{id,from:{id:IGSID,username},text,media:{id,media_product_type}}}` | Supported · already normalized (`InstagramCommentCreated`); M13-008 adds media/username/timestamp |
| `messages` | basic + manage_messages | `messaging[]:{sender:{id},recipient:{id},timestamp,message:{mid,text?,is_echo?,is_deleted?,is_unsupported?,is_self?,quick_reply:{payload}?,attachments?…}}`; echoes mirror own sends; reactions/edits exist | Supported · already normalized; M13-008 tightens echo/deletion/unsupported/self filtering |
| `messaging_postbacks` | basic + manage_messages | `messaging[]:{…,postback:{mid,title,payload[,referral]}}` (mid present since v11.0); icebreaker/CTA taps | Supported · **M13-008 implements** `InstagramPostbackReceived{mid,title,payload}` |
| `messaging_seen` | basic + manage_messages | `messaging[]:{…,read:{mid}}` — **message ID, not a watermark** | Supported · **M13-008 implements** `InstagramMessageRead{mid}`; any `read.watermark` assumption must not be built |
| `messaging_handover`, `messaging_optins`, `messaging_referral`, `standby` | basic + manage_messages | Per official examples | Subscribed as needed; `standby`/handover out of M13 scope unless multi-app arises |
| `messaging_policy_enforcement`, `response_feedback`, `story_insights`, insights webhooks | n/a (Messenger/FB-Login only) | n/a | **Unavailable on IG Login** · excluded |

Verification (`X-Hub-Signature-256` HMAC-SHA256 over raw bytes; challenge
handshake) is unchanged and matches ADR-007 + implementation.

Sources: Setup Webhook Subscriptions — Instagram Platform (2026-03-03);
Webhook Notification Examples — Instagram Platform (2025-11-24); Webhooks for
Instagram Messaging (field table).

### 3.7 Media (Instagram Login — supported, catalog only)

`GET /<IG_ID>/media` → IDs; `GET /<MEDIA_ID>?fields=id,media_type,media_url,thumbnail_url,permalink,caption,timestamp,owner,like_count,comments_count,children`
(media_type IMAGE/VIDEO/REELS/CAROUSEL_ALBUM; reels/video/carousel
distinguishable). Basic permission suffices for reads. FB-Login-only fields
(`boost_ads_list`, `collaborators`, ad `@`-caption, `view_count`, …) are out of
scope. **No content publishing in M13** (permission existence does not expand
scope). M13-006 builds the picker on this.

Sources: IG Media reference (updated 2026-08-12); IG Login overview.

### 3.8 Insights (Instagram Login — supported with boundaries)

Permission `instagram_business_manage_insights` (introduced 2025-03-24;
Advanced Access required for third-party accounts). Account:
`GET /<IG_ID>/insights`; media: `GET /<MEDIA_ID>/insights`; periods
day/week/days_28/month/lifetime/total_over_range; media-type-dependent metric
tables; `total_*` aggregated (ads-inclusive) metrics are **FB Login only**;
`story_insights` webhook + insights webhook are **FB Login only**; media data
kept 2 years (up to 48h delay), account series 90 days; `follower_count` /
`online_followers` need 100+ followers; EU/Japan story `replies` excluded.
M13-007 implements organic metrics + follower snapshots with degradation.

Sources: Insights guide (2025-01-21); Account Insights (2026-06-16); Media
Insights (2026-06-18); insights-on-IG-Login blog (2025-03-24).

### 3.9 Relationship / follow status — SUPPORTED WITH USER-CONSENT CONSTRAINTS

> **Correction 2026-09-05 (M13-001):** the 2026-09-04 version of this section
> wrongly classified per-user follow status as globally unsupported. The
> official Instagram User Profile API with Instagram Login exposes
> `is_user_follow_business`. What follows supersedes that conclusion; the old
> claim is preserved only in git history, not in this document.

Endpoint: `GET https://graph.instagram.com/<IGSID>` with `fields=` listing the
wanted profile fields. Token: Instagram User access token of the app user who
can manage messages on the professional account. Permissions:
`instagram_business_basic` + `instagram_business_manage_messages`. Access:
Advanced (third-party) / Standard (own dashboard accounts). IGSID comes from a
messaging webhook notification (`messages.sender.id`, postback/referral
senders). Supported fields: `name`, `username`, `profile_pic` (expires after
days), `follower_count` (the *user's* followers), `is_user_follow_business`
(**whether the Instagram user follows your app user**), `is_business_follow_user`,
`is_verified_user`. Blocked users are unviewable.

**User-consent requirement (official):** consent is set **only** when the user
sends a message to the app user, or clicks an icebreaker or persistent menu. A
user who merely comments (post/comment) does **not** grant profile access —
lookup fails with `User consent is required to access user profile` (a
distinguishable, non-retryable-until-consent signal; M13-011 must not poll on
it). Persistent-menu clicks arrive as `messaging_postbacks` **and** open the
standard 24h window (persistent-menu guide) — they are documented
consent-establishing events. Whether an *ordinary* button-template/quick-reply
postback tap from our own message establishes consent is **not explicitly
proven** by current documentation, even though `messaging_postbacks` is a
listed profile-lookup subscription.

| Capability | Status |
|---|---|
| User Profile API (`GET /<IGSID>`) | Supported |
| `is_user_follow_business` | Supported |
| Existing-consented messaging-user lookup (sent message / icebreaker / persistent menu) | Supported |
| Raw comment → immediate follow lookup | Unsupported (missing consent — official error) |
| Opening Private Reply → ordinary template postback → follow lookup | **Unverified / requires provider-contract or production-safe verification** (NOT globally unsupported; NOT assumed supported) |
| No-consent polling / blind retries | Forbidden (wastes rate budget; treat consent error as definitive until a consent event arrives) |
| Scraping / private API fallback | Forbidden |

**Consequence:** M13-011 implements the relationship/profile port
(follows / does-not-follow / unavailable-unknown) behind a provider
capability/policy switch: query in Case A (consented), never blindly in Case B
(unconsented — continue via an allowed path), keep the Follow Gate switched off
until Case C (ordinary-postback consent) is proven. M13-012 stays decoupled
from M13-011; M13-014/015 consume the gate conditionally. Business Discovery
remains the FB-only aggregate-counts surface, not a substitute.

Sources: Instagram User Profile API with IG Login (page 2025-01-21; v25.0/v26.0
examples); User Profile API — Instagram Messaging/Messenger (updated
2026-04-01, same consent sentence, FB path); Persistent Menu with IG Login
(updated 2026-06-30, postback + window semantics).

### 3.10 App Review / access levels (current)

Standard Access: own/managed accounts added in the App Dashboard (development).
Advanced Access: any third-party account (App Review + Business Verification +
screencasts of exact flows). Live app required for all webhook delivery;
`comments`/`live_comments` additionally require Advanced Access; testers need
app role + all permissions + professional-account role. Account-side
prerequisite: Instagram Settings → Message controls → Connected Tools → Allow
Access to Messages. No Meta call in CI ever; production tenants need completed
review — external dependency, tracked for M13-015/M11-style rehearsal, not a
coding task.

Sources: repeated Access-Level blocks across Business Login (2026-03-13),
Messaging (2026-05-06), Conversations, Comment Moderation, Insights;
IG Messaging overview testing limitations (2026-06-26).

## 4. What changed since August 2026 (ADR-006 deltas)

1. Instagram Login now directly supports Send API messaging, Conversations API,
   Private Replies, public replies, full webhook field set, media reads and
   insights — the Messenger-Platform-only premise is gone.
2. Private Reply endpoint shape on IG Login is `POST /<IG_ID>/messages` with
   `recipient.comment_id` (Bearer IG User token), not only `/<PAGE_ID>/messages`.
3. Read receipts are `read:{mid}` (message ID); there is no `watermark` in the
   current Instagram contract.
4. Window expiry surfaces as Graph code `10` + `error_subcode` `2534022`
   (Messenger Common Error Codes, 2026-06-25); code `490` appears in no current
   official table — the existing adapter mapping is stale input to M13-003.
5. `instagram_business_manage_insights` exists and works on IG Login (since
   2025-03-24); aggregated `total_*` metrics and insight webhooks stay FB-only.
6. Latest observed Graph version is v26.0; versioned calls are the documented
   form; Qasedak's unversioned hosts are M13-003 input, not a violation.
7. Message tags other than `human_agent` are unusable for Instagram (deprecated
   tags now error 100); Sponsored/One-Time/Marketing/News messages are
   unavailable for Instagram Messaging — M13-010/011 must not plan on them.
8. (Correction 2026-09-05.) The User Profile API exposes `is_user_follow_business`
   on Instagram Login, gated by user consent (sent message / icebreaker /
   persistent menu; comment-only fails officially); ordinary template-postback
   consent is unverified — see §3.9.

## 5. Explicit answers (§25 of the M13-001 instruction)

1. **Is Instagram Login primary?** Yes — for every M13 capability.
2. **Direct Send API on IG Login?** Yes — `POST /<IG_ID>/messages`, IGSID recipient.
3. **Conversations API on IG Login?** Yes — `/me/conversations`, messages, details (20-recent bound).
4. **Token type?** Instagram User access token (long-lived 60-day, refreshable).
5. **Hosts?** `graph.instagram.com` for all M13 runtime calls; `api.instagram.com`
   (code exchange) and `www.instagram.com` (authorize) for login;
   `graph.facebook.com` only for retained FB-path extras.
6. **Messaging permissions?** `instagram_business_basic` + `instagram_business_manage_messages`.
7. **Comment permissions?** `instagram_business_basic` + `instagram_business_manage_comments`.
8. **Insights permissions?** `instagram_business_basic` + `instagram_business_manage_insights`.
9. **Webhook fields?** `comments`, `live_comments`, `messages`, `messaging_postbacks`,
   `messaging_seen`, `messaging_handover`, `messaging_optins`, `messaging_referral`,
   `standby` (as needed); subscribed via `POST /<IG_ID>/subscribed_apps`.
10. **Private Reply rule?** One per comment; 7 days (post/reel/story/ad); Live
    during broadcast only; comment-ID addressing; follow-ups need a recipient
    response + 24h window.
11. **Messaging-window rule?** 24h from the user's last message; error
    10/2534022; comments do not open the window.
12. **Postback shape for M13-008?** `postback:{mid,title,payload[,referral]}` in `messaging[]`.
13. **Read/Seen shape for M13-008?** `read:{mid}` — message ID, never a watermark.
14. **Follow Status usable?** Yes, with user-consent constraints: `GET /<IGSID>`
returns `is_user_follow_business`; works for consented users (sent message /
icebreaker / persistent menu); raw-comment lookup fails officially; ordinary
template-postback consent is Unverified (§3.9).
15. **Standard/Advanced?** Standard = own dashboard accounts; Advanced =
    third-party (review + verification); Live app + roles for webhooks; Advanced
    for comments/live_comments.
16. **Review/Verification externals?** App Review per permission, Business
    Verification, Live app, exact-flow screencasts, Creator Conversations-API
    pre-call, Connected-Tools toggle.
17. **Human Agent for automation?** No — human/operator only, 7-day tag, feature approval required.
18. **ADR-006 superseded assumptions?** Messenger-only messaging; FB Login
    required for messaging; Page required for all messaging; IG-Login-only path
    "limited"; (implementation-level) 490 mapping and any watermark-shaped read
    handling.
19. **Still need Facebook Login?** Retained, not primary: existing FB-path
    accounts/health lifecycle, Business Discovery, `total_*` insights,
    `story_insights` webhook, hashtag search, ads/shopping (out of M13 scope).
20. **OpenReply behaviors excluded?** Out-of-window proactive sends; Sponsored /
    One-Time / Marketing / News messages; consent-less follow-gate enforcement; publishing;
    ads/shopping/hashtag/Business-Discovery features; group messaging; folder
    semantics; >20-message history detail; `total_*` on the IG path; insight
    webhooks on the IG path; non-`human_agent` tags.

## 6. Carried-over assumptions that M13 implementation must still verify

- Outbound text limit (current code: 1000 chars) — seen in community references
  only; M13-010 must pin it against the Send API reference before enforcing.
- Exact messaging/Private-Reply rate-limit arithmetic — the official
  rate-limit documents are the normative pointer; no community numbers are
  adopted here; M13-009/010 verify at implementation time (as the original
  matrix already prescribed).
- `message_echoes` / `message_reactions` / `message_edit` subscription-field
availability on the IG-Login path — observed in the examples index; M13-008
confirms against the live subscription table before subscribing.
- Whether an ordinary button-template/quick-reply postback tap establishes User
Profile consent — current docs prove it only for sent messages, icebreakers
and persistent-menu clicks; M13-011 keeps the Follow Gate behind a
capability/policy switch until provider-contract or production-safe
verification proves the ordinary-postback case.

## 7. Sources (retrieved 2026-09-04 unless marked 2026-09-05; page revision dates as shown)

- Instagram Platform overview — updated 2026-04-17
- Business Login for Instagram — updated 2026-03-13
- Access Token reference — updated 2026-03-09
- Refresh Access Token reference — updated 2025-07-17
- Send Messages with IG Login — updated 2026-05-06
- Quick Replies with IG Login — updated 2026-06-30
- Button Template with IG Login — updated 2026-06-30
- Send a Private Reply to a Commenter (Instagram Platform) — updated 2026-06-30
- Private Replies (Instagram Messaging / Messenger) — updated 2026-07-02
- Get Conversations with IG Login (v25.0/v26.0 examples)
- Conversations API for Messenger Platform — updated 2026-02-11
- Instagram Messaging overview — updated 2026-06-26
- Setup Webhook Subscriptions (Instagram Platform) — updated 2026-03-03
- Webhook Notification Examples (Instagram Platform) — 2025-11-24
- Comment Moderation guide — updated 2026-06-02
- IG Comment / IG Comment Replies / IG Media references (Media updated 2026-08-12)
- Insights guide (2025-01-21); Account Insights (2026-06-16); Media Insights (2026-06-18)
- Insights on IG Login announcement — 2025-03-24
- Business Discovery — updated 2026-08-12
- Human Agent features-reference (7-day tag; review + verification)
- Send a Message (Messenger Platform) — updated 2026-08-11 (tags, unavailable message types, error table)
- Messenger Common Error Codes — updated 2026-06-25 (10/2534022)
- Instagram Platform error codes — updated 2026-06-02
- Facebook Login long-lived tokens — updated 2026-06-30 ("Latest Graph API Version: v26.0")
- Instagram User Profile API with IG Login — page 2025-01-21, v25.0/v26.0 examples (endpoint, fields, consent rule; retrieved 2026-09-05)
- User Profile API — Instagram Messaging/Messenger — updated 2026-04-01 (same consent sentence, FB path)
- Persistent Menu with IG Login — updated 2026-06-30 (menu postback + 24h window semantics)
- Facebook Login for Business IG onboarding — 2025-05-29
- Instagram API with Instagram Login overview — 2025-01-21 (scope names; 2025-01-27 deprecation)
- Meta-owned Postman collection `postman.com/meta/instagram` — located 2026-09-04 (supplementary; documentation pages sufficed)
