# M08-000 — Inbox and conversations design

- Canonical/connected file UUID:
  `c269caa0-e456-818c-8008-85a77340be64` — PASS
- Requested mappings: `conversations.inbox`, `conversations.thread`
- Was target page manually opened by human? **NO**
- Was page activated programmatically? **YES**
- Penpot revision: `null`

## Stable targets

Page `Qasedak · Inbox & Conversations`:
`c48311ed-e700-80f8-8008-88200ec40bf3`

| Board | Stable UUID |
|---|---|
| Conversations / Inbox / Desktop | `c48311ed-e700-80f8-8008-88200ed6b9fc` |
| Conversations / Inbox / Mobile | `c48311ed-e700-80f8-8008-88201670e15e` |
| Conversations / Thread / Mobile | `c48311ed-e700-80f8-8008-88201a3b1157` |
| Conversations / Inbox / Tablet | `c48311ed-e700-80f8-8008-88201bd8a56d` |
| Conversations / Product States | `c48311ed-e700-80f8-8008-8820201f874b` |

Reusable Conversation Item component:
`c48311ed-e700-80f8-8008-881eaafa74fb`.

## Coverage

Desktop includes the approved sidebar instance, conversation list, status filters,
read/unread treatment, selected conversation, inbound/outbound messages, timestamps,
composer, 1000-character counter, disabled/send states and a context panel.

Responsive behavior is intentional: tablet keeps list/thread side by side at 834px;
mobile has separate list and thread navigation states at 390px.

The state gallery covers list loading, no conversations, no search results, thread
loading, send failure/retry, closed 24-hour messaging window, disconnected Instagram,
permission error, generic server error and read/unread examples.

## Backend alignment and divergence

Current Conversations endpoints support workspace-scoped pagination/status filtering,
participant ID, channel, status, unread count, message direction/body/timestamps and
text replies. Replies are limited to 1000 characters and a 24-hour inbound-message
window.

- Search is designed but visibly disabled until a backend query exists.
- Contact name, tags and notes are visibly future/disabled until M07 supplies them.
- The current context panel therefore uses only participant ID, channel/account state
  and recent message context.
- Archive/mark-read controls were not promised because no current HTTP endpoint exposes
  those mutations.

## Implementation expectation

- `/dashboard/inbox` → `src/features/conversations/ui/InboxScreen.tsx`
- `/dashboard/inbox/[conversationId]` →
  `src/features/conversations/ui/ThreadScreen.tsx`

Both are `planned`; M08-004 owns implementation and data behavior.

## Validation

Desktop plus both mobile boards were exported and visually inspected. Live stable
page/board/component resolution and offline `conversations.inbox` lookup passed.
