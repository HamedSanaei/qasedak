# Qasedak — Vision Document

**Status:** Engineering baseline / product details to be validated during M01  
**Audience:** product owner, software engineers, AI coding agents, designers, operators

## 1. Product vision

Qasedak is a Persian-first SaaS platform for compliant social-conversation automation, beginning with Instagram. Businesses should be able to connect an eligible Instagram professional account, centralize supported conversations/interactions, and define safe automations such as event-triggered responses without building or operating their own Meta integration.

The product is not intended to bypass Meta platform restrictions. Its value is to make officially supported capabilities reliable, understandable and usable by non-technical teams while preserving auditability and operational safety.

## 2. Problem statement

Small and medium businesses receive repetitive Instagram comments and direct messages, lose leads during busy periods, and manually repeat routing, qualification and response work. Existing tools can be expensive, opaque, difficult to localize, or poorly fitted to local workflows. A dependable automation product must also absorb OAuth/token complexity, webhook retries, API failures, rate limits and platform policy changes that end users should not need to understand.

## 3. Target users

- Business owners managing Instagram as a sales/support channel.
- Social-media operators handling comments, inbox and lead follow-up.
- Teams that need controlled access to a shared workspace rather than shared credentials.
- Operators/administrators who require observability, billing, support and incident tooling.

## 4. Product principles

1. **Official APIs first.** Use supported Meta APIs and documented permissions; never design around scraping or credential sharing.
2. **Automation must be explainable.** A user should understand why a rule fired, which input version it used and what action occurred.
3. **Duplicate-safe by design.** Webhook retries or worker retries must not create duplicate intended effects.
4. **Workspace isolation.** Every business-owned resource is scoped and authorized server-side.
5. **Reliability over feature count.** A smaller automation set with strong tests and failure behavior is preferable to many brittle actions.
6. **Design before UI implementation.** Penpot-approved screens are the source of visual intent; Next.js is the production implementation.
7. **Repository state is durable memory.** AI agents must be able to continue work without relying on chat history.

## 5. MVP direction

The provisional MVP consists of account/workspace identity, supported Instagram account connection, durable webhook ingestion, an inbox/conversation projection, an automation engine with an initially narrow supported trigger/action set, lightweight contacts, and the core Next.js management UI. Billing may follow after the core loop is validated depending on launch strategy.

The exact automation matrix is intentionally **not frozen in this starter**. M01 must verify current official Meta capabilities, permissions, app-review requirements and messaging/comment limitations before behavior becomes a product commitment.

## 6. Explicit non-goals for the initial product

- Scraping private Instagram data or automating personal-account credentials.
- Sending arbitrary unsolicited bulk DMs outside supported platform rules.
- A general-purpose workflow engine unrelated to social automation.
- Microservices from day one.
- Supporting every social network before the Instagram core is reliable.
- Letting the frontend implement security, billing entitlement or business invariants.

## 7. Success measures

Product metrics will be finalized after capability validation, but engineering success requires: deterministic webhook processing, very low duplicate-action incidence, visible connection/token health, measurable automation success/failure, safe workspace authorization, reproducible deployments, and a regression suite strong enough that multi-agent development cannot silently change core behavior.

## 8. Constraints

- Backend: ASP.NET Core Web API / C# on .NET 10.
- Architecture: Modular Monolith with Clean Architecture inside each module.
- Frontend: independent Next.js application; no separate Vite SPA.
- Data: PostgreSQL with module-owned schemas.
- Delivery: GitHub Actions and immutable Docker images.
- UI design: Penpot before production screen implementation.
- AI-assisted engineering: Graphify is mandatory for repository navigation and every task updates durable project state.

## 9. Major risks

The largest early risk is external-platform dependency: Meta capabilities, permissions, review rules, token behavior and quotas can change. Other important risks are webhook redelivery/concurrency, accidental cross-workspace data exposure, unbounded automation side effects, token leakage, brittle generated UI, and agent-to-agent architectural drift. The milestone plan deliberately addresses these before breadth.
