# M08-006 visual review

Reviewed on 2026-08-29 against the live Penpot source file
`c269caa0-e456-818c-8008-89e5136d6851` using the production Docker image and a
disposable account/workspace created through the public web flow. No product
metrics, connected accounts, conversations, subscription data, or success states
were seeded in source code.

## Captured routes

Both 1440 × 1000 and 390 × 844 evidence exists for:

- `/dashboard`
- `/dashboard/inbox` (real empty state)
- `/dashboard/automations`
- `/dashboard/automations/new`
- `/dashboard/settings/instagram`
- `/dashboard/billing`
- `/dashboard/accounts`
- `/dashboard/help`

Additional evidence covers login, registration, the reusable user menu, the
collapsed desktop sidebar, and the mobile drawer. The Inbox list/thread source is
mapped to Penpot board `c48311ed-e700-80f8-8008-88200ed6b9fc`; no real thread
could be created because the public product API exposes neither Instagram account
connection nor a conversation fixture endpoint.

## Responsive and runtime checks

`responsive-review.json` records 1440, 1280, 1024, 768, 390, and 360 px checks.
Every width had `scrollWidth === innerWidth`; the desktop sidebar is present at
1024 px and above, and the accessible drawer trigger replaces it below 1024 px.
`runtime-issues.json` is empty after the final route pass.
