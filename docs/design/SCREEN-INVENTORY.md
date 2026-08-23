# Screen inventory

Human-readable roll-up of the Penpot ↔ Next.js mapping. The machine-readable source of
truth is `frontend/Qasedak.Web/design/penpot-sync.json`; the workflow contract is
`docs/design/PENPOT-SYNC.md`; per-sync evidence lives in `docs/design/sync/`.

| Area | Screen | Penpot page / board (IDs in manifest) | Status | Next.js route | Sync status | Task |
|---|---|---|---|---|---|---|
| Design system | Navigation / Sidebar | Global Navigation Components / "Navigation / Sidebar" | provisional | `/dashboard` shell | synced (M05-005) | M05-005 |
| Public | Landing page | TBD | not designed | `/` | not-mapped | M08 |
| Identity | Sign in | TBD | not designed | `/login` | not-mapped | M08-002 |
| Workspace | Dashboard content | Admin Dashboard / "Dashboard — Directam Reference" | reference surveyed | `/dashboard` (content) | pending sync | M08 |
| Instagram | Connected accounts | Connect Instagram / "Connect to Instagram — Desktop" | reference surveyed | `/dashboard/settings/instagram` | pending sync | M08-003 |
| Inbox | Conversations | TBD | not designed | `/dashboard/inbox` | not-mapped | M08-004 |
| Automations | Automation list | Comment Automation / "Comment Automation — List" | reference surveyed | `/dashboard/automations` | pending sync | M08-005 |
| Automations | Flow builder | Comment Automation / "Comment Automation — New" | reference surveyed | `/dashboard/automations/[id]` | pending sync | M08-005 |
| Billing | Subscription | Pricing & Components (surveyed) | reference surveyed | `/dashboard/billing` | pending sync | M09 |
