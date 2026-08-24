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

## Open decisions requiring human input

1. **Payment provider selection** (blocks M09-002): choose the provider and record an ADR covering legal/deployment fit, webhook reachability, pricing model.
2. **Penpot MCP connection** (blocks M08-001..005): connect the Penpot plugin so live pages/boards/components can be read through MCP before any screen work.

New architectural decisions require an ADR when they change boundaries, dependencies, persistence ownership, runtime topology, security model or externally visible contracts.
