# M13-015 — Meta production go-live and smoke runbook

**Contract date:** 2026-09-08
**Scope:** M13 Instagram production verification only. This runbook never authorizes use of a customer Instagram account for testing.

## 1. Independent go-live gates

These gates are independent. A green code/deployment gate MUST NOT be treated as proof of an external Meta gate.

| Gate | Evidence required | Default status before evidence |
|---|---|---|
| Code green | exact-SHA repository verification + CI | pending until exact M13-015 SHA passes |
| Production deployment green | immutable image, backup, migration result, health + safe smoke | pending until deploy workflow succeeds |
| Meta App Review | operator-provided Meta approval evidence | **Unknown** |
| Advanced Access | operator-provided permission/access evidence | **Unknown** |
| Business Verification | operator-provided Meta business-verification evidence | **Unknown** |
| Meta App Live mode | operator verifies app is Live | **Production-only** |
| Production webhook subscriptions | designated test account + server-owned field verification | **Production-only** |
| Designated production TEST account | explicit operator designation | **Unavailable unless supplied** |

A release may be code/deployment green while external Meta gates remain blocked. Record that truthfully rather than changing external status from CI output.

## 2. Deployment prerequisites

- Exact source SHA is the approved `M13_015_SHA`; production tag is `sha-<first12 M13_015_SHA>`.
- `/opt/qasedak/.env.production` exists, mode 600, and contains only production values supplied outside Git.
- PostgreSQL is healthy; database backup succeeds before migration.
- `deploy/remote-deploy.sh` verifies immutable tag format, backup-before-migrate, readiness, Web/API health and rollback.
- Expected M13-015 default is **no new migration** unless the final committed tree proves otherwise.
- Never place a real Meta/Instagram token in CI or this runbook.
## 3. Safe default production smoke — always run

This level performs **no Meta mutation** and does not require a production Instagram account.

| Check | Expected result |
|---|---|
| `GET /` | 200 from public Web |
| `GET /api/v1/system` | 200 and expected Qasedak system payload |
| anonymous protected route | normal auth redirect/401 contract; no data leakage |
| webhook GET with wrong verify token | rejection (403) |
| unsigned webhook POST | rejection (401), no business effects |
| API `/health/live` | healthy |
| API `/health/ready` | healthy, including PostgreSQL dependency |
| PostgreSQL `pg_isready` | healthy |
| API/Web containers | running exact immutable `sha-*` images |
| scheduled worker | running in API host; no tight retry/startup storm |
| safe authorized local reads | connection/capability/follower-history reads only; token-free response |

Reference host-local probes from the deployment contract:

```bash
curl -fsS http://127.0.0.1:${API_PORT:-8080}/health/live
curl -fsS http://127.0.0.1:${API_PORT:-8080}/health/ready
curl -fsS http://127.0.0.1:${API_PORT:-8080}/api/v1/system
curl -fsS http://127.0.0.1:${WEB_PORT:-3000}/
```

Also inspect `docker compose ps` and recent API logs for provider startup storms. Do not print environment values, authorization headers, webhook app secret, protection key, or token ciphertext.
## 4. Safe webhook-negative smoke

Use synthetic values only. The goal is to prove fail-closed routing without ingesting a valid Meta event.

1. Send verification GET with `hub.mode=subscribe`, a deliberately wrong `hub.verify_token`, and a harmless challenge. Expect 403; never reveal the configured verify token.
2. Send a small unsigned JSON POST to `/api/v1/webhooks/instagram`. Expect 401 with no provider/business effect.
3. Do **not** send a correctly signed fake production event unless an operator intentionally enters the designated test-account procedure below.
4. Confirm logs contain only bounded status/correlation data, never submitted body text or secret material.

## 5. Designated Meta TEST-account smoke — explicit operator opt-in only

Run this level only when the operator explicitly supplies a production **test** Instagram professional account that may safely receive/send test effects. Never substitute a customer account, personal customer content, or a randomly selected connected account.

Record before starting:

