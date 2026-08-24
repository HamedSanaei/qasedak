# ADR-008: Payment provider abstraction with Zarinpal (live) and Bank Melli (boundary)

- Status: Accepted — **provider selection superseded by ADR-009 (2026-08-24): Bank
  Melli/SADAD cancelled, Behpardakht Mellat selected instead. The architecture,
  exactly-once design and Zarinpal integration described here remain in force.**
- Date: 2026-08-24
- Deciders: Human selected the providers; agent implemented per official documentation availability
- Task: M09-002

## Context

The human selected two payment providers for Qasedak subscriptions:

1. **Zarinpal** — direct payment gateway, contract verified live from Zarinpal's CURRENT
   official documentation (`payment.zarinpal.com/pg/v4/payment/request.json`,
   `.../verify.json`, StartPay redirect, `Authority`/`Status` callback, verify codes
   `100` first-verification / `101` previously-verified, amounts in ریال, explicit
   `"IRR"|"IRT"` currency).
2. **Bank Melli (SADAD family)** — direct internet payment gateway. The OFFICIAL merchant
   technical contract is **not present anywhere in the repository** and was not supplied:
   no endpoint spec, no signing/encryption algorithm, no terminal/merchant credential
   format, no callback field contract. Deriving the protocol from GitHub/StackOverflow/
   Laravel plugins/blogs is forbidden by the task directive.

Qasedak's Billing module is provider-neutral by design (ADR-006): Domain knows nothing
about providers, and one live subscription per workspace already holds explicit,
timestamped lifecycle transitions.

## Decision

1. **Provider-neutral port in Application.** `IPaymentGateway` (create → authority +
   redirect URL; verify server-to-server) lives in
   `Qasedak.Modules.Billing.Application.Payments`. Domain/Application/controllers contain
   zero provider logic; all protocol detail stays in Infrastructure adapters.

2. **Canonical currency: IRR.** One internal representation everywhere: plans store
   `AmountIrr`, payment attempts copy it at creation, and clients never submit prices.
   The Zarinpal adapter sends `currency:"IRR"` explicitly (officially supported), so no
   silent conversion exists anywhere in the stack.

3. **Durable PaymentAttempt aggregate.** Every checkout creates a persisted attempt
   (`billing.payment_attempts`) carrying workspace, plan intent, provider id, canonical
   amount, status (`Pending→Verified|Failed`), provider authority (UNIQUE filtered index —
   anti-replay), provider reference, masked card PAN (audit only), failure code, and full
   timestamps. No card data beyond provider-masked values is ever stored.

4. **Exactly-once entitlement via DB concurrency, not locks.** The attempt row maps
   PostgreSQL `xmin` as an EF Core optimistic-concurrency token. Concurrent duplicate
   callbacks/retries/refreshes race on that token: exactly one writer commits the
   `Verified` transition together with the subscription period application in a single
   `SaveChanges`; the loser reloads, observes terminal state, and answers idempotently.
   Callback query parameters alone NEVER activate anything — activation happens only on
   the result of a successful server-to-server `VerifyAsync` (codes 100/101 both apply
   exactly once; 101 covers "our response to the first callback was lost").

5. **Flow.** checkout (member-guarded) → server creates attempt + gateway request →
   browser redirect to provider → provider returns to PUBLIC
   `/api/v1/payments/callback/{provider}?attempt=…&Authority=…&Status=…` → attempt resolved
   server-side by authority → S2S verification → atomic transition → browser lands on the
   frontend result page; the workspace-scoped status endpoint remains the source of truth.

6. **Zarinpal adapter: direct HttpClient, no community packages.** Typed options bound to
   `Billing:Payments:Zarinpal` (Enabled/MerchantId secret/BaseUrl/Currency),
   `IHttpClientFactory` typed client, 20s timeout, structured logging that never logs the
   merchant id, raw payloads, authorities or card values. Transport faults map to a typed
   unavailable signal (attempt stays Pending → retryable); contract rejections map to a
   typed rejection carrying the provider code.

7. **Bank Melli: boundary only, fail-closed.** `MelliOptions` defines the configuration
   contract (Enabled/TerminalId/MerchantId/CredentialKey/BaseUrl/CallbackBaseUrl) and
   `MelliPaymentGateway` refuses every operation until Enabled AND an official contract
   exists — flipping Enabled without the protocol still fails loudly rather than guessing
   wire formats. Shipped configuration keeps Melli disabled everywhere.

8. **Minimal API surface.** plan catalog, current subscription overview, checkout
   (creates attempt), payment status + history (workspace-scoped), public strongly
   validated callbacks. No secrets in URLs (only the public attempt id); workspace
   operations require membership; foreign workspaces are invisible (uniform 403).

9. **Secrets and audit.** Provider credentials come exclusively from environment
   configuration (documented in `.env.example` / deployment docs); nothing real is
   committed. Checkout/finalization transitions append to the audit trail without any
   credential, authority or card material.

## Alternatives considered

- **Community Zarinpal NuGet packages** — rejected: unverified maintenance/security, and
  the official REST surface is small enough that a typed internal adapter is clearer.
- **Storing Toman/IRT internally** — rejected: dual-unit ambiguity; IRR matches the
  provider verify unit and removes conversion entirely.
- **In-memory idempotency locks** — rejected: restarts/multi-instance break them;
  DB uniqueness + row-version concurrency is durable.
- **Guessing SADAD protocol from third-party code** — explicitly forbidden; would be
  fabrication with live-money consequences.

## Consequences

- Zarinpal can go live with environment credentials alone; CI uses deterministic
  fixtures/recording gateways and never calls live providers.
- Bank Melli remains selectable-but-inert until its official merchant document supplies:
  (a) endpoint specification, (b) signature/encryption algorithm spec, (c) credential
  contract, (d) callback field contract. The milestone claim stays honest: Zarinpal
  production-capable per official docs; Melli externally blocked pending those documents.
