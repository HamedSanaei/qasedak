# Screen inventory

Human-readable roll-up of the Penpot ↔ Next.js mapping. The machine-readable source of
truth is `frontend/Qasedak.Web/design/penpot-sync.json`; the workflow contract is
`docs/design/PENPOT-SYNC.md`; per-sync evidence lives in `docs/design/sync/`.

| Area | Screen | Penpot page / board (IDs in manifest) | Status | Next.js route | Sync status | Task |
|---|---|---|---|---|---|---|
| Design system | Navigation / Sidebar | Global Navigation Components / "Navigation / Sidebar" (`f5bf3c2c-…-8752c6768b24` group) | provisional | `/dashboard` shell | synced (M05-005) | M05-005 |
| Public | Landing page | TBD — candidate `Directam Landing — Desktop` `f6b8d46f-…-85ab43d94e44` on Page 1 | not designed | `/` | not-mapped | M08 |
| Identity | Sign in / Register / Workspace creation | GetCode · 05 Auth & Checkout / "Auth / Login / Desktop" `324404a7-…-8776b27352cb` (+ OTP Mobile `…8776b3100eb1`) — **GetCode-branded divergence documented** | draft | `/login`, `/register` | synced (M08-002, draft basis) | M08-002 |
| Workspace | Dashboard content | Admin Dashboard / "Dashboard — Directam Reference" `f5bf3c2c-…-8744692773fc` + states board `…8747843b4ad6` | reference surveyed; sidebar live-synced | `/dashboard` (content) | pending sync | M08 |
| Instagram | Connected accounts | "Connect to Instagram — Desktop" `f5bf3c2c-…-874ac4b51953` + "Profile — Connected Accounts" `…874a8c53c34c` | approved | `/dashboard/settings/instagram` | synced (M08-003) | M08-003 |
| Inbox | Conversations + thread | **NO DESIGN EXISTS** — all 24 pages of canonical file swept during M08-004; no inbox/conversation/DM board anywhere | functional UI shipped on foundation tokens; visual sync BLOCKED (missing design) | `/dashboard/inbox`, `/dashboard/inbox/[conversationId]` | blocked (no design source to map) | M08-004 |
| Automations | Automation list | Comment Automation / "Comment Automation — List" `f5bf3c2c-…-874ebb85c7c2` | approved | `/dashboard/automations` | synced (M08-005) | M08-005 |
| Automations | Flow builder | "Comment Automation — New" `f5bf3c2c-…-874ec2cb62fb` + "Smart Answering — Component States" `…8747843b4ad6` | approved (divergences documented: 1000-char counter, post-scoping disabled in v1) | `/dashboard/automations/new`, `/dashboard/automations/[automationId]` | synced (M08-005) | M08-005 |
| Billing | Subscription | Pricing & Components (surveyed) | reference surveyed | `/dashboard/billing` | pending sync | M09 |
