# Qasedak — Deployment Document & User Guide

## 1. Status

This is a starter deployment contract plus provisional user guide. Production instructions become operationally authoritative only after M10/M11 migration, backup, secrets, TLS, observability and rollback rehearsals complete.

## 2. Runtime components

- `qasedak-api`: ASP.NET Core API on port 8080 inside its container.
- `qasedak-web`: Next.js standalone server on port 3000.
- PostgreSQL 18: persistent relational state.
- Future durable worker/queue components are added only by an architecture task/ADR when M04/M06 needs them.

Frontend and backend are separate images, deployed together. Do not run Node and ASP.NET as ad-hoc background processes inside one container.

## 3. Local Docker development

Copy `.env.example` to `.env` for local-only values, then:

```bash
docker compose up --build
```

Expected local endpoints:

- Web: `http://localhost:3000`
- API scaffold: `http://localhost:8080/api/v1/system`
- API liveness: `http://localhost:8080/health/live`
- PostgreSQL: localhost port 5432 unless overridden.

The default password in examples is intentionally local-only and must never be reused in production.

## 4. Direct development

Backend requires .NET 10 SDK. Frontend requires Node.js 22+. PostgreSQL 18 can be provided by Compose while running API/web from local toolchains. The exact commands become locked by M00-004 once lockfiles/restores are verified.

Before broad AI-agent development, Graphify must be initialized according to `AGENTS.md` and M00-003.

## 5. CI/CD

`.github/workflows/ci.yml` runs repository contracts, .NET build/tests, Next.js quality/build, then Docker builds. CodeQL and Dependabot provide additional security/dependency automation. `images.yml` publishes API/web images to GHCR on version tags or explicit manual workflow dispatch, including build provenance/SBOM configuration.

M00-004 replaces baseline npm install behavior with the committed lockfile/deterministic install contract after a real dependency resolution run.

## 6. Production configuration

Production must provide, at minimum, database/database-user credentials, protected connection string, public web/API origins, authentication secrets, Meta application credentials and later provider-specific secrets. Store these in deployment secret facilities, never source control or image layers.

### Payment provider environment contract (ADR-008)

Injected into the API container as standard ASP.NET configuration keys (double underscore). `.env.example` and `docker-compose.yml` carry the same names.

| Key | Secret | Meaning |
| --- | --- | --- |
| `Billing__Payments__CallbackBaseUrl` | no | Absolute public base of the API; builds provider return URLs (no secrets inside them). |
| `Billing__Payments__FrontendBaseUrl` | no | Absolute base of the web app for post-payment result redirects. |
| `Billing__Payments__Zarinpal__Enabled` | no | Checkout may offer `zarinpal` only when true. |
| `Billing__Payments__Zarinpal__MerchantId` | **yes** | 36-character Zarinpal merchant code. Never logged (structured logs exclude it). |
| `Billing__Payments__Zarinpal__BaseUrl` | no | Official payment API base (`https://payment.zarinpal.com`). |
| `Billing__Payments__Zarinpal__Currency` | no | Canonical Qasedak currency `IRR`; do not switch to IRT without a new ADR. |
| `Billing__Payments__Mellat__Enabled` | no | Checkout may offer `mellat` only when true. Implemented against the vendor reference `docs/vendor/behpardakht/BEHPARDAKHT-IPG-v1.29-EN.md` (IPG User Guide v1.29, Tir 1402, "Unofficial - External" translation — provenance preserved; a newer conflicting merchant onboarding document requires a vendor-reference ADR before any change). |
| `Billing__Payments__Mellat__TerminalId` / `Username` / `Password` | **yes** | Merchant Internet-terminal number and portal credentials (vendor §4/§21.1). Issued by Behpardakht onboarding; never committed, never logged. |
| `Billing__Payments__Mellat__ServiceUrl` | no | SOAP service endpoint (default `https://bpm.shaparak.ir/pgwchannel/services/pgw?wsdl`, vendor §6.1). |
| `Billing__Payments__Mellat__PaymentPageUrl` | no | Persian payment page the browser is POSTed to with the RefId (default `https://bpm.shaparak.ir/pgwchannel/startpay.mellat`, vendor §8.2). |
| `Billing__Payments__Mellat__ServiceNamespace` | no | SOAP target namespace for request envelopes (config-overridable; default follows the classic binding convention) — override if Shaparak publishes a different WSDL binding. |
| `Billing__Payments__Mellat__CallbackBaseUrl` | no | Absolute public callback base. MUST be inside the domain registered with Behpardakht (vendor §5/§9/§62): Shaparak rejects callbacks from unregistered domains and requires the payment-page Referer to match. |

