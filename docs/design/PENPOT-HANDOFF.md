# Penpot → Next.js handoff contract

Every implemented screen must have a bounded handoff entry before coding.

## Required fields

- Penpot file/project reference
- exact page/frame/component reference
- design status: draft / approved / superseded
- desktop and mobile targets
- typography, spacing, color and radius tokens
- reusable component inventory and variants
- state matrix: loading, empty, error, success, disabled, permission-denied where applicable
- responsive behavior and breakpoints
- accessibility notes: landmarks, labels, keyboard/focus order, contrast
- API/data dependencies
- screenshots/review evidence if available

## Implementation rules

- Penpot expresses visual/interaction intent, not application architecture.
- Do not paste generated HTML/CSS as production architecture.
- Prefer reusable React/Next.js components and feature-local composition.
- UI must not contain server authorization/business rules.
- Add tests for important user behavior and regression-prone states.
