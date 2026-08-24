# Sync record — Qasedak final designs (Auth / Inbox / Billing)

- Date: 2026-08-24
- Agent: ox-alpha (DeepSeek Harness session)
- Canonical Penpot file: `c269caa0-e456-818c-8008-85a77340be64` (verified live via MCP; 39 pages enumerated)
- Design source of this run: Codex design-completion boards on the four new `Qasedak ·` pages

## Penpot MCP evidence (what was actually read)

- `penpotUtils.getPages()` → full page inventory incl.
  `Qasedak · Product UI Components` (c48311ed-e700-80f8-8008-881e9771c583),
  `Qasedak · Identity & Workspace` (…881f0352eb6a),
  `Qasedak · Inbox & Conversations` (…88200ec40bf3),
  `Qasedak · Billing & Payments` (…8820a6cf5187).
- `getPageById` + `openPage` per page; board tree inspection (names/types/texts/fills/
  radii/font sizes/positions) for: Identity Login/Register Desktop (+ Mobile + States
  boards), Inbox Desktop (+ Product States), Billing Plans / Current Subscription /
  Checkout / Payment Results (+ Checkout Mobile).
- Extracted contract persisted to `docs/design/sync/2026-08-24-qasedak-final-designs.md`
  (shared tokens + per-screen values). No visual values were invented.

## Mappings consumed / updated in penpot-sync.json

| Mapping | Page | Board | Result |
| --- | --- | --- | --- |
| identity.auth | Qasedak · Identity & Workspace | Identity / Login / Desktop (`…881f0372388a`) | **draft → approved**; GetCode OTP boards remain non-authoritative reference only |
| inbox.conversations (NEW) | Qasedak · Inbox & Conversations | Conversations / Inbox / Desktop (`…88200ed6b9fc`) | approved — removes the historical M08-004 "no design source" blocker |
| billing.payment (NEW) | Qasedak · Billing & Payments | Billing / Plans / Desktop (`…8820a7020aa1`) | approved — plans/subscription/checkout/results implemented |

## Implementation outcome

- **identity.auth**: visual reconciliation executed against the final design
  (split-hero brand panel, auth card tokens, security/policy boxes, mobile stacking);
  email+password identity behavior, validation, API integration and tests untouched.
- **inbox.conversations**: reconciliation executed per extracted contract; search stays
  disabled BY DESIGN until the backend ships a query capability (honest divergence,
  documented); existing presentation logic/tests preserved.
- **billing.payment**: new `/dashboard/billing`, `/dashboard/billing/checkout`,
  `/dashboard/billing/result` screens over the new Billing HTTP surface; amounts render
  exactly as received (IRR grouping + ریال); checkout submits only plan code + provider;
  result page polls the server status endpoint (callback hints alone never claim success).
- New frontend tests: `tests/billing.test.mjs` (presentation mapping, IRR formatting,
  fail-closed tones, client contract asserting no client-supplied price).

## Divergences / honesty notes

- Inbox search input renders DISABLED with a warning badge — the design itself marks it
  as pending backend search support.
- Bank Melli provider radio renders but stays disabled with «غیرفعال — قرارداد رسمی
  تأیید نشده» until the official SADAD/Bank Melli merchant technical contract exists.

## Addendum (2026-08-24, later the same day) — provider change per ADR-009

The human cancelled Bank Melli/SADAD and selected Behpardakht Mellat as the second v1
provider. Through Penpot MCP the Checkout board labels were updated IN THE CANONICAL FILE
(design system unchanged): «پرداخت مستقیم بانک ملی» → «به‌پرداخت ملت» and mark «ملی» →
«ملت» on both `Billing / Checkout / Desktop` (shapes c48311ed-e700-80f8-8008-8820b7248b33,
…8820b73fb602, …8820b756b100) and `Billing / Checkout / Mobile` (shapes
c48311ed-e700-80f8-8008-8820bf9203e5, …8820bfacaa1e). Next.js checkout was reconciled to
the updated design; the manifest basis text above records this. The original bullet above
is preserved as history of the earlier same-day state.
- The reply-composer counter binds to the backend's actual cap; the design's illustrative
  numbers are not treated as limits.

## Gates at time of writing

- `python scripts/validate_penpot_sync.py` → PASSED (6/6 checks)
- `python scripts/check_architecture.py` → PASSED (35 projects, 6 business modules)
- `npm run verify` (lint max-warnings 0, typecheck, tests, next build) → PASSED
