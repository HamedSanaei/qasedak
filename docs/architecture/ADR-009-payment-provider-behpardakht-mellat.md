# ADR-009: v1 payment providers are Zarinpal + Behpardakht Mellat (Bank Melli/SADAD cancelled)

- Status: Accepted
- Date: 2026-08-24
- Supersedes: the provider-selection half of ADR-008 (ADR-008's architecture — provider-neutral
  `IPaymentGateway`, canonical IRR, `PaymentAttempt` exactly-once persistence — remains fully in force).

## Context

The human made the final provider decision for Qasedak v1:

1. **Zarinpal** — already implemented and operational (see ADR-008).
2. **Behpardakht Mellat** — newly selected; must be integrated.
3. **Bank Melli / SADAD** — CANCELLED and removed from active v1 scope.

## Decision

- The provider-neutral architecture is unchanged: Billing Domain/Application know only
  `IPaymentGateway` and typed application contracts. No Mellat-specific DTO, SOAP type or
  WSDL artifact may leak outside Infrastructure.
- `MelliPaymentGateway`/`MelliOptions` are deleted from the active scope and replaced by
  `BehpardakhtMellatPaymentGateway` (`providerId = "mellat"`) with typed server-side
  `BehpardakhtOptions` (terminal id / username / password / base url / callback base url).
- **Live Mellat transport is externally blocked**: the project does not currently have the
  CURRENT official Behpardakht merchant technical contract. Available sources are mirrored
  historical PGW manuals (v1.0/1.1) and community packages describing the legacy flow
  `bpPayRequest → redirect → callback → bpVerifyRequest → bpSettleRequest` (plus inquiry/
  reversal). These are treated strictly as architectural background: no endpoint, WSDL/SOAP
  detail, response code or field semantic is copied into production transport until the
  verified document arrives. Until then every gateway operation fails CLOSED:
  - disabled (`Enabled=false`, default everywhere) → `PaymentProviderDisabledException`;
  - enabled without a verified contract implementation → `PaymentGatewayUnavailableException`
    naming exactly which documents are required.
- Configuration contract ships now (`.env.example`, docker-compose, appsettings,
  deployment guide §6) using historically documented field concepts explicitly marked as
  requiring re-verification against the official document before enabling.
- Frontend provider selection shows زرین‌پال and به‌پرداخت ملت per the approved Qasedak
  billing design; the Penpot Checkout board labels were updated through MCP accordingly.
- Currency stays canonical IRR end-to-end. Behpardakht historically operates IRR; any
  provider-specific monetary transformation may exist ONLY inside its adapter and only if
  the verified contract requires it. No تومان↔ریال conversion exists anywhere.

## Exactly-once guarantees (unchanged, provider-independent)

Server creates the PaymentAttempt and owns the amount; unique Authority/order identity;
callbacks never prove payment; verification is server-to-server; settle happens only
according to the verified provider contract; duplicate callbacks are idempotent (terminal
attempt reload); duplicate/concurrent verification extends entitlement exactly once via DB
uniqueness + xmin row-version concurrency; failed verification never activates anything;
reversal/inquiry will be modeled when the verified contract defines them.

## Alternatives considered

- Keeping Bank Melli/SADAD — cancelled by human decision.
- Implementing Mellat transport from mirrored historical manuals/community packages —
  rejected: fabrication hazard; the directive forbids copying unverified protocol details.

## Consequences

- Zarinpal remains fully operational; nothing about it changes except shared-abstraction
  naming.
- M09-002 remains DONE-PARTIAL with one precise external blocker: obtain the CURRENT
  official Behpardakht merchant technical documentation (service endpoints/WSDL, operation
  contracts for payment/verify/settle incl. response-code table, callback parameter schema,
  reversal/inquiry semantics if applicable). Once supplied, the adapter implements real
  transport behind the existing port and M09-002 can become fully DONE.