- test-account provider identity and internal ConnectedAccountId;
- approving operator and UTC start time;
- confirmed App Live / App Review / Advanced Access evidence actually available;
- test media/comment/thread IDs created specifically for this smoke;
- cleanup owner.

Optional verification sequence, stopping at the first unexpected outcome:

1. OAuth/connect the designated test account; verify state replay is refused and returned identity matches the intended account.
2. Verify subscription health for server-owned fields: `comments`, `live_comments`, `messages`, `messaging_postbacks`, `messaging_seen`.
3. Read profile identity, media catalog and account/media insights; confirm exact-account responses and permission-scoped degradation.
4. Create a fresh disposable test comment and verify signed comment webhook ingestion.
5. Execute one Private Reply to that disposable comment, only inside the verified eligibility window; verify one semantic send.
6. Have the designated test user send a real response so the normal user-message window is proven rather than inferred from the comment.
7. Exercise one postback/reveal continuation and redeliver the same postback once; verify reveal remains single-effect.
8. Send one inbound DM matching a test automation and verify one Direct reply; redeliver the webhook and verify no duplicate send.
9. Exercise Public Reply only when the disposable test comment/media make a public mutation deliberately safe.
10. Trigger history sync and verify imported history does not execute automations or inflate unread state.
11. If FollowGate remains `Unsupported`, do not turn it on or claim success merely because the relationship adapter can be called.

## 6. Cleanup after designated TEST-account smoke

- Disable/delete temporary test automations created only for smoke.
- Remove disposable public test replies/comments/media when the Instagram UI/API safely permits operator cleanup.
- Disconnect the designated test account if it is not intended to remain a production fixture; otherwise document why it remains.
- Confirm no test scheduled follow-up is still pending.
- Confirm no unresolved `Attempting`/`Uncertain` provider mutation was blindly retried.
- Record the exact test account, test artifact IDs, outcomes and UTC finish time in deployment evidence. Do not record access tokens, app secret, webhook verify token, arbitrary DM/comment text, or raw Graph bodies.

## 7. Production evidence to capture

Record exact `M13_015_SHA`, CI/CodeQL/Publish/Deploy run IDs, immutable image tag/digests if available, database backup artifact name, migration/no-migration result, PostgreSQL/API/Web/worker health, webhook-negative safe smoke results, and CI live-Meta-deny proof.

If no designated production test Instagram account was explicitly supplied, record exactly:

`live Meta M13-015 end-to-end smoke NOT RUN — no designated production test Instagram account`

That result is truthful and acceptable; it must not be rewritten as PASS or FAIL.
## 8. Failure, rollback and stop rules

- Migration failure: do not switch API/Web; preserve backup and deployment logs.
- Readiness/Web/safe-smoke failure after switch: use the repository deployment rollback to the previous successful immutable image.
- Provider startup storm, unexpected outbound Meta traffic, token/secret leakage, cross-account behavior, or duplicate provider mutation: stop designated live smoke immediately and investigate before further effects.
- External Meta denial (App Review/Advanced Access/Business Verification/Live mode): classify as an external blocker; do not work around it with scraping/private APIs or customer credentials.
- Rate limit/transient Meta failure: preserve retry/backoff semantics; do not manually spam retries.
- Ambiguous provider mutation after `Attempting`: surface `Uncertain`; never force an automatic resend simply to make smoke green.

## 9. Final go-live interpretation

A production deployment is considered technically healthy when the exact immutable SHA is running, backup/migration policy succeeded, PostgreSQL/API/Web/worker health is green, and the safe default smoke passes. External-customer Meta capability is separately gated by real Meta approval/access evidence.

The designated TEST-account sequence is production-only evidence. Its absence does not invalidate deterministic CI/real-PostgreSQL coverage, but it must remain explicitly NOT RUN until an operator provides the account.

Canonical supporting documents:

- `docs/product/m13-015-meta-compliance-matrix.md`
- `docs/ops/M13-015_META_APP_REVIEW_CHECKLIST.md`
- `docs/product/meta-instagram-platform-contract.md`
- `docs/ops/PRODUCTION_ENVIRONMENT.md`
- `deploy/remote-deploy.sh`
