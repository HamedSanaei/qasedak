# Current handoff

## Where we are

Milestones M00 (engineering foundation) and M01 (Meta/Instagram feasibility & contracts) are complete. The repository builds/tests green, dependencies are locked, both images build, Graphify is healthy, and all Meta integration decisions are documented with citations and ADRs. No product feature code beyond the webhook verification spike exists yet.

## Completed — M01 summary

- **M01-001:** `docs/product/instagram-mvp-capability-matrix.md` — every capability row cites official Meta docs fetched during the task. Key facts: comment→DM only via Private Reply (one message per comment, 7-day window, Live during broadcast); messaging requires Messenger Platform + Facebook Login tokens; `comments`/`live_comments` webhooks need Advanced Access + Live app + public account.
- **M01-002:** `docs/product/meta-oauth-token-lifecycle.md` — OAuth flow (`www.instagram.com/oauth/authorize` → code → short-lived → 60-day long-lived), refresh preconditions (≥24h old, valid, `instagram_business_basic`), permanent expiry otherwise; ownership split Identity vs Instagram module; health enum; open questions OQ-1..3 routed to M03.
- **M01-003:** Webhook authenticity spike — `IWebhookSignatureVerifier`/`IWebhookSubscriptionValidator` ports in Instagram Application, HMAC-SHA256 + challenge implementations in Infrastructure; 20/20 deterministic tests in new `Qasedak.Modules.Instagram.UnitTests` with committed fixtures (incl. escaped-unicode raw-bytes case). No persistence/endpoints.
- **M01-004:** ADR-006 (dual integration paths, capability boundary) + ADR-007 (webhook authenticity contract); SRS §4 binds Meta-facing requirements to these artifacts.

## Next task — M02-001

1. `python scripts/agent_preflight.py --task M02-001`; refresh graph (`graphify . --update --no-viz --code-only`).
2. Run a bounded graphify query on Identity module structure; record evidence.
3. Model users/workspaces/memberships/roles in `Qasedak.Modules.Identity.Domain`: aggregates/value objects/invariants per SRS §3.1 (workspace isolation from day one; UUIDv7 IDs; UTC timestamps).
4. Domain unit tests (new or existing test project placement consistent with architecture rules).
5. Gates: build/format/test green; record evidence; update state files; finalize; then continue to M02-002.

Suggested commit for the milestone: `feat(identity): model workspace membership domain` (per-task messages are in `docs/project/TASKS.md`).
