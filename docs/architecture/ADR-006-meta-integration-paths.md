# ADR-006 — Meta integration paths and MVP capability boundary

- Status: Accepted
- Date: 2026-08-23
- Task: M01-004 (decisions from M01-001 research)

## Context

Meta currently exposes three Instagram integration components with different token types, base URLs, permissions, and review requirements: Business Login for Instagram (`graph.instagram.com`, Instagram User tokens), Facebook Login for Business (`graph.facebook.com`), and Instagram Messaging via Messenger Platform (Facebook-side tokens). The verified capability matrix (`docs/product/instagram-mvp-capability-matrix.md`) shows that every messaging capability Qasedak's MVP needs — inbound DM webhooks, comment-triggered Private Replies, replies inside the 24-hour window — routes through the Messenger Platform with Facebook Login tokens, while the Page-free Instagram-only path covers basic connection and comments data. Comment/`live_comments` webhooks additionally require Advanced Access (App Review + Business Verification) and a Live app.

## Decision

1. Qasedak supports **two connection paths**: the fast "Business Login for Instagram" flow (no Facebook Page required) and the messaging-capable "Facebook Login for Business + Messenger Platform" upgrade. The Instagram module models both as one connected-account aggregate with a path discriminator.
2. The MVP comment→DM automation is implemented **only** as a documented Private Reply: exactly one automated message per comment ID, within 7 days of comment creation (Live comments only during broadcast), with all subsequent conversation governed by the 24-hour window owned by the Conversations module. Idempotency key for the effect is the Meta comment ID.
3. Operator-initiated replies outside the 24-hour window use the Human Agent tag and are treated as manual human actions, not automation effects.
4. Proactive campaigns after window close are out of scope until Meta offers a sanctioned mechanism; no workaround designs are permitted.
5. Production readiness includes App Review with Advanced Access for `comments`/`live_comments`, Business Verification, and a Live app — tracked as an external dependency of M11 rehearsal, not a coding task.

## Consequences

- Workspaces without a linked Facebook Page get limited functionality; the UI/state surface must make this distinction explicit from M03 onward.
- Duplicate webhook notifications for boosted/ads posts are expected behavior; deduplication by notification identity is mandatory in the M04 inbox design.
- Rate-limit arithmetic remains an implementation-time verification item in M04–M06 (no invented numbers).

## Verification

Capability claims cite official Meta documentation fetched August 2026 in `docs/product/instagram-mvp-capability-matrix.md`; contract tests for webhook authenticity exist since M01-003; private-reply/window semantics become executable tests at M05/M06.
