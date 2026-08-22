# Qasedak — Software Test Plan

## 1. Purpose

Tests are Qasedak's executable memory. Because multiple AI agents will modify the repository over many sessions, the test system must detect behavioral drift instead of merely producing an attractive coverage percentage. A feature is not complete when only its happy path works.

## 2. Test pyramid / boundaries

### Static repository and architecture tests

`scripts/check_architecture.py`, documentation/state validation and manifest checks run on every CI change. They protect module dependency direction, required documentation/state contracts and multi-agent handoff integrity.

### Domain unit tests

Fast, deterministic tests for invariants, state transitions, matching/rule evaluation, value semantics and failure conditions. Critical automation rules should have dense boundary-case coverage and later mutation testing.

### Application/use-case tests

Test authorization expectations, orchestration, transactions/ports, result classification and duplicate/retry logic with controlled test doubles where the external boundary is genuinely abstracted. Do not reproduce implementation line-for-line in mocks.

### PostgreSQL integration tests

Use Testcontainers PostgreSQL for mappings, constraints, migrations, transaction/isolation/idempotency and query semantics. Do not use an in-memory database as evidence that PostgreSQL behavior is correct.

### External contract tests

Meta/payment adapters use deterministic scrubbed fixtures and mock HTTP endpoints to validate request/response mapping, signatures/errors and provider classifications. Normal CI never depends on live Meta availability or real credentials.

### API integration tests

Host the ASP.NET application in process (`WebApplicationFactory`) and test routing, serialization/problem details, authentication/authorization, middleware and module composition. For persistence-backed scenarios, pair with PostgreSQL container infrastructure.

### Frontend tests

Start with repository contract/unit/component behavior tests and add a real browser layer (for example Playwright) for critical approved flows once the UI exists. Test loading/error/permission/connection states, not only static snapshots.

## 3. Regression rule

Every confirmed bug should gain a regression test that fails without the fix whenever technically meaningful. An agent may not skip/remove/weaken assertions, suppress warnings, reduce test scope or lower gates merely to make CI pass without explicit human approval recorded in state/ADR where appropriate.

## 4. Critical scenario matrix

At minimum, future suites must cover:

- cross-workspace resource access denied even with guessed IDs;
- OAuth state tamper/replay/denial/expired callback handling;
- token redaction/protection and revoked connection behavior;
- invalid/forged webhook rejected;
- identical webhook delivered multiple times creates one logical effect;
- concurrent duplicate webhook/automation execution remains safe;
- automation cannot activate without valid trigger/action;
- deterministic condition matching across edge cases;
- external action succeeds, transiently fails, permanently fails, times out;
- retry after ambiguous/partial outcome does not blindly duplicate effect;
- billing/provider webhook replay does not duplicate entitlement/subscription changes;
- DB migration from previous supported state succeeds;
- frontend permission/error/loading states remain usable.

## 5. Coverage policy

Coverage is a diagnostic and threshold guard, not the product objective. Once meaningful code exists, target at least ~90% line/branch attention for critical pure Domain rule sets and ~80% for Application behavior, with exclusions explicitly justified. Do not invent a coverage target for the empty scaffold simply to report a high number. M00/M02 introduce the concrete collector/gates as real code appears.

## 6. Mutation testing

By M10, mutation testing (for example Stryker.NET for selected C# rule assemblies) should run on the most critical automation/authorization/idempotency logic. Mutation score is used to expose weak assertions, not blindly maximize every module.

## 7. Performance and reliability tests

Before production, execute representative load profiles for webhook ingress, conversation queries and automation evaluation using realistic cardinality and duplicate/retry patterns. Capture latency/error/resource baselines. Chaos/fault-focused tests should demonstrate safe behavior during provider failures, PostgreSQL interruption/restart and worker retries where the runtime design supports it.

## 8. Security testing

Static analysis/CodeQL and dependency updates are baseline gates. Add targeted authorization/IDOR, request authenticity/replay, rate-limit/abuse, secret/log-redaction and malformed-input tests. High-risk security decisions receive threat-oriented review rather than relying on scanner output alone.

## 9. CI gates

Pull requests must pass: repository contract checks; architecture rules; backend restore/build/tests; frontend lint/typecheck/tests/build; and Docker builds. Release workflows publish only after normal branch/tag policy and should preserve provenance/SBOM metadata. CI shall not use production secrets.

## 10. Test data

Use synthetic/scrubbed deterministic fixtures. Never commit production messages, access tokens, personal data or real payment credentials. Time/random IDs should be controllable where assertions depend on them.

## 11. Exit criteria per task

A task may be marked DONE only when acceptance behavior is tested at the correct boundary; existing suites remain green; no unexplained skips/suppressions are added; architecture/state/docs are current; Graphify evidence exists; and exact not-run external gates are disclosed. `scripts/agent_finalize.py` and `scripts/verify.py --full` are the standard handoff gates.
