# Current handoff

## Where we are

Milestones M00 (foundation), M01 (Meta feasibility & contracts), M02 (identity & workspace core) and M03 (Instagram account connection) are complete. The repository builds/tests green (159 backend tests), dependencies are locked, both images build, Graphify is healthy in code-only mode. All three open questions from M01-002 (OQ-1..3) are resolved with citations in `docs/product/meta-oauth-token-lifecycle.md`.

## Completed — M03 summary

- **M03-001:** Meta OAuth adapter — `IAuthorizationUrlBuilder`/`IMetaOAuthClient` ports in Instagram Application; Infrastructure `InstagramAuthorizationUrlBuilder` (documented authorize URL incl. anti-CSRF state — OQ-1) and `GraphInstagramOAuthClient` implementing the verified token contract: POST `api.instagram.com/oauth/access_token` (data-array payload), GET `graph.instagram.com/access_token|refresh_access_token`; structured failures never throw and redact secrets. OQ-2 resolved: FB Page tokens never expire on schedule; no refresh scheduling on the FB path.
- **M03-002:** Account lifecycle — Domain aggregate `ConnectedAccount` (path discriminator per ADR-006, scope snapshot, health enum, expiry metadata only; raw tokens never enter the domain), guarded transitions, terminal disconnect; Application use cases connect/disconnect/list with ports `IConnectedAccountRepository` + `IProtectedTokenStore`.
- **M03-003:** Persistence — `InstagramDbContext` owns the `instagram` schema (`connected_accounts` with partial unique `(WorkspaceId, ProviderUserId)` where not disconnected → reconnection allowed; `account_tokens` ciphertext-only rows); committed migration; AES-GCM 256-bit `AesGcmTokenProtector` (runtime-injected key, validated lazily); rotation replaces ciphertext atomically; 5 Testcontainers PostgreSQL 18 integration tests.
- **M03-004:** Token health — `IMetaTokenInspector` port + adapter probing graph.instagram.com/me; OQ-3 taxonomy as deterministic fixtures (190-expired→Expired; 190-invalidation→Revoked; 10/200→PermissionLoss→Unhealthy; rate limits/5xx/unknown→Transient leaves health untouched); `EvaluateAccountHealthUseCase` persists states (local expiry short-circuit, missing-token fault, ExpiringSoon ≤7-day window).

## Next task — M04-001

1. `python scripts/agent_preflight.py --task M04-001`; refresh graph (`graphify . --update --no-viz --code-only`).
2. Bounded graphify query on the existing webhook verification spike (`Qasedak.Modules.Instagram.*.Webhooks`, M01-003); record evidence.
3. Build the webhook receive endpoint on top of the verified spike: GET subscription handshake (hub.mode/hub.challenge/hub.verify_token via `IWebhookSubscriptionValidator`) and POST event ingestion with `IWebhookSignatureVerifier` over raw bytes (escaped-unicode serialization caveat).
4. Negative paths first-class: bad signature → reject without body echo; unknown topic handling; deterministic fixtures only — CI must never call live Meta APIs.
5. Gates: build/format/test green; record evidence; update state files; finalize; continue M04.

Suggested commit for the milestone: per-task messages live in `docs/project/TASKS.md` (`feat(instagram): add meta oauth infrastructure adapter` … `feat(instagram): manage token health and revocation` for M03).
