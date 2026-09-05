# Qasedak — Meta OAuth & Token Lifecycle Contract

**Task:** M01-002 · **Status:** Verified against official Meta documentation (fetched August 2026);
re-verified unchanged against current pages on 2026-09-04 (M13-001: Business Login page
2026-03-13, Access Token reference 2026-03-09, Refresh reference 2025-07-17 — same
endpoints, scopes, 60-day lifetimes and refresh preconditions; latest observed Graph
version v26.0; Qasedak's unversioned hosts are M13-003 input, not a contract change.
**Companion documents:** [Instagram MVP Capability Matrix](instagram-mvp-capability-matrix.md) (M01-001), ADR-006/ADR-007 (M01-004).

## 1. Purpose

Define the binding contract for connecting an Instagram professional account to a Qasedak workspace: authorization flows, scope model, token lifetimes, refresh rules, protection requirements, and ownership boundaries inside Qasedak's modules. Everything here is grounded in Meta's current developer documentation; items we could not verify today are explicitly listed as open questions rather than asserted.

## 2. Login paths used by Qasedak

| Path | Used for | Tokens produced |
|---|---|---|
| **Business Login for Instagram** (Instagram API with Instagram Login) | Basic connection, comments surface, profile data — no Facebook Page required | Instagram User access tokens (`graph.instagram.com`) |
| **Facebook Login for Business** + Messenger Platform | All messaging capabilities (inbox replies, comment→DM private replies) — requires a Facebook Page linked to the professional account | Facebook User/Page access tokens (`graph.facebook.com`) |

Rationale: the capability matrix (C2/C3/C5) shows messaging routes through the Messenger Platform with Facebook-side tokens, while C7 shows the Instagram-only path works without a Page. Qasedak therefore treats the Instagram-only path as the fast connection flow and the Facebook path as the messaging-capable upgrade — a decision formalized in ADR-006.

> **M13-001 update (2026-09-04):** the messaging premise above is superseded.
> Current official documentation proves Instagram Login directly supports Send
> API messaging, Conversations, Private Replies and the full webhook field set
> (see `meta-instagram-platform-contract.md` and ADR-010). Instagram Login is
> now the primary path; the Facebook path is retained deliberately for
> FB-only extras and existing FB-path accounts, not as the messaging default.
> The flow/endpoint/lifetime rules in §3–§5 below were re-verified unchanged.

## 3. Authorization flow (Business Login for Instagram) — verified contract

From [Business Login for Instagram](https://developers.facebook.com/docs/instagram-platform/instagram-api-with-instagram-login/business-login):

1. **Authorize** — app launches the embed/authorization URL:
   `https://www.instagram.com/oauth/authorize?client_id=<INSTAGRAM_APP_ID>&redirect_uri=<REDIRECT_URI>&response_type=code&scope=<comma-separated scopes>`
   Scopes are the `instagram_business_*` family: `instagram_business_basic`, `instagram_business_content_publish`, `instagram_business_manage_messages`, `instagram_business_manage_comments` (the legacy `business_*` scope names were deprecated by Meta on January 27, 2025).
2. **Redirect** — Meta redirects the user to our redirect URI with an **authorization code**.
3. **Exchange code → short-lived token** — verified against the official Business Login page (August 2026 refresh, identity semantics confirmed September 2026): the code is exchanged via **`POST https://api.instagram.com/oauth/access_token`** with form fields `client_id`, `client_secret`, `grant_type=authorization_code`, `redirect_uri`, `code`. Success returns `{data:[{access_token, user_id, permissions}]}` — the object is wrapped in a top-level `data` array; `user_id` is the professional account IG_ID (not a separate app-scoped id — the app-scoped value is the distinct `id` field on `/me`). The code is valid for **1 hour**, single use, and must be exchanged with the *same* `redirect_uri` used in step 1. This yields:
   - a **short-lived Instagram User access token**,
   - the **professional account IG_ID**, which is also the value of webhook `entry.id` for this account (Get Started guide: `/me?fields=user_id` returns "the value of the `id` field received in webhook notifications"), so the stored identity routes webhooks without further mapping,
   - the list of **granted permissions**.
4. **Exchange short-lived → long-lived** — `GET https://graph.instagram.com/access_token` with `grant_type=ig_exchange_token`, `client_secret`, and the short-lived token returns `{access_token, token_type:"bearer", expires_in}` where `expires_in` is seconds until expiry (≈60 days).
5. **Refresh** — `GET https://graph.instagram.com/refresh_access_token` issues a fresh long-lived token valid for another 60 days, provided **all** of:
   - the existing long-lived token is **at least 24 hours old**,
   - it is still **valid** (not expired),
   - the user has granted **`instagram_business_basic`**.
   
   Tokens not refreshed within 60 days **expire permanently** and cannot be refreshed.

### 3.1 Facebook Login for Business path

For messaging-capable workspaces the same OAuth shape runs against Facebook with `instagram_basic` + messaging permissions (`pages_messaging`, `instagram_manage_comments`) and yields Facebook User/Page tokens; Private Replies require a **Page access token** from a user who can perform moderation on the linked Page ([Private Replies](https://developers.facebook.com/docs/messenger-platform/instagram/features/private-replies)).

**OQ-2 resolution (verified August 2026 against [Get Long-Lived Access Tokens](https://developers.facebook.com/docs/facebook-login/guides/access-tokens/get-long-lived)):**
- A Facebook **User** token becomes long-lived via `GET graph.facebook.com/{version}/oauth/access_token?grant_type=fb_exchange_token&client_id={app-id}&client_secret={app-secret}&fb_exchange_token={short-lived}` → ≈60 days; an expired token can never be exchanged, the user must redo login.
- A long-lived **Page** token is generated from a long-lived User token of a Page-role user via `GET {app-scoped-user-id}/accounts`; such Page tokens carry **no expiration date** and are only invalidated by events (password change, permission revocation, app deauthorization, role loss).
- **Consequence for Qasedak:** there is *no refresh endpoint* on the Facebook path. The Instagram module stores the never-expiring Page token and detects invalidation through API error responses at use time (health mapping in M03-004), rather than scheduling refreshes.

## 4. Ownership and storage inside Qasedak

- **Identity module** owns users, workspaces, memberships, roles. It never sees Meta tokens.
- **Instagram module** owns the *connected account* aggregate: workspace reference (stable identifier only — no cross-module project reference), provider identity (Instagram-scoped user ID and/or Page ID), granted scope snapshot, token material, health state.
- Token material lives only in the Instagram module's Infrastructure persistence, **encrypted at rest** before production use; encryption key handling follows the deployment secret policy (injected at runtime, never in repo/images).
- Tokens are **never returned to the frontend**; the API exposes only connection state (connected/not, health, scopes) per §6.
- Disconnect/revoke deletes token material and records an auditable sensitive action (audit trail lands in M10-003; the intent is recorded here as a contract).

## 5. Operational rules

| Rule | Contract |
|---|---|
| Refresh scheduling | Background job refreshes each long-lived token when age ≥ 24h and remaining validity ≤ 7 days; jittered to avoid thundering herds |
| Refresh idempotency | At-most-one in-flight refresh per connected account; result replaces stored token atomically |
| Expired token | Health → `Expired`; all dependent automations pause with observable reason; reconnect required |
| Revocation detection | Meta API errors on token use mark health `Revoked`/`Unhealthy` (exact error-code mapping implemented in M03-004 with fixtures) |
| Password change / permission removal | Surfaced as `Unhealthy` with actionable state; never silently retried |
| Logging | Token values, secrets, and authorization codes are redacted from all logs |

## 6. Connection-state surface (API contract sketch)

```
GET /api/v1/workspaces/{workspaceId}/instagram/connections
  -> [{ accountId, providerIdentity, scopes[], health, expiresAt?, connectedAt }]
POST /api/v1/workspaces/{workspaceId}/instagram/connections/{accountId}/disconnect
```

Health enum (initial): `Connected`, `ExpiringSoon`, `Expired`, `Revoked`, `Unhealthy`.

## 7. Open questions

- **OQ-1 (RESOLVED, M03-001):** The Business Login authorize endpoint **does support an optional `state` parameter** — "An optional value indicating a server-specific state. For example, you can use this to protect against CSRF issues. We will include this parameter and value when redirecting the user back to you." ([Business Login query-string table](https://developers.facebook.com/docs/instagram-platform/instagram-api-with-instagram-login/business-login), verified August 2026). Qasedak always sends a random state and validates the echo at its callback boundary.
- **OQ-2 (RESOLVED, M03-001):** see §3.1 — Facebook Page tokens from long-lived User tokens never expire on a schedule; invalidation is event-driven and detected via API errors. No refresh scheduling exists for the FB path.
- **OQ-3 (RESOLVED, M03-004):** Revocation/error-code taxonomy implemented as deterministic fixtures (`MetaErrorTaxonomyTests`): code 190 with "expired" → `Expired`; code 190 with invalidation subcodes 463/467 or "deauthorized" → `Revoked`; codes 10/200 permission errors → `PermissionLoss` (surfaced `Unhealthy`); rate limits (4/17/32), HTTP 429/5xx and unknown shapes → `Transient` (health deliberately untouched — degraded state must be caused by Meta, never by network noise).

## 8. Sources (fetched August 2026)

- Business Login for Instagram (flow, endpoints, scopes, refresh conditions): <https://developers.facebook.com/docs/instagram-platform/instagram-api-with-instagram-login/business-login>
- Instagram Platform Webhooks (requirements/tokens table): <https://developers.facebook.com/docs/instagram-platform/webhooks>
- Messenger Platform — Instagram Private Replies (Page token + permissions): <https://developers.facebook.com/docs/messenger-platform/instagram/features/private-replies>
