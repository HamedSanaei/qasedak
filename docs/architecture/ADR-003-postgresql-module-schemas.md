# ADR-003 — PostgreSQL module-owned schemas

- Status: Accepted
- Date: 2026-08-23

## Decision

Start with one PostgreSQL physical database. Each business module owns a logical schema (for example `identity`, `instagram`, `automations`, `conversations`, `contacts`, `billing`). A module must not query another module's tables directly.

## Consequences

Operations remain simple while ownership is explicit. Future extraction can use established boundaries rather than reconstructing them from a shared table model.