Operational prerequisites before enabling Mellat in production (no real-credential smoke test runs in CI by design):

1. Real terminal credentials issued to Qasedak's merchant account.
2. The deployment's public API host registered with Behpardakht (IP allowlist + callback path inside the registered domain); the jump endpoint `/api/v1/payments/mellat/startpay` is hosted on this same domain so the payment-page Referer requirement holds.
3. Staging smoke test with small real amounts: pay → redirect → callback → verify → settle → subscription Active, plus one deliberate cancel (ResCode 17) and one duplicate callback replay.
4. Reconciliation runbook awareness: unresolved verify outcomes are reconciled via Inquiry; reversal (`bpReversalRequest`) applies only within ~3 hours after verification and never after settlement.

A verified payment activates/extends the workspace subscription exactly once via database uniqueness + row-version concurrency on `billing.payment_attempts`; callback query parameters alone never activate anything.

A reverse proxy/ingress provides TLS, public routing and security headers as finalized in M11. Only required services/ports are public; PostgreSQL should normally remain private.

## 7. PostgreSQL persistence

The official PostgreSQL 18 Docker image uses a version-specific `PGDATA` below `/var/lib/postgresql` and declares `/var/lib/postgresql` as the persistent volume. Qasedak's Compose files therefore mount that parent. Never change persistent-volume paths casually during upgrade; major upgrades require a documented migration/backup plan.

## 8. Migrations and deployment sequence

Before production release, define a controlled sequence similar to: verify image/config → database backup/check → run approved migration step → start/update API/web → readiness/smoke tests → observe error/latency signals → complete or roll back. Do not let multiple application replicas independently race destructive migrations.

## 9. Health and observability

The scaffold exposes `/health/live` and `/health/ready`. Readiness will gain real dependency checks as persistence/external requirements are implemented. Production monitoring must include application errors, webhook processing, automation outcomes, connection/token health, PostgreSQL health/capacity and resource saturation without exposing sensitive message/token content.

## 10. Backup / restore / rollback

M10 requires a documented/rehearsed PostgreSQL backup and restore into a clean environment plus application smoke checks. Releases use immutable image tags so application rollback is deterministic. Database rollback compatibility must be assessed for each migration; destructive incompatible schema changes require explicit sequencing rather than “docker image rollback” assumptions.

## 11. Initial user guide (provisional)

The final UI will be based on Penpot designs, so exact labels may change. The intended core flow is:

1. Sign in and create/select a workspace.
2. Open Instagram account settings and start the supported Meta connection flow.
3. Confirm connection health/permissions.
4. View supported inbound conversations/interactions in Inbox as implemented.
5. Create an automation using supported triggers/conditions/actions.
6. Validate and activate it; inspect execution/result information.
7. Disable an automation or disconnect an account when it should no longer act.

The product must never instruct users to paste Instagram passwords into Qasedak for unsupported automation.

## 12. Operations checklist before first production launch

All M00–M10 exit criteria relevant to launch are green; Meta capability/review status is current; secrets rotated/injected; database backup restore tested; migrations rehearsed; TLS/domain/routing configured; rate/abuse controls enabled; telemetry/alerts verified; legal/privacy/retention decisions recorded; recovery/rollback rehearsed; Penpot/Next.js critical flows accessibility-tested; and project state accurately points to the release commit/tag/images.
