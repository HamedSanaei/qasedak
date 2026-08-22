# ADR-004 — Graphify is mandatory agent context infrastructure

- Status: Accepted
- Date: 2026-08-23

## Context

Multiple AI agents will work on Qasedak over many days. Re-reading the full repository wastes tokens and increases the chance of stale or inconsistent reasoning.

## Decision

Require Graphify before broad code navigation. Agents refresh/query the graph, record evidence, and keep project state in repository files. Graphify failure blocks feature edits unless a human explicitly authorizes and records a bypass.

## Verification

`AGENTS.md`, `.agents/rules/GRAPHIFY.md`, `scripts/agent_preflight.py`, `scripts/record_graphify_evidence.py`, and `scripts/agent_finalize.py` enforce the workflow socially and mechanically where possible.
