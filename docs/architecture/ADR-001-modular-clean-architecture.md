# ADR-001 — Modular Monolith with Clean Architecture per module

- Status: Accepted
- Date: 2026-08-23

## Context

Qasedak contains several business capabilities that will grow independently: identity/workspaces, Instagram integration, conversations, automations, contacts and billing. A repository-wide Domain/Application split would spread each capability across too many global folders as the codebase grows.

## Decision

Use a Modular Monolith. Each business module owns Domain, Application and Infrastructure projects. Dependencies point inward and direct business-module project references are forbidden. API is the composition root.

## Consequences

Business capabilities remain locally understandable while compiler/project-reference boundaries preserve Clean Architecture. Cross-module interaction requires explicit contracts/events and may add small coordination overhead.
