# M08-006 — Inbox and conversations sync

- Canonical and connected file: `c269caa0-e456-818c-8008-89e5136d6851`
- Page: Inbox & Conversations — `c48311ed-e700-80f8-8008-88200ec40bf3`
- Desktop: `c48311ed-e700-80f8-8008-88200ed6b9fc`
- Mobile list: `c48311ed-e700-80f8-8008-88201670e15e`
- Mobile thread: `c48311ed-e700-80f8-8008-88201a3b1157`
- Tablet: `c48311ed-e700-80f8-8008-88201bd8a56d`
- Loading/empty/error states: `c48311ed-e700-80f8-8008-8820201f874b`
- Conversation item component: `c48311ed-e700-80f8-8008-881eaafa74fb`
- Penpot revision: `null`; human page opening: **NO**; programmatic activation: **YES**

Desktop and state boards were structurally inspected and visually exported. The nested
desktop shell/sidebar (`c48311ed-e700-80f8-8008-88200ede730a`) was also inspected to
extract the exact navigation, caret, active-dot, account, help, billing, Instagram and
collapse SVG paths.

Changed implementation paths: `src/features/conversations/api/server.ts`,
`src/features/conversations/ui/{ConversationList,ThreadPanel,ReplyComposer}.tsx`,
`Inbox.module.css`, and both Inbox App Router pages/loading states. Search is omitted
because the list endpoint exposes no search query. Reply remains limited to 1000
characters and succeeds only after the server confirms the outbound action.

Visual evidence covers the real empty state at 1440 × 1000 and 390 × 844. A populated
thread screenshot remains unavailable because the public product API exposes neither a
connected-account creation flow nor a conversation fixture endpoint; no record was
inserted directly or fabricated for evidence.
