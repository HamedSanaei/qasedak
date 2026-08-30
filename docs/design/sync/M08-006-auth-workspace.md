# M08-006 — Identity and workspace sync

- Canonical and connected file: `c269caa0-e456-818c-8008-89e5136d6851`
- Page: Identity & Workspace — `c48311ed-e700-80f8-8008-881f0352eb6a`
- Login desktop: `c48311ed-e700-80f8-8008-881f0372388a`
- Register desktop: `c48311ed-e700-80f8-8008-881f075bc2f7`
- Login mobile: `c48311ed-e700-80f8-8008-881f0b6a5a33`
- Register mobile: `c48311ed-e700-80f8-8008-881f0cbbe326`
- Validation/session/workspace states: `c48311ed-e700-80f8-8008-881f0ea618ba`
- Penpot revision: `null`; human page opening: **NO**; programmatic activation: **YES**

The desktop login and product-state boards were structurally inspected and exported;
desktop/mobile auth boards were live-inspected before implementation. `AuthShell.tsx`,
`AuthForms.tsx`, `WorkspaceOnboarding.tsx`, `/login`, `/register`, and
`/onboarding/workspace` now use real Identity/Workspace requests through same-origin web
handlers. The web server owns the HttpOnly session and workspace cookies. The active M12
feature screens still receive the short-lived access token from the same auth response for
their legacy bearer-client compatibility bridge (the `/api/v1` proxy prefers the cookie);
this is an explicitly temporary migration seam, not the server-side authorization source.
Web-owned handlers use `/web-api/*` because production reserves `/api/*` for the ASP.NET
Core reverse-proxy target.

Visual evidence: `artifacts/visual-review/M08-006/screenshots-final/desktop-login.png` and
`desktop-register.png` at 1440 × 1000. Narrow auth behavior follows the inspected 390 px
boards. Remaining difference: backend error rules and password constraints are
application-owned and rendered through the Penpot status-alert treatment.
