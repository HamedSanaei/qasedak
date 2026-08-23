# Current handoff

## Where we are

Milestones M00 (foundation), M01 (Meta feasibility & contracts), M02 (identity & workspace core), M03 (Instagram account connection), M04 (webhook ingestion & event normalization) and M05 (conversations inbox) are complete. The repository builds/tests green (**200 backend tests**), `scripts/verify.py --full` passed for the M05 milestone, Graphify is healthy in code-only mode.

## Completed — M05 summary

- **M05-001:** Conversations domain — `Conversation` aggregate (unique participant thread per workspace+channel, unread accounting, archive/reopen, read-once, per-thread unique provider message ids, 1000-char bodies, FromState rehydration) + `Message` entity; `IConversationRepository`; EF Core persistence under the `conversations` schema (`InitialConversationsCreation`); module unit tests.
- **M05-002:** Inbound projection — normalization carries Meta's `mid`; `ProjectInboundMessageUseCase` finds-or-creates threads with aggregate-level idempotency (duplicates → `DuplicateDelivery`, oversized inbound stored truncated); composition-root `InstagramConversationBridge` resolves workspace via `FindWorkspaceIdByProviderIdentityAsync` and drops unbound identities; webhook POST now runs a post-ingest seam (`IWebhookPostIngestProcessor`, no-op default, Api fills with pending normalize+dispatch adapter; processing failure → durable retry + HTTP 202). End-to-end signed-webhook→conversation persistence + redelivery idempotency over real PostgreSQL. Domain lesson encoded: provider send-times may precede thread creation — never guard that.
- **M05-003:** Inbox queries — `IConversationQueries`/`EfConversationQueries` (no-tracking paging/filtering/status filter, last-message preview, detail with ordered messages) behind `/api/v1/workspaces/{id}/conversations[/{id}]`, JWT-authorized; foreign threads are 404. Minimal-API lesson: non-nullable query params are *required* and throw when absent — use nullable signatures with defaults.
- **M05-004:** Outbound replies — channel-neutral `IConversationChannelGateway` port; `SendReplyUseCase` enforces open-thread + 24-hour messaging-window compliance before any network call and appends only accepted sends; Instagram `GraphInstagramMessagingClient` adapter posts `{graph}/me/messages` with Bearer page token, structured taxonomy mapping Graph code 490 → `MessagingWindowExpired`; composition-root `InstagramReplyGateway` binds account lookup + protected token decrypt; POST `.../replies` endpoint maps stable failure codes to 404/400/409/502. Unit tests cover window boundary, no-append-on-rejection, redacted error details.

## Next task — M06-001

1. `python scripts/agent_preflight.py --task M06-001`; refresh graph (`graphify . --update --no-viz --code-only`).
2. Bounded graphify query on automation scaffolding + how Instagram/Conversations modules expose seams; record evidence.
3. Model the automation aggregate per TASKS.md invariants; keep Domain transport-free and clock-free (timestamps as parameters).
4. Gates: build/format/test green; evidence; state files; finalize; continue M06.

Suggested commits for M05: `feat(conversations): model conversation and message state`, `feat(conversations): project inbound instagram messages`, `feat(conversations): expose workspace inbox queries`, `feat(conversations): send replies through instagram`.
