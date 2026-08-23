# Current handoff

## Where we are

Milestones M00 (engineering foundation), M01 (Meta/Instagram feasibility & contracts) and M02 (identity & workspace core) are complete. The repository builds/tests green (112 backend tests), dependencies are locked, both images build, Graphify is healthy in code-only mode, and all Meta integration decisions remain documented with citations and ADRs. Identity now has a real domain, real persistence and real HTTP authorization.

## Completed — M02 summary

- **M02-001:** Identity Domain — `User`/`EmailAddress` (canonical, ≤320), `Workspace` aggregate with `Membership`s and `MembershipRole` (Owner/Admin/Member); invariants: owner-seeded creation, ownerless-state rejection, no duplicate memberships, admin cannot touch Owner roles, last-owner demotion/removal blocked, self-source-only ownership transfer. 47 domain unit tests.
- **M02-002:** Authentication use cases + security contracts in Identity Application (`RegisterUserUseCase`, `AuthenticateUserUseCase`, password policy, stable `auth.*` failure codes, dummy-hash timing equalizer for unknown emails); Infrastructure adapters PBKDF2-SHA256 (210k iterations, per-hash salt, constant-time verify) and HMAC-SHA256 compact token issuer/validator. 32 unit tests.
- **M02-003:** Persistence — `IdentityDbContext` owns the `identity` schema (`users` unique email, `user_credentials`, `workspaces`, `memberships` unique (workspace, user), value conversions for value objects, field-backed memberships navigation); `EfUserRepository`/`EfWorkspaceRepository`; design-time factory; committed migration `InitialIdentityCreation`; 5 Testcontainers PostgreSQL 18 integration tests (roundtrip, uniqueness, cascade). `Microsoft.EntityFrameworkCore.Relational` pinned to 10.0.11 in CPM.
- **M02-004:** Server-boundary authorization — `SecurityTokenAuthenticationHandler` ("QasedakBearer"); endpoints `POST /api/v1/identity/register|login`, `GET me`, `POST /api/v1/workspaces`, `GET /api/v1/workspaces/{id}/members` with 401/403/404 negative paths; token-key config resolved per use (unconfigured hosts boot, fail loudly on first token op). 7 API integration tests against real host + PostgreSQL.

## Next task — M03-001

1. `python scripts/agent_preflight.py --task M03-001`; refresh graph (`graphify . --update --no-viz --code-only`).
2. Bounded graphify query on the Instagram module's Meta integration seams; record evidence.
3. Implement the Meta OAuth adapter per ADR-006 and `docs/product/meta-oauth-token-lifecycle.md`: Business Login for Instagram (`www.instagram.com/oauth/authorize` → code → short-lived token → 60-day long-lived via `graph.instagram.com/access_token` grant_type=ig_exchange_token), refresh via `refresh_access_token`; scopes `instagram_business_{basic,content_publish,manage_messages,manage_comments}`; everything behind ports with deterministic HTTP fixtures — CI must never call live Meta APIs.
4. Resolve open questions OQ-1..3 routed to M03 (see M01-002 doc) or record decisions in ADRs.
5. Gates: build/format/test green; record evidence; update state files; finalize; continue M03.

Suggested commit for the milestone: `feat(identity): model workspace membership domain` covers M02 as a whole (per-task messages are in `docs/project/TASKS.md`); the next milestone's default message lives in its TASKS.md entry.
