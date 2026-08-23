# Current handoff

## Where we are

Milestones M00 (foundation), M01 (Meta feasibility & contracts), M02 (identity & workspace core), M03 (Instagram account connection) and M04 (webhook ingestion & event normalization) are complete. The repository builds/tests green (175 backend tests), dependencies are locked, both images build, Graphify is healthy in code-only mode. All open questions OQ-1..3 are resolved with citations in `docs/product/meta-oauth-token-lifecycle.md`.

## Completed — M04 summary

- **M04-001:** Webhook verification endpoints at `/api/v1/webhooks/instagram` — GET handshake via `IWebhookSubscriptionValidator` (challenge verbatim), POST enforcing `X-Hub-Signature-256` over exact raw bytes; bad signature → empty 401, oversized → 413, signed non-JSON → 400. Application boundary `IMetaWebhookIngester` keeps transport out of Application.
- **M04-002:** Durable idempotent inbox — `webhook_inbox` table keyed by SHA-256 of the raw body (Meta redeliveries are byte-identical); `InboxWebhookIngester` insert-and-accept / redelivery-no-op with attempt counter; concurrent same-identity races caught and still accepted; migration `AddWebhookInbox`.
- **M04-003:** Normalization — `MetaPayloadNormalizer` turns canonical bodies into explicit events (`InstagramMessageReceived` echo-skipped, `InstagramCommentCreated`, `InstagramMentionCreated`); unknown fields → `UnrecognizedWebhookFragment`, malformed JSON never throws; `ProcessPendingWebhookEventsUseCase` normalizes→dispatches→closes entries through `IWebhookInboxStore` + `IIntegrationEventDispatcher` ports.
- **M04-004:** Instrumentation — meter `Qasedak.Instagram.Webhooks` (notifications by outcome, events by kind, duplicates, ingestion duration histogram), correlation-id echo/mint (`X-Correlation-Id`), LoggerMessage structured rejection logs, redelivery-threshold warning, pending-backlog gauge.

## Next task — M05-001

1. `python scripts/agent_preflight.py --task M05-001`; refresh graph (`graphify . --update --no-viz --code-only`).
2. Bounded graphify query on the Conversations module scaffolding; record evidence.
3. Model conversation/message domain per TASKS.md invariants (identity ownership, state transitions); keep Domain transport-free — webhook-normalized events feed conversations later.
4. Gates: build/format/test green; evidence; state files; finalize; continue M05.

Suggested commits: per-task messages live in `docs/project/TASKS.md` (M04 used `feat(instagram): verify meta webhook requests`, `feat(instagram): add idempotent webhook inbox`, `feat(instagram): normalize webhook integration events`, `feat(observability): instrument webhook processing`).
