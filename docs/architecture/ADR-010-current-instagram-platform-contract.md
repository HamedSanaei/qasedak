# ADR-010 — Current Instagram platform contract (Instagram Login primary)

- Status: Accepted (M13-001, 2026-09-04)
- Supersedes: ADR-006-meta-integration-paths.md (messaging-path decision only;
  ADR-006 is preserved as historical evidence)
- Normative companion: `docs/product/meta-instagram-platform-contract.md`

## Context

ADR-006 (August 2026) decided two connection paths with a hard split: the
Page-free Instagram-Login path for basic connection/comments data, and
Facebook Login for Business + Messenger Platform for **all** messaging
(inbound DMs, Private Replies, 24-hour replies). The M13-001 fresh audit of
current official Meta documentation (page revisions March–August 2026,
retrieved 2026-09-04) shows that split no longer holds: Instagram Login now
directly supports the full messaging surface Qasedak needs. In parallel, the
audit corrected two implementation-level assumptions (window-error code,
read-receipt shape) and confirmed follow-status unavailability.

## Decision

1. **Instagram Login is Qasedak's primary integration path** for OAuth,
   messaging (Send API incl. quick replies and button templates), Conversations
   API, comment Private Replies, public comment replies, webhooks, media reads
   and insights — all on `graph.instagram.com` with Instagram User access
   tokens and the `instagram_business_*` permission family.
2. **Facebook Login is retained deliberately, not by default**: existing
   FB-path accounts and health lifecycle keep working; FB-only extras
   (Business Discovery, `total_*` aggregated insights, `story_insights`
   webhook, hashtag search, ads/shopping surfaces) stay available but out of
   M13 scope. New connections default to Instagram Login.
3. **Failure-t ledger corrections are contract input, not code** (M13-001
   changes no production behavior): the messaging-window signal is Graph code
   `10` + `error_subcode` `2534022` (no official `490` exists in current
   tables); read receipts are `read:{mid}` (message ID, never a watermark);
   postbacks are `postback:{mid,title,payload}`.
4. **Per-user follow status is officially unsupported** for Qasedak's intended
   gate: no endpoint or field exposes "does IGSID X follow this account".
   M13-011 therefore ships branch (2) — supported opening/postback/reveal flow
   with the gate truthfully unavailable — unless Meta ships such a field under
   a future task. No scraping, private APIs or substitutes, ever.
5. **Human Agent stays human/operator-only**: the `human_agent` tag (7-day,
   feature approval required) must never appear in automation paths.
6. Latest Graph version observed is `v26.0`; M13-003 configures one version for
   all Graph calls instead of today's unversioned hosts. This ADR does not
   freeze the version.

## Consequences

- M13-002 through M13-015 proceed on the Instagram-Login-first contract; their
  planning texts needed only verdict notes (recorded in M13-001), not rescoping.
- M13-003 must rebuild the error taxonomy around code/subcode pairs (incl.
  `fbtrace_id`) and drop the `490` assumption; M13-008 must implement
  `read:{mid}` and postback shapes exactly.
- The dual-path `ConnectionPath` aggregate is kept: it now records a
  deliberate, shrinking FB-path population rather than a messaging requirement.
- Production readiness still needs App Review + Business Verification +
  Advanced Access for third-party tenants, a Live app, and the account-side
  Connected-Tools toggle — external dependencies, not coding tasks.

## Verification

Every claim cites a current official Meta page in the companion contract
document (§7, retrieved 2026-09-04). No production code, schema, package,
test or secret changed in M13-001. Downstream implementation tasks re-verify
only the two carried-over assumptions named in the contract (§6: text limit,
rate-limit arithmetic).
