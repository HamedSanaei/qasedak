# Project status

**Project:** Qasedak  
**Current milestone:** M09 — Payments & Billing  
**Current task:** M09-002 — Payment gateway integration (Zarinpal operational; Behpardakht Mellat boundary shipped, live transport externally blocked)  
**Last completed:** M08-005 → superseded by 2026-08-24 Codex design-completion + Qasedak final-design reconciliation run  
**Product implementation:** In progress (M09)

## 2026-08-24 — Payment architecture (M09-002 executable scope) COMPLETE

- `PaymentAttempt` aggregate (Pending→Verified|Failed), xmin optimistic concurrency,
  unique filtered Authority index = anti-replay; verified payment extends entitlement
  exactly once; callback queries alone never activate anything.
- Provider-neutral `IPaymentGateway` in Application; Infrastructure owns protocols:
  `ZarinpalPaymentGateway` implements the CURRENT official v4 REST contract
  (request.json/verify.json, code 100/101 semantics, StartPay redirect); typed options;
  secrets server-side only; merchant id/secrets/payloads/card PAN never logged.
- **Provider decision updated same day (ADR-009): Bank Melli/SADAD CANCELLED; Behpardakht
  Mellat selected.** `BehpardakhtMellatPaymentGateway` (`providerId="mellat"`) is a
  fail-closed boundary with typed `BehpardakhtOptions`; enabling without the verified
  current official contract surfaces `payment.providerUnavailable` naming exactly which
  documents are required. Historical bpPayRequest/bpVerify/bpSettle flow treated as
  background only — nothing copied into transport.
- Endpoints: plans catalog, workspace subscription, checkout (202 + server-owned
  redirect), payment status/history, public provider callback → 302 to frontend result
  page. Migration `AddPaymentsAndPlanPrices`; env contracts in `.env.example`,
  docker-compose and deployment guide §6 (`MELLAT_*`); ADR-008 + ADR-009 accepted.
- Penpot Checkout boards updated in-file via MCP: «پرداخت مستقیم بانک ملی» → «به‌پرداخت
  ملت» on Desktop+Mobile; frontend reconciled; design system unchanged.
- Tests: Billing unit 61/61; Billing integration (Testcontainers) incl. concurrent
  verify exactly-once 9/9; full Api.IntegrationTests 46/46.

## 2026-08-24 — Final Penpot designs reconciled into the app

- Codex completed four new `Qasedak ·` pages in the canonical file
  (`c269caa0-e456-818c-8008-85a77340be64`); all boards live-inspected via MCP.
- Extracted contract: `docs/design/sync/2026-08-24-qasedak-final-designs.md`;
  sync record: `docs/design/sync/2026-08-24-qasedak-final-sync-record.md`.
- Manifest updates (validated 6/6): `identity.auth` draft→**approved** on
  `Qasedak · Identity & Workspace`; NEW `inbox.conversations` **approved** (removes the
  historical M08-004 no-design blocker; evidence preserved); NEW `billing.payment`
  **approved** across Plans/Subscription/Checkout/Results boards.
- Frontend: auth screens visually reconciled (email+password behavior untouched);
  inbox reconciled (search disabled BY DESIGN until backend query ships); new billing
  UI `/dashboard/billing`, `/dashboard/billing/checkout`, `/dashboard/billing/result`
  with server-authoritative IRR amounts and bounded status polling; new
  `tests/billing.test.mjs`. `npm run verify` green.

## Next action

1. Human action: obtain the CURRENT official Behpardakht Mellat merchant technical
   documents (service endpoints/WSDL, payment/verify/settle operation contracts,
   response-code table, callback field schema, reversal/inquiry semantics if the contract
   defines them). Until then Mellat stays boundary-only and M09-002 remains honestly
   partial (Zarinpal production-capable).
2. Optional hardening when credentials exist: staging-environment Zarinpal smoke test
   (never in CI).

## Baseline established

- Modular Monolith backend boundary defined.
- Clean Architecture inside each module: Infrastructure → Application → Domain.
- ASP.NET Core Web API composition root scaffolded.
- Independent Next.js frontend scaffolded for future Penpot implementation.
- PostgreSQL 18 deployment baseline defined with module-owned logical schemas.
- CI, image publishing, CodeQL and Dependabot workflows scaffolded.
- Architecture/state/documentation guard scripts scaffolded.
- English engineering document set and Persian printable HTML document set created.
- Milestones/tasks and multi-agent handoff protocol created.

## Engineering foundation verified (M00)

### Graphify (M00-003)

- Graphify CLI 0.9.26 healthy; mode is code-only (local AST): no LLM API key on this
  machine; doc semantic extraction stays unavailable until a key is provided, then
  re-run without `--code-only`.
- Evidence recorded per task in `.agent-state/GRAPHIFY_EVIDENCE.md`.

### Toolchain and gates (M00-004)

- Toolchain resolved: .NET SDK 10.0.302, Node 24/npm 11, Docker engine 29.7.2. TypeScript pinned to 6.0.3 because the installed typescript-eslint hard-fails on TS ≥ 7.
- Dependencies locked: `package-lock.json` committed; frontend Dockerfile and CI use `npm ci`.
- All local gates green: backend Release build 0 warnings/0 errors, format check pass; frontend lint/typecheck/test/build pass; Docker images build successfully.
- `generate_manifest.py` ignores gitignored runtime artifacts and `verify.py` resolves npm correctly on Windows.

## Meta feasibility & contracts verified (M01)

- `docs/product/instagram-mvp-capability-matrix.md` — capability rows grounded in official Meta docs; comment→DM is Private-Reply-only; messaging requires the Messenger Platform path.
- `docs/product/meta-oauth-token-lifecycle.md` — full OAuth flow, scopes, token lifecycle, module ownership.
- Webhook authenticity: Application ports + Infrastructure HMAC/challenge implementations; ADR-006 (integration paths) and ADR-007 (webhook authenticity) accepted.