# ADR-002 — Separate backend and frontend images

- Status: Accepted
- Date: 2026-08-23

## Decision

Keep ASP.NET Core and Next.js as independent source/runtime applications. Build immutable `qasedak-api` and `qasedak-web` images and deploy them together through one Compose stack.

## Rationale

A single multi-process container would couple Node and ASP.NET lifecycle/health/scaling. Separate images preserve isolation while retaining one-command deployment.
