# Current handoff

## Where we are

**M09-002 is DONE (2026-08-24): executable scope + final Penpot design reconciliation +
Behpardakht Mellat live transport.** The vendor technical reference
`docs/vendor/behpardakht/BEHPARDAKHT-IPG-v1.29-EN.md` (IPG User Guide v1.29 EN,
"Unofficial - External" — provenance preserved) was supplied by the human and used as the
sole protocol source for a complete Mellat SOAP transport behind the existing
provider-neutral port. Nothing is committed — working tree only, per contract.

## What this run delivered (Mellat transport completion)

### Transport (backend)
- `BehpardakhtSoapClient`: explicit SOAP 1.1 envelopes over typed HttpClient for
  bpPayRequest/bpVerifyRequest/bpSettleRequest/bpInquiryRequest/bpReversalRequest;
  XML-escaped parameters; namespace-agnostic `*Response`→`return` parsing by local name
  (namespace itself is config-overridable `ServiceNamespace`, not invented); SOAP fault /
  non-success HTTP / timeout → `PaymentGatewayUnavailableException`. No SOAP-generated
  types outside Infrastructure (`InternalsVisibleTo` test assemblies only).
- `BehpardakhtMellatPaymentGateway`: pay per §8 (ten string params, amount IRR unchanged,
  payerId "0", deterministic numeric orderId derived from the attempt id), defensive
  `"ResCode,RefId"` parse with ResCode=0 requiring a non-empty exact-case RefId persisted
  on the attempt; verify→settle chain with idempotent 43/45, bounded §19 classifier
  (success/idempotent/user-cancel/configuration/definitive/unknown), Inquiry reconciliation
  of unknown verify outcomes instead of blind retry, reversal ≤ ~3h post-verify exposed as
  `ReverseAsync` on the concrete gateway only (never after settle, never on the port).
- Callback: POST form variant on the public callback route parses RefId/ResCode/
  SaleOrderId/SaleReferenceId/CardHolderPan(masked) → OK/CANCEL/FAILED hints; mandatory
  identity check BEFORE verification: callback SaleOrderId must exactly equal the stored
  ProviderOrderId and callback RefId must resolve the stored attempt — mismatch marks
  `payment.callbackRejected`, makes ZERO bank calls, activates nothing, audited.
- Jump endpoint `GET /api/v1/payments/mellat/startpay` renders an auto-submitting form
  posting only the RefId to the configured payment page; credentials never reach the
  browser; hosted on the registered merchant domain so Referer rule §62 holds.
- Persistence: new nullable `ProviderOrderId` column on `billing.payment_attempts`
  (migration `AddPaymentProviderOrderId` + Designer + snapshot); `{provider}` placeholder
  in checkout callback templates now resolves to the selected gateway's provider id.
- Config/docs: typed options extended (ServiceUrl/PaymentPageUrl/ServiceNamespace);
  `.env.example` + docker-compose passthroughs + appsettings.json aligned; docs/08 §6
  rewritten as implemented-with-prerequisites; ADR-009 updated to cite the vendor doc.

### Tests
- Billing unit 119/119 incl. new BehpardakhtMellatTransportTests (envelope contents +
  escaping; malformed pay/code parsing incl. empty-RefId fail-closed; §19 classification
  table; orchestration scripts pay/verify/settle/inquiry/reverse across success, idempotent,
  definitive, timeout paths; use-case callback validation asserting ZERO verify calls on
  forged SaleOrderId and exactly-once activation across duplicate callbacks).
- API e2e (real host + real PostgreSQL + scripted SOAP fake; CI never touches
  bpm.shaparak.ir): Mellat checkout persists ProviderOrderId + jump redirect with exact-case
  REF; jump page HTML carries exact RefId and zero credential material; form callback
  activates exactly once (verify+settle once, duplicate replay harmless, single period);
  forged SaleOrderId → `payment.callbackRejected` with no entitlement.
- Full backend solution suite green: 458 tests across all unit + Testcontainers projects.

## Earlier this day (context)

- Payment architecture shipped: `PaymentAttempt` exactly-once persistence, neutral port,
  Zarinpal official v4 REST gateway, endpoints, migration, ADR-008/ADR-009.
- Codex final designs reconciled into Next.js (auth/inbox/billing); Penpot sync manifest
  validated; frontend suites green.

## Verification status

- Backend: Release build clean; full solution suite green as listed above.
- Gates still to run at finalize time this session: `dotnet format --verify-no-changes`,
  `validate_penpot_sync.py`, `check_architecture.py`, `agent_finalize.py --task M09-002`,
  `verify.py --full`.

## Next actions for a human

1. Operational go-live prerequisites (not CI): real Mellat terminal credentials; Shaparak
   registration of the deployment's public host (IP allowlist; callback path + jump page
   inside the registered domain); staging smoke incl. deliberate cancel (ResCode 17) and a
   duplicate-callback replay. Same pattern applies to a Zarinpal staging smoke when ready.
2. Continue with the next task in TASKS.md.

## Next task for an agent

M09-002 is fully DONE; pick the next actionable task from TASKS.md. Do not commit/push/tag
unless explicitly asked; suggested commits are recorded per task in TASKS.md.
