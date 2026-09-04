# Qasedak — Instagram MVP Capability Matrix

> **Historical document (August 2026, M01-001).** Its messaging-path claims
> (Messenger-Platform-only messaging, Facebook Login required, Page required)
> are **superseded** by `meta-instagram-platform-contract.md` (M13-001,
> verified 2026-09-04) and ADR-010. Preserved verbatim below as research
> evidence; do not treat it as the current contract.

**Task:** M01-001 · **Status:** Verified against official Meta documentation (fetched August 2026)  
**Method:** Every capability below was checked against Meta's current developer documentation on `developers.facebook.com` during this task. No capability is asserted from memory or third-party blogs. Sources are cited inline and listed at the end.

## 1. Purpose

Qasedak's first product milestone automates Instagram direct-message workflows (comment-triggered DMs, inbox, replies). This document states which of those desired capabilities are possible through official Meta APIs today, under which integration path, permissions, access tier, and time-window constraints — and what that means for the module boundaries chosen in M02–M06.

## 2. Integration paths (as documented by Meta)

Meta documents three coexisting integration components for Instagram professional accounts. The webhook requirements table ([Instagram Platform Webhooks](https://developers.facebook.com/docs/instagram-platform/webhooks)) states their contractual differences:

| Component | Access tokens | Base URL | Basic permission | Notes |
|---|---|---|---|---|
| Business Login for Instagram ("Instagram API with Instagram Login") | Instagram User access token | `graph.instagram.com` | `instagram_business_basic` | No Facebook Page required |
| Facebook Login for Business ("Instagram API with Facebook Login") | Facebook User or Page access token | `graph.facebook.com` | `instagram_basic` | Requires Page linked to the professional account |
| Instagram Messaging via Messenger Platform | Facebook User or Page access token | `graph.facebook.com` | `instagram_basic` (+ field-specific) | The messaging path |

All three require **Business Verification**, and comments-related webhooks require **Advanced Access** (same source).

## 3. Desired capabilities vs. verified reality

Legend: ✅ supported officially · ⚠️ supported with binding constraints · ❌ not available.

| # | Qasedak capability | Status | Binding constraints (all from cited official docs) |
|---|---|---|---|
| C1 | Detect new comments on a connected professional account's media | ⚠️ | `comments` / `live_comments` webhook fields need **Advanced Access**; app must be **Live**; owning account must be **public**; live-comment notifications arrive only during the broadcast [Webhooks](https://developers.facebook.com/docs/instagram-platform/webhooks) |
| C2 | Trigger a DM to a commenter (comment → DM automation) | ⚠️ | Implemented as **Private Reply**: exactly **one** DM to the commenter, sendable within **7 days** of comment creation (posts, ads posts, reels); for Instagram Live **only during the broadcast**; delivered to Inbox if commenter follows the account, otherwise to Requests; requires the Page-linked professional account, a Page access token, and `instagram_manage_comments` + `pages_messaging` via Facebook Login [Private Replies](https://developers.facebook.com/docs/messenger-platform/instagram/features/private-replies) |
| C3 | Continue conversation after the automated DM | ⚠️ | Only inside the **24-hour standard messaging window** opened by the user's reply; within the window even promotional content is permitted [Policy Overview](https://developers.facebook.com/docs/messenger-platform/policy/policy-overview), [Private Replies](https://developers.facebook.com/docs/messenger-platform/instagram/features/private-replies) |
| C4 | Operator replies outside the 24-hour window | ⚠️ | **Human Agent tag** allows manually responding to user messages within a **7-day period**; positioned for human support responses, not bulk automation [Policy Overview](https://developers.facebook.com/docs/messenger-platform/policy/policy-overview) |
| C5 | Receive inbound DMs for the inbox | ✅ | `messages` webhook events when an Instagram user messages the connected professional account; app must be Live [Webhooks](https://developers.facebook.com/docs/instagram-platform/webhooks) |
| C6 | Proactive/outbound campaigns after window close | ❌ | One-time Notifications, Sponsored Messages, and News messaging are **not available for IG Messaging API** [Policy Overview](https://developers.facebook.com/docs/messenger-platform/policy/policy-overview) |
| C7 | OAuth connection without a Facebook Page | ✅ (limited) | Business Login for Instagram issues Instagram-scoped IDs/tokens without a Page, but messaging capabilities route through the Messenger Platform with Facebook Login tokens (see §2) — so full MVP workspaces will connect via Facebook Login for Business |
| C8 | Automated experience disclosure | ⚠️ | Where required by law, automated chats must disclose they are automated at conversation start, after significant lapse, or at human→automation handoff [Policy Overview](https://developers.facebook.com/docs/messenger-platform/policy/policy-overview) |

### 3.1 Consequence for the M05/M06 flows

The flagship "comment-to-DM" automation (M06-005) is feasible **only** as a Private Reply:

1. one automated DM per comment (idempotency key = comment ID — duplicates are a policy violation risk, and Meta explicitly warns boosted/ads posts can produce **duplicate webhook notifications**);
2. send deadline = min(7 days from comment creation, broadcast end for Live);
3. all later automation must yield to the 24-hour window state owned by Conversations.

## 4. Access-tier and review constraints

- **Advanced Access** is mandatory for `comments` and `live_comments` webhook notifications; App Review plus **Business Verification** is part of reaching it; apps must be **Live** to receive any webhook notifications ([Webhooks](https://developers.facebook.com/docs/instagram-platform/webhooks)).
- Standard Access restricts data access to people with a role on the app ([Private Replies](https://developers.facebook.com/docs/messenger-platform/instagram/features/private-replies)) — sufficient for development, not for production tenants.
- Implication for planning: production launch requires completed App Review with recorded screencasts of the exact flows; this is an external dependency that M11 rehearsal must include.

## 5. Explicit non-goals for MVP (verified unavailable or out of scope)

- Sending promotional DMs outside messaging windows (no sanctioned mechanism — see C6).
- Automations on accounts that are private (comments/@mentions webhooks not delivered).
- Any scraping/bypass of undocumented endpoints — prohibited by repo architecture constraints regardless of feasibility.

## 6. Open questions to retire before dependent milestones

| Question | Blocking | Resolution vehicle |
|---|---|---|
| Concrete messaging rate-limit arithmetic for our call patterns | M04–M06 send-path design | Measure against official rate-limiting docs during M04 implementation; do not hardcode assumptions |
| Exact `messages` payload schemas for persistence design | M04-003 normalization | Captured as fixtures in the M01-003 spike and extended in M04 |
| Whether Human Agent tag use fits operator UI timing rules | M08 inbox UX | Product decision recorded in ADR-006 follow-ups |

## 7. Sources (all fetched August 2026)

- Instagram Platform — Webhooks (verification requests, event notifications, requirements table): <https://developers.facebook.com/docs/instagram-platform/webhooks>
- Messenger Platform — Webhooks (`X-Hub-Signature-256` validation): <https://developers.facebook.com/docs/messenger-platform/webhooks>
- Messenger Platform — Instagram Private Replies: <https://developers.facebook.com/docs/messenger-platform/instagram/features/private-replies>
- Messenger Platform — Policy Overview (24-hour window, Human Agent tag, unavailable features): <https://developers.facebook.com/docs/messenger-platform/policy/policy-overview>
- Instagram Platform — Business Login for Instagram (flow, scopes, token lifetimes): <https://developers.facebook.com/docs/instagram-platform/instagram-api-with-instagram-login/business-login>

*Next:* M01-002 turns §2/C7 into the full OAuth & token lifecycle contract; M01-003 spikes the webhook verification contract described above; M01-004 records the decisions as ADRs.
