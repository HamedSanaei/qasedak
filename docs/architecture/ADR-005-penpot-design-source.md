# ADR-005 — Penpot is the UI design source of truth

- Status: Accepted
- Date: 2026-08-23

## Decision

User-facing screens are designed/approved in Penpot before implementation. Next.js implementation must preserve approved layout, tokens, responsive states and interaction intent while still meeting accessibility, performance and application architecture requirements.

## Consequence

Generated design code is never pasted blindly. The implementation is production Next.js composed from reusable components and feature modules, with Penpot references recorded in the design handoff.
