# M08-004 — Conversation inbox (functional UI shipped; visual sync BLOCKED — missing design)

**Date:** 2026-08-24 · **Canonical file:** `c269caa0-e456-818c-8008-85a77340be64`

## BLOCKED portion — exact missing-design documentation

**No inbox/conversation design exists in the canonical Penpot file.** Evidence: a full
sweep of all 24 pages of file `c269caa0-e456-818c-8008-85a77340be64` was performed during
this milestone (page inventory captured via `penpotUtils.getPages` + per-page board
enumeration). No board, frame, or component named or describing an inbox, conversation
list, thread view, DM view, or reply composer exists on any page — including the Directam
reference pages, GetCode auth/checkout pages, admin dashboard boards, and Page 1.
Per the milestone directive §9 this portion is marked BLOCKED with the exact gap; no
design values were invented and **no manifest mapping was created** (there is no design
source to map). Unblocking requires a human-approved inbox design added to the canonical
file, after which the screen can be re-synced against `docs/design/sync/M08-004-conversation-inbox.md`.

## What WAS delivered (everything not blocked by the missing design)

The backend conversations API already exists (`ConversationEndpoints`, workspace-member
policy). Implemented the functional product surface using only approved foundation
tokens/primitives (no new visual language):

- `/dashboard/inbox` — filterable list (همه/باز/در انتظار), status + unread pills,
  relative fa-IR timestamps, empty/error/loading states.
- `/dashboard/inbox/[conversationId]` — message thread (direction-aware bubbles),
  reply composer with client-side empty/too-long validation mirroring backend rules,
  Persian copy for every stable failure code (`conversation.notFound`, `reply.*`,
  `channel.unsupported`, `instagram.noConnectedAccount`, `instagram.tokenMissing`).
- `src/shared/api/conversations.ts` typed client over GET list / GET detail / POST replies.

## Tests/gates

Frontend 26/26 (+4: status/failure-copy coverage, reply validation parity, deterministic
fa relative-time formatting, endpoint contract w/ injected transport); typecheck pass;
architecture pass. Backend untouched in this task.
