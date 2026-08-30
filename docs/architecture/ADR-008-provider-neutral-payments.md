# ADR-008 — Provider-neutral payments with Zarinpal and Bank Melli

- Status: Accepted
- Date: 2026-08-24

## Context

Qasedak v1 needs one-off gateway payments for subscription purchase and renewal. The
human has selected two providers: **Zarinpal** and **Bank Melli direct Internet Payment
Gateway**, expected to use the SADAD integration family where that is confirmed by the
project's current official merchant contract. Provider selection is no longer a product
or architecture blocker.

The repository currently contains only an empty Billing module composition seam. It does
not contain Bank Melli/SADAD merchant documentation, credentials or a provider contract.
A bounded search of official Bank Melli and SADAD PSP domains during M08-000 did not
locate a current public technical merchant specification. Old community libraries and
protocol fields are therefore not acceptable implementation sources.

Zarinpal's official organization currently publishes gateway SDKs whose public surface
includes payment creation, redirect URL creation and server-side verification. The human
decision also records successful/already-verified result codes 100/101. The implementation
agent must verify the current official gateway documentation and merchant contract again
before coding rather than treating this ADR as an endpoint/field specification.

## Decision

The Billing module owns payment orchestration. Application code depends on a
provider-neutral port conceptually named `IPaymentGateway`. Provider adapters live in
Billing Infrastructure, for example:

- `ZarinpalPaymentGateway`
- `MelliPaymentGateway`

Application and Domain must not reference provider SDK types, request/response DTOs,
endpoint names or credential models. Provider adapters translate their current official
contracts into Billing-owned request/result types.

The provider-neutral model includes at least:

- `PaymentAttempt` identity and workspace/subscription purpose;
- provider;
- unique merchant order ID;
- server-owned amount and currency;
- provider authority/token where applicable;
- provider reference ID where applicable;
- state;
- callback-received metadata;
- verification attempt/result metadata;
- verified timestamp;
- failure category safe for support/UI display.

Provider credentials are configuration/secret-store concerns. They are never Domain
entity fields, frontend data, design data or ordinary log values.

## Required payment invariants

1. The server creates and persists the payable amount. Browser, redirect and callback
   amounts are never trusted.
2. A callback is a signal to continue processing, not proof of payment.
3. Entitlement activation occurs only after server-to-provider verification succeeds.
4. Verification is idempotent. A duplicate callback or already-verified provider response
   returns the existing outcome and cannot grant entitlement twice.
5. Concurrent verification must not extend the same subscription twice. Persistence uses
   an atomic state transition and uniqueness/concurrency control around the attempt and
   entitlement grant.
6. Every callback maps to an existing server-created `PaymentAttempt`; unknown attempts
   fail closed.
7. Merchant order IDs are unique. Provider reference IDs are unique at the provider scope
   when the official contract supplies them.
8. Secrets and sensitive payment data remain server-side and are masked in logs. Full
   tokens, credentials and card data are never logged.
9. Provider failures are normalized to stable Billing failure categories without leaking
   sensitive provider payloads.
10. Qasedak v1 renewals are one-off gateway payments. No automatic recurring card charge
    is promised or modeled without a later accepted provider contract and ADR.

## Provider-specific constraints

### Zarinpal

The expected flow is request → redirect → callback → server-side verify. Codes 100 and
101 are treated as successful/already-verified only after the implementation agent
confirms their current meaning and the current amount/currency contract from official
Zarinpal merchant documentation. An already-verified result must return the existing
verified attempt and entitlement outcome.

Official implementation starting points:

- <https://github.com/ZarinPal/ZarinPal-node-SDK>
- <https://github.com/ZarinPal/Zarinpal-Python-Sdk>

These links establish current official SDK ownership and capabilities; the .NET adapter
must still follow the current provider HTTP/merchant contract instead of porting SDK DTOs
into Application or Domain.

### Bank Melli / SADAD

User-facing naming is «پرداخت مستقیم بانک ملی». The exact SADAD protocol, endpoints,
signature/encryption rules, callback fields, amount unit, verification behavior and
merchant credentials must come from the current official Bank Melli/SADAD merchant
documentation supplied to this project. None are invented in this ADR.

Lack of that documentation is an implementation dependency for `MelliPaymentGateway`
only. It does not block the provider-neutral Billing model or the Zarinpal adapter.

## Consequences

- M09-002 is executable: Zarinpal can proceed after current official-doc verification;
  Melli work proceeds once the official merchant package/credentials are available.
- Billing persistence must be designed around `PaymentAttempt` idempotency before HTTP
  callbacks are exposed.
- UI shows neutral provider names and server-owned totals, then a pending verification
  state until server verification completes.
- Adding another provider is an Infrastructure adapter plus configuration and contract
  tests, not a Domain/Application rewrite.
