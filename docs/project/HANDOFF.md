# Current handoff

## Where we are

Milestone M00 — Engineering Foundation — is complete. The repository builds and tests green on the workstation, dependencies are locked, both container images build, and Graphify is initialized and healthy. No Instagram/product feature exists yet.

## Completed — M00-003 (Graphify)

- Graphify 0.9.26 healthy; code-only graph (277 nodes / 297 edges / 43 communities); outputs and evidence recorded.
- Limitation: no LLM API key on this machine → doc semantic extraction skipped. Re-run `graphify . --update --no-viz` without `--code-only` once a key is set.

## Completed — M00-004 (locked deps + green gates)

- Fixed starter defects so gates could run honestly: added `using Xunit;` to both test projects; renamed underscore test methods for CA1707; pinned TypeScript 7.0.2→6.0.3 (typescript-eslint rejects TS ≥ 7).
- Committed `package-lock.json`; frontend Dockerfile + CI switched to `npm ci`; `.dockerignore` added to both build contexts; manifest script skips runtime `cache` dirs so CI's `--check` can pass on a fresh checkout.
- Verified locally: `dotnet build/format/test` Release all green (tests 3/3); `npm run verify` green; `docker build` for `qasedak-api:verify` and `qasedak-web:verify` green.

## Next task — M01-001

1. Run agent preflight: `python scripts/agent_preflight.py --task M01-001`.
2. Refresh the graph: `graphify . --update --no-viz --code-only`.
3. Research current official Meta/Instagram Graph API capabilities relevant to the MVP (messaging windows, comment triggers, webhook fields, permissions/review). Use `web_search` with citations; do not rely on memory for policy numbers.
4. Write `docs/product/instagram-mvp-capability-matrix.md` (or the location the docs set expects) listing desired automations vs official capability, permission requirements, and open risks.
5. Record Graphify evidence, update state files, regenerate the manifest, then `python scripts/agent_finalize.py --task M01-001 && python scripts/verify.py`.

Suggested commit: `docs(product): define instagram automation mvp capability matrix`
