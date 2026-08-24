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
| ADR-008 | Payments: provider-neutral `IPaymentGateway`; Zarinpal implemented over the CURRENT official REST contract; Bank Melli/SADAD boundary-only (fail-closed) until the official merchant technical contract exists; canonical currency IRR; exactly-once entitlement via DB uniqueness + xmin concurrency | Accepted |

## Open decisions requiring human input

1. **Bank Melli/SADAD official merchant documentation** (blocks ONLY the live Melli transport): supply the official technical integration documents associated with the project's Bank Melli account — endpoint specification, signature/encryption algorithm spec, terminal/merchant credential contract, callback field contract. Everything else in payments is complete.
2. **Penpot MCP connection** (blocks the visual reconciliation of Qasedak Auth, Inbox and the new Billing UI screens): connect the Penpot plugin so live pages/boards can be read through MCP before screen work.

New architectural decisions require an ADR when they change boundaries, dependencies, persistence ownership, runtime topology, security model or externally visible contracts.
