# M08-000 — Billing, subscription and payment design

- Canonical/connected file UUID:
  `c269caa0-e456-818c-8008-85a77340be64` — PASS
- Requested mappings: `billing.plans`, `billing.subscription`,
  `billing.checkout`, `billing.payment-result`
- Was target page manually opened by human? **NO**
- Was page activated programmatically? **YES**
- Penpot revision: `null`

## Stable targets

Page `Qasedak · Billing & Payments`:
`c48311ed-e700-80f8-8008-8820a6cf5187`

| Board | Stable UUID |
|---|---|
| Billing / Plans / Desktop | `c48311ed-e700-80f8-8008-8820a7020aa1` |
| Billing / Current Subscription / Desktop | `c48311ed-e700-80f8-8008-8820adebc780` |
| Billing / Checkout / Desktop | `c48311ed-e700-80f8-8008-8820b1f8bfe9` |
| Billing / Payment Results / Desktop | `c48311ed-e700-80f8-8008-8820b826931b` |
| Billing / Checkout / Mobile | `c48311ed-e700-80f8-8008-8820bd6206dd` |

Reusable Payment Provider Option component:
`c48311ed-e700-80f8-8008-881eaba064db`.
The existing Plan Selector component
`f5bf3c2c-b970-8002-8008-874b64c35ccf` remains mapped for plan-selection reuse.

## Coverage

- plans and subscription selection;
- current plan/entitlement summary and one-off renewal;
- provider selection using «زرین‌پال» and «پرداخت مستقیم بانک ملی»;
- server-owned order summary and redirect transition;
- pending server verification, success, failure, cancellation,
  already-verified/idempotent return and indeterminate verification/retry;
- intentional mobile checkout.

The UI never requests card details or exposes provider credentials. It does not promise
automatic recurring card charging. Amount, currency, discount, tax and entitlements are
rendered from Billing server responses; the browser and callback are not amount sources.

Payment/invoice history is visibly deferred because the current Billing module has no
persistence/query contract. It must not be implemented from invented data.

## Architecture dependency

ADR-008 selects Zarinpal and Bank Melli/SADAD behind provider-neutral Billing contracts.
No current official Bank Melli/SADAD merchant specification or credentials exist in the
repository, and a bounded official-domain search did not find a public current technical
contract. That remains a dependency for the Melli adapter only. Zarinpal work is not
blocked, but its current official merchant documentation must be checked again before
implementation.

## Implementation expectation

- `/dashboard/billing/plans` → `src/features/billing/ui/PlansScreen.tsx`
- `/dashboard/billing` → `src/features/billing/ui/SubscriptionScreen.tsx`
- `/dashboard/billing/checkout` → `src/features/billing/ui/CheckoutScreen.tsx`
- `/dashboard/billing/payment-result` →
  `src/features/billing/ui/PaymentResultScreen.tsx`

All paths are `planned`; M09 owns implementation.

## Validation

Plans, checkout, payment-result and mobile-checkout exports were visually inspected.
One mobile Bank Melli label overlap and the payment-result action labels were corrected
during the quality pass. Live target resolution and offline `billing.checkout` lookup
passed.
