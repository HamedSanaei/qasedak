# Decision log

| ADR | Decision | Status |
|---|---|---|
| ADR-001 | Modular Monolith with Clean Architecture inside each module | Accepted |
| ADR-002 | Backend and Next.js frontend remain separate and publish separate images | Accepted |
| ADR-003 | PostgreSQL starts as one physical database with module-owned schemas | Accepted |
| ADR-004 | Graphify is mandatory for AI-agent repository navigation | Accepted |
| ADR-005 | Penpot is the UI design source of truth; approved screens are implemented in Next.js | Accepted |
| ADR-006 | Billing is provider-neutral until a payment provider is selected by ADR (M09-002 BLOCKED on that decision); entitlements are server-owned and fail closed | Accepted |
| ADR-007 | M08 screen tasks require a live Penpot MCP connection; with the plugin disconnected they are BLOCKED, never implemented from imagination | Accepted |
| ADR-008 | Payments: provider-neutral `IPaymentGateway`; Zarinpal implemented over the CURRENT official REST contract; Bank Melli/SADAD boundary-only (fail-closed) until the official merchant technical contract exists; canonical currency IRR; exactly-once entitlement via DB uniqueness + xmin concurrency | Accepted — provider selection superseded by ADR-009 |
| ADR-009 | v1 payment providers = Zarinpal (live) + Behpardakht Mellat (live transport implemented against the human-supplied vendor reference `docs/vendor/behpardakht/BEHPARDAKHT-IPG-v1.29-EN.md`, IPG User Guide v1.29 EN translation, "Unofficial - External" provenance preserved; newer conflicting onboarding docs ⇒ future ADR); Bank Melli/SADAD CANCELLED and removed from active scope; architecture/currency/exactly-once guarantees unchanged | Accepted — transport completed 2026-08-24 |
| ADR-010 | Production database migrations run explicitly by `dotnet Qasedak.Api.dll --migrate` from the exact API release image; normal API startup never silently migrates; all seven contexts target one physical PostgreSQL with module-owned schemas | Accepted — M12-001 |
| ADR-011 | Production delivery is CI-gated: CI success triggers immutable SHA images, then the deployment workflow transfers only deployment artifacts over pinned SSH and runs a flock-protected remote deploy; binary rollback never destroys or automatically rolls back PostgreSQL data | Accepted — M12-001 |
| M12-004 | `C:\Users\Hamed\Documents\Qasedak` is the canonical GitHub-connected working clone; the Python clone is retained only as a byte-for-byte recovery archive after selective merge | Accepted — 2026-08-30 |

## Open decisions requiring human input

None currently. The former open decision "Behpardakht Mellat current official merchant documentation" was resolved when the vendor reference `docs/vendor/behpardakht/BEHPARDAKHT-IPG-v1.29-EN.md` entered the repo and the live transport shipped against it (see ADR-009). Remaining Mellat items are operational go-live prerequisites, not architectural decisions: real terminal credentials, Shaparak registration of the deployment host, and a staging smoke test (documented in docs/08 §6).

New architectural decisions require an ADR when they change boundaries, dependencies, persistence ownership, runtime topology, security model or externally visible contracts.
