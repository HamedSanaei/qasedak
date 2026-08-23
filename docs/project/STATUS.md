# Project status

**Project:** Qasedak  
**Current milestone:** M06 — Automations Engine v1 (complete; M07 next)  
**Current task:** M07-001 — Model workspace contact identity  
**Last completed:** M06-005 — Deliver comment-to-DM flow (M06 fully verified, `verify.py --full` green)  
**Product implementation:** Not started

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

- Graphify CLI 0.9.26 healthy; first real graph: 277 nodes / 297 edges / 43 communities (`graphify . --no-viz --code-only` + `graphify cluster-only .`).
- `graphify-out/graph.json`, `graphify-out/GRAPH_REPORT.md` refreshed; five bounded queries recorded as healthy evidence in `.agent-state/GRAPHIFY_EVIDENCE.md`.
- Mode is code-only (local AST): no LLM API key on this machine; doc semantic extraction stays unavailable until a key is provided, then re-run without `--code-only`.

### Toolchain and gates (M00-004)

- Toolchain resolved: .NET SDK 10.0.302, Node 24/npm 11, Docker engine 29.7.2. TypeScript pinned to 6.0.3 because the installed typescript-eslint hard-fails on TS ≥ 7.
- Starter defects fixed: missing `using Xunit;` in both test projects; CA1707 underscore test names renamed (warnings are errors repo-wide).
- Dependencies locked: `package-lock.json` committed; frontend Dockerfile and CI now use `npm ci`; minimal `.dockerignore` files added for both image contexts.
- All local gates green: backend Release build 0 warnings/0 errors, format check pass, tests 3/3; frontend lint/typecheck/test/build pass (repository contract tests 2/2); Docker images `qasedak-api:verify` and `qasedak-web:verify` build successfully.
- `generate_manifest.py` ignores gitignored runtime artifacts (`cache` dirs, `tsconfig.tsbuildinfo`) and `verify.py` resolves npm correctly on Windows, keeping every gate honest on fresh checkouts.

## Meta feasibility & contracts verified (M01)

- `docs/product/instagram-mvp-capability-matrix.md` — capability rows grounded in official Meta docs (webhook requirements table, private replies, 24-hour window/Human Agent tag, business login scopes); comment→DM is Private-Reply-only; messaging requires the Messenger Platform path.
- `docs/product/meta-oauth-token-lifecycle.md` — full OAuth flow, `instagram_business_*` scopes, 60-day long-lived tokens with verified refresh preconditions, module ownership and health-state surface.
- Webhook authenticity spike: Application ports + Infrastructure HMAC/challenge implementations; new `Qasedak.Modules.Instagram.UnitTests` passes 20/20 deterministic fixture tests (including escaped-unicode raw-bytes contract).
- ADR-006 (integration paths) and ADR-007 (webhook authenticity) accepted; SRS §4 now binds Meta-facing requirements to these contracts.

## Next action

Start milestone **M02** with **M02-001**: model the workspace membership domain (users/workspaces/memberships/roles and invariants) in the Identity module Domain layer, with domain unit tests.
