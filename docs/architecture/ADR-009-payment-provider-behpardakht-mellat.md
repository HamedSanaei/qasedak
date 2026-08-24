# ADR-009: v1 payment providers are Zarinpal + Behpardakht Mellat (Bank Melli/SADAD cancelled)

- Status: Accepted (transport completed M09-002)
- Date: 2026-08-24 (transport completion update same day)
- Supersedes: the provider-selection half of ADR-008 (ADR-008's architecture — provider-neutral
  `IPaymentGateway`, canonical IRR, `PaymentAttempt` exactly-once persistence — remains fully in force).
- Vendor technical reference (implemented contract): **`docs/vendor/behpardakht/BEHPARDAKHT-IPG-v1.29-EN.md`**
  — English translation of the Behpardakht IPG User Guide **v1.29** (Tir 1402 / 2023), supplied by the
  human and labeled **"Unofficial - External"**. This provenance is preserved verbatim: it is not a
  first-party official English document. If newer merchant onboarding material conflicts with it, the
  conflict is resolved through a future vendor-reference ADR — never through silent code changes.

## Context

The human made the final provider decision for Qasedak v1:

1. **Zarinpal** — already implemented and operational (see ADR-008).
2. **Behpardakht Mellat** — newly selected; integrated against the vendor reference above.
3. **Bank Melli / SADAD** — CANCELLED and removed from active v1 scope.

An earlier revision of this ADR recorded the live Mellat transport as externally blocked pending an
authoritative protocol document. The vendor reference has since been supplied, reviewed in full, and
used as the sole source for every wire-level detail of the implemented transport.

## Decision

- The provider-neutral architecture is unchanged: Billing Domain/Application know only
  `IPaymentGateway` and typed application contracts. No Mellat-specific DTO, SOAP type or WSDL artifact
  leaks outside Infrastructure (`IBehpardakhtSoapClient` and its wire records are Infrastructure-
  internal; visible to test assemblies only via `InternalsVisibleTo`).
- `MelliPaymentGateway`/`MelliOptions` are deleted from the active scope and replaced by
  `BehpardakhtMellatPaymentGateway` (`providerId = "mellat"`) with typed server-side
  `BehpardakhtOptions` (`Enabled`, `TerminalId`, `Username`, `Password`,
  `ServiceUrl` default `https://bpm.shaparak.ir/pgwchannel/services/pgw?wsdl`,
  `PaymentPageUrl` default `https://bpm.shaparak.ir/pgwchannel/startpay.mellat`,
  config-overridable `ServiceNamespace`, `CallbackBaseUrl`). Secrets arrive via environment only.
- Implemented transport, per vendor reference section:
  - `bpPayRequest` (§8) with the ten documented string parameters; response parsed defensively as
    `"ResCode,RefId"`; `ResCode=0` requires a non-empty RefId which is persisted exact-case;
    any non-zero code is a typed rejection, never success. Numeric `orderId` is derived
    deterministically from the attempt id and persisted on `billing.payment_attempts.ProviderOrderId`
    (migration `AddPaymentProviderOrderId`).
  - Redirect via our own jump endpoint `/api/v1/payments/mellat/startpay` rendering an auto-submitting
    form POSTing only the RefId to the configured payment page (§8.2); credentials never reach the
    browser; the endpoint lives on the registered merchant domain so the Referer requirement (§62)
    holds — deployment prerequisite documented in docs/08 §6.
  - Callback (§9): HTTP POST form (`RefId`, `ResCode`, `SaleOrderId`, `SaleReferenceId`,
    `CardHolderPan`) normalized to OK/CANCEL/FAILED hints. Mandatory identity rule enforced BEFORE any
    verification: callback `SaleOrderId` must exactly match the stored `ProviderOrderId` and the
    callback RefId resolves the stored attempt; mismatch → attempt marked `payment.callbackRejected`,
    no verify call, no activation, audited. Callback amount/status never prove payment.
  - Verify → settle chain (§10–§11): `bpVerifyRequest` then `bpSettleRequest`; codes `0` success,
    `43` already verified / `45` already settled treated idempotently; definitive failures (incl. `48`
    reversed, `17` user cancel, merchant configuration errors `21/23/24/62/421`) map to failed states
    without entitlement; unknown outcomes (timeout/fault/undocumented code) trigger
    `bpInquiryRequest` reconciliation instead of blind retries; still-unknown leaves the attempt
    Pending for operational reversal (`bpReversalRequest`, ≤ ~3h after verify, never after settle),
    exposed as `ReverseAsync` on the concrete gateway only (Zarinpal is unaffected).
  - Response-code classification is bounded and explicit per the §19 table — not every non-zero code
    is retryable.
- Exactly-once guarantees remain owned by Qasedak persistence (below). CI never contacts
  bpm.shaparak.ir: deterministic scripted SOAP fakes cover unit and API integration suites, and a
  real-credential staging smoke remains an operational deployment prerequisite (docs/08 §6).
- Currency stays canonical IRR end-to-end. Behpardakht operates IRR; no تومان↔ریال conversion exists
  anywhere.
- Frontend provider selection shows زرین‌پال and به‌پرداخت ملت per the approved Qasedak billing design.

## Exactly-once guarantees (unchanged, provider-independent)

Server creates the PaymentAttempt and owns the amount; unique Authority + persisted numeric order
identity + stored case-sensitive RefId + stored SaleReferenceId; callbacks never prove payment;
verification is server-to-server; settle happens only after explicit verification success/idempotent
success; duplicate callbacks are idempotent (terminal attempt reload); concurrent finalization extends
entitlement exactly once via DB uniqueness + xmin row-version concurrency; failed verification or
settlement never activates anything; unresolved/reversed transactions never activate anything.

## Alternatives considered

- Keeping Bank Melli/SADAD — cancelled by human decision.
- Implementing from mirrored historical manuals/community packages — rejected then, superseded now:
  the supplied v1.29 reference is the implemented contract; community sources were not used.
- Deriving the SOAP namespace from third-party clients — rejected: the namespace is
  configuration-overridable and responses are parsed by element local name, so no invented binding is
  hard-coded.

## Consequences

- Both providers are fully operational behind one port; M09-002's transport gap is closed.
- Enabling Mellat in production requires real credentials plus Shaparak-side registration of the
  deployment's public host (IP allowlist + registered domain covering the callback path and jump
  page) — recorded in docs/08 §6 and the release checklist as an operational prerequisite, not a CI
  gate.
- A future conflicting merchant onboarding document ⇒ new vendor-reference ADR; no silent change.
