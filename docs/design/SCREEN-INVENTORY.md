# Screen inventory

Human-readable roll-up of the Penpot ↔ Next.js mapping. The machine-readable source of
truth is `frontend/Qasedak.Web/design/penpot-sync.json`; the workflow contract is
`docs/design/PENPOT-SYNC.md`; per-sync evidence lives in `docs/design/sync/`.

| Area | Screen | Penpot page / board (IDs in manifest) | Status | Next.js route | Sync status | Task |
|---|---|---|---|---|---|---|
| Design system | Navigation / Sidebar | Global Navigation Components / "Navigation / Sidebar" (`f5bf3c2c-…-8752c6768b24` group) | provisional | `/dashboard` shell | synced (M05-005) | M05-005 |
| Public | Landing page | TBD — candidate `Directam Landing — Desktop` `f6b8d46f-…-85ab43d94e44` on Page 1 | not designed | `/` | not-mapped | M08 |
| Identity | Sign in / Register / Workspace creation | Qasedak · Identity & Workspace / "Identity / Login / Desktop" `c48311ed-…-881f0372388a` (+ Register Desktop/Mobile + Validation/Session/Workspace States boards) — final Qasedak-native design | **approved** (was draft on GetCode reference) | `/login`, `/register` | synced (2026-08-24 reconciliation; behavior preserved) | M08-002 → 2026-08-24 sync |
| Workspace | Dashboard content | Admin Dashboard / "Dashboard — Directam Reference" `f5bf3c2c-…-8744692773fc` + states board `…8747843b4ad6` | reference surveyed; sidebar live-synced | `/dashboard` (content) | pending sync | M08 |
| Instagram | Connected accounts | "Connect to Instagram — Desktop" `f5bf3c2c-…-874ac4b51953` + "Profile — Connected Accounts" `…874a8c53c34c` | approved | `/dashboard/settings/instagram` | synced (M08-003) | M08-003 |
| Inbox | Conversations + thread + product states | Qasedak · Inbox & Conversations / "Conversations / Inbox / Desktop" `c48311ed-…-88200ed6b9fc` (+ Tablet/Mobile/Thread/Product States boards) | **approved** — supersedes the old "NO DESIGN EXISTS" blocker (historical evidence preserved in docs/design/sync/M08-004-*) | `/dashboard/inbox`, `/dashboard/inbox/[conversationId]` | synced (2026-08-24 reconciliation; search disabled by design until backend query ships) | M08-004 → 2026-08-24 sync |
| Automations | Automation list | Comment Automation / "Comment Automation — List" `f5bf3c2c-…-874ebb85c7c2` | approved | `/dashboard/automations` | synced (M08-005) | M08-005 |
| Automations | Flow builder | "Comment Automation — New" `f5bf3c2c-…-874ec2cb62fb` + "Smart Answering — Component States" `…8747843b4ad6` | approved (divergences documented: 1000-char counter, post-scoping disabled in v1) | `/dashboard/automations/new`, `/dashboard/automations/[automationId]` | synced (M08-005) | M08-005 |
| Billing | Plans / subscription / checkout / payment results | Qasedak · Billing & Payments / "Billing / Plans / Desktop" `c48311ed-…-8820a7020aa1` (+ Current Subscription, Checkout, Payment Results, Checkout Mobile boards) | approved | `/dashboard/billing`, `/dashboard/billing/checkout`, `/dashboard/billing/result` | synced (2026-08-24; server-authoritative amounts, provider radios with Melli disabled pending official contract) | M09-002 UI |
