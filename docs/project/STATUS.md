# Project status

**Project:** Qasedak  
**Current milestone:** M12 — v2 Product Features  
**Current task:** M12-002 — Enable inbox thread context panel
**Last completed:** M12-002 (2026-08-29)
**Product implementation:** In progress (M12)

## 2026-08-29 — Inbox thread context panel COMPLETE (M12-002 → DONE)

- Backend: read-only workspace-scoped lookup `GET /api/v1/workspaces/{id}/contacts/by-identity`
  (`IContactQueries.FindByIdentityAsync`, resolving `MergedIntoId` chains) so a conversation's
  `(channel, participantId)` resolves to its CRM contact; reuses the by-id detail payload.
  New e2e `ContactResolvesByProviderIdentityAndReturnsCrmSurface` (resolve → tag/note
  mutations reappear on re-resolve, 404 for unknown identity, 400 for missing params, 403
  for foreign workspace).
- Frontend: `src/shared/api/contacts.ts` (resolve + tag/note mutations), `src/features/contacts/presentation.ts`
  (copy + validation), and the thread page `[conversationId]/page.tsx` renders the
  «اطلاعات گفتگو» panel as a live CRM surface — contact name, removable tag chips + add-tag,
  notes timeline + add-note, and a neutral empty state when no contact exists yet. The
  design's «غیرفعال» badge and the «Tags و Notes تا تکمیل M07 …» warning are gone (M07 shipped).
- Sync: penpot-sync `inbox.conversations` notes updated + SCREEN-INVENTORY row + sync record
  `docs/design/sync/M12-002-thread-context-panel.md`. No fresh Penpot MCP read this session
  (MCP client unavailable) — reconciled against the extracted 2026-08-24 contract.
- Gates: backend Release build 0 warnings/0 errors, full backend suite 471/471, `npm run verify`
  47/47, validate_penpot_sync + check_architecture + check_environment_contract all PASS.

## 2026-08-28 — Server-side inbox search COMPLETE (M12-001 → DONE)

- Backend: `SearchPattern` (Conversations Application) trims search terms and escapes
  LIKE wildcards (`%`/`_`/`\`) so user input matches literally; blank terms remove the
  filter. `EfConversationQueries.ListAsync` applies the term with `EF.Functions.ILike`
  over the counterpart identity or any message body (EXISTS translation).
- HTTP surface: optional `search` query param on
  `GET /api/v1/workspaces/{id}/conversations`, composing with `status` and paging.
- Frontend: `/dashboard/inbox` search is live (250 ms debounce), the «فعلاً غیرفعال»
  badge is removed, empty state distinguishes no-results from empty inbox; client
  contract tests updated.
- Tests: 8 new `InboxSearchTests` unit cases (Conversations suite 23/23); a new API e2e
  scenario (`InboxListSupportsCaseInsensitiveSearchAcrossParticipantAndBodies`) is ADDED
  but NOT executed — Docker daemon was down this session (honest residual).
- Gates: backend Release build 0 warnings, `dotnet format --verify-no-changes` clean,
  all unit suites green (380); frontend `npm run verify` green (37 tests incl. search
  contract, lint/typecheck/build).
- Sync evidence: `docs/design/sync/M12-001-inbox-search.md` (enabled-state divergence:
  placeholder «جستجو در گفتگوها…» — the design only defined the disabled state);
  SCREEN-INVENTORY inbox row updated; MILESTONES.md gained M12 (v2 Product Features);
  TASKS.md gained M12-001 DONE + M12-002/M12-003 TODO.

## 2026-08-24 — Behpardakht Mellat live transport COMPLETE (M09-002 → DONE)

- Vendor contract arrived in-repo: `docs/vendor/behpardakht/BEHPARDAKHT-IPG-v1.29-EN.md`
  (User Guide v1.29 EN translation, "Unofficial - External" provenance preserved; newer
  conflicting onboarding docs ⇒ future ADR). Used as the SOLE protocol source.
- `BehpardakhtSoapClient`: explicit SOAP 1.1 envelopes for bpPayRequest/bpVerifyRequest/
  bpSettleRequest/bpInquiryRequest/bpReversalRequest; XML-escaped params; namespace-agnostic
  response parsing; fault/HTTP/timeout → typed Unavailable. No SOAP types escape Infrastructure.
- Gateway orchestration: pay per §8 (IRR unchanged, payerId 0, deterministic orderId persisted
  as new `ProviderOrderId` column + migration), exact-case RefId persisted; jump endpoint
  `/api/v1/payments/mellat/startpay` auto-posts only RefId to startpay.mellat; POST form
  callback normalized to OK/CANCEL/FAILED with mandatory identity check BEFORE verification
  (SaleOrderId must equal stored ProviderOrderId; mismatch → `payment.callbackRejected`,
  zero bank calls, audited); verify→settle chain with idempotent 43/45, bounded §19 code
  classifier, Inquiry reconciliation of unknown outcomes, reversal ≤ ~3h post-verify on the
  concrete gateway only. Callback values never prove payment; entitlement exactly once intact.
- Typed options extended (`ServiceUrl`/`PaymentPageUrl`/`ServiceNamespace`, overridable);
  `.env.example`/docker-compose/appsettings aligned; docs/08 §6 rewritten as implemented +
  operational go-live prerequisites; ADR-009 updated to reference the vendor doc path.
- Tests: billing unit 119/119 (new envelope/parsing/classifier/orchestration/callback-validation
  suites); API e2e over real host + PostgreSQL + scripted SOAP fake: jump redirect + persisted
  ProviderOrderId, jump page HTML carries exact RefId and no credentials, form callback
  activates exactly once (verify+settle once, duplicate harmless), forged SaleOrderId rejected
  without any bank call or entitlement. Full backend suite green (458 tests).

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

1. Operational (human, not CI): Mellat go-live per docs/08 §6 — real terminal credentials,
   Shaparak registration of the deployment's public host (IP allowlist; callback path +
   jump page inside the registered domain), staging smoke incl. deliberate cancel and
   duplicate replay; same for a Zarinpal staging smoke when its merchant account is ready.
2. Continue M09 with the next task in TASKS.md.

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