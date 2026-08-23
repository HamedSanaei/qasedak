# ADR-007 — Webhook authenticity: HMAC over raw bytes plus challenge handshake

- Status: Accepted
- Date: 2026-08-23
- Task: M01-004 (contract proven by M01-003 spike)

## Context

Meta signs webhook event notifications with a SHA256 HMAC keyed by the app secret, delivered in the `X-Hub-Signature-256: sha256=<lowercase hex>` header, and explicitly documents that the signature is computed over an escaped-unicode serialization of the payload — re-serializing or decoding the body changes the bytes and breaks validation. Subscription setup uses a separate GET handshake (`hub.mode=subscribe`, `hub.verify_token`, `hub.challenge`). The M01-003 spike implemented and tested this contract deterministically.

## Decision

1. Authenticity checks live behind two application ports in `Qasedak.Modules.Instagram.Application.Webhooks` (`IWebhookSignatureVerifier`, `IWebhookSubscriptionValidator`), implemented in Infrastructure (`HmacWebhookSignatureVerifier`, `MetaWebhookSubscriptionValidator`) and registered via `AddInstagramModule`. Endpoints stay thin composition-root concerns (wired in M04).
2. Signature verification uses **the exact raw request bytes** (never a parsed-and-re-serialized body), HMAC-SHA256 with the configured app secret, lowercase-hex comparison through `CryptographicOperations.FixedTimeEquals`, and strict header grammar (`sha256=` prefix, 64 lowercase hex chars). Malformed headers fail closed as `InvalidSignatureHeader`.
3. The subscription handshake succeeds only when `hub.mode == "subscribe"`, the configured verify token matches (constant-time), and `hub.challenge` is echoed verbatim; anything else fails closed.
4. Deterministic fixtures under `tests/Qasedak.Modules.Instagram.UnitTests/Fixtures/webhook/` are the canonical contract for M04 endpoint tests, including an escaped-unicode payload that would fail any re-serialization implementation.
5. Durable, idempotent ingestion (inbox/dedup) is deliberately out of scope here and remains M04-002; this ADR covers authenticity only.

## Consequences

- ASP.NET endpoint code in M04 must bind the request body as raw bytes (no model binding that re-encodes) before JSON parsing.
- Secret rotation changes both signature validation and handshake acceptance atomically via configuration; tests pin behavior, not secret values.
- Failure modes are observable enums on the port results so M04 can log/reject without string matching.

## Verification

`dotnet test Qasedak.Modules.Instagram.UnitTests` — 20/20 deterministic tests pass (valid signatures including escaped-unicode fixture, tampered body, wrong secret, malformed headers, full handshake matrix). Architecture check confirms layer placement; format check clean.
