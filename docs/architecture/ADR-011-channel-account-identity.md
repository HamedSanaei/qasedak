# ADR-011 — Exact channel-account identity (ChannelAccountId)

- Status: Accepted (M13-002)
- Builds on: ADR-010 (Instagram Login primary); M13-001 identity contract
  (`ConnectedAccount.Id` vs `ChannelAccountId` vs `IG_ID` vs app-scoped user id
  vs `IGSID` vs `mid` vs comment ID — never confused)

## Context

Before M13-002, Qasedak keyed conversations by
`(WorkspaceId, Channel, ParticipantId)` and resolved outbound sends through the
workspace's first active Instagram Login account (`ListByWorkspaceAsync` +
`FirstOrDefault`). Automations were workspace-wide: any account's event could
execute any automation. With two connected accounts in one workspace, threads
merged, sends could leave through the wrong account/token, and automations
cross-executed. That is a correctness and security defect, not a limitation.

## Decision

1. **ChannelAccountId** (`Qasedak.BuildingBlocks.Domain`): opaque,
   channel-neutral, provider-neutral `readonly record struct` over a Guid, with
   `From` (rejects empty) and `TryParse`. It carries no provider type, token or
   host. `ChannelAccountId?` (null) marks legacy/unresolved records.
2. **Mapping ownership**: only the Api composition root maps
   `ConnectedAccount.Id → ChannelAccountId` (same Guid value, distinct type).
   Conversations and Automations (Domain/Application/Infrastructure) never
   reference `Qasedak.Modules.Instagram.*`; Instagram Infrastructure never
   writes their schemas. Verified by `check_architecture.py` (35 projects).
3. **Conversations natural key** is now
   `(WorkspaceId, Channel, ChannelAccountId, ParticipantId)`, enforced by unique
   index `IX_conversations_exact_thread`. Migration
   `20260905000206_AddChannelAccountId` adds a nullable uuid column and replaces
   the old triple unique index. PostgreSQL NULL-distinctness lets any number of
   legacy rows share a triple while exact quadruples stay unique.
4. **Legacy conversation semantics**: NULL = pre-M13-002 row. Readable in
   list/detail (payloads carry `channelAccountId: null`); replies refuse with
   `reply.accountUnresolved` (409); exact inbound never adopts legacy rows — it
   creates a separate exact thread. No account is ever guessed.
5. **Legacy automation semantics**: NULL binding = unbound. `ListByAccountAsync`
   excludes unbound rows, and `ExecuteAutomationUseCase` refuses any request
   whose resolved account differs from the automation's binding (or is
   unresolved) before evaluation and ledger — so unbound automations never
   execute on exact-account events and cross-account execution is impossible.
   Binding is create-time immutable (`automation.bindingImmutable` on PUT
   change attempts); rebinding means creating a new automation (M13-014).
   Migration `20260905000458_AddChannelAccountBinding` is purely additive
   (nullable column + non-unique index).
6. **Inbound**: bridges resolve the exact active `ConnectedAccount` behind the
   event's canonical routing identity in one query
   (`ResolveActiveAccountAsync` → Resolved/NotFound/Ambiguous); unknown,
   disconnected-only and ambiguous identities are dropped without guessing, and
   only a Resolved account crosses into modules as the opaque identity.
7. **Outbound**: `ChannelDeliveryRequest`/`ActionDispatch`/`ExecutionRequest`
   carry `ChannelAccountId?`. `InstagramReplyGateway` resolves by
   `FindByIdAsync`, verifies workspace ownership, connected state and
   InstagramLogin path, then uses only that account's protected token. The
   `FirstOrDefault` fallback is deleted. Failure codes
   (`instagram.accountUnresolved/unknownAccount/accountWorkspaceMismatch/
   accountDisconnected/unsupportedAccountPath/tokenMissing`, plus
   `reply.accountUnresolved`) refuse safely (409) and never select another
   account.

## Rollback compatibility (deployment window vs `sha-6e5b912e4be7`)

- Automations migration: purely additive — old image fully compatible.
- Conversations migration: nullable column add (old image ignores it; old
  inserts default NULL) + unique-index replacement. Old image boots and operates
  normally **until** multi-account data exists: its `SingleOrDefault` triple
  lookup throws if two rows ever share `(Workspace, Channel, Participant)`
  (exact A/B threads, or raced legacy duplicates the old index used to reject).
  Immediate rollback before new multi-account rows are written remains safe.
  Operational rule: before rolling back, check for duplicate triples
  (`GROUP BY WorkspaceId, Channel, ParticipantId HAVING COUNT(*) > 1`
  over `conversations.conversations`); `Down()` additionally fails if such
  duplicates exist, since the old unique index cannot be recreated over them.

## Verification

Two-account isolation (inbound → separate threads; outbound → exact tokens),
foreign-workspace/disconnected/missing-account refusals with no fallback,
automation A/B isolation, legacy migration survival + refusal semantics, and
exact-quadruple uniqueness are proven by unit, Testcontainers PostgreSQL and
API E2E tests (see M13-002 evidence). No live Meta calls anywhere in CI.

## Correction: deterministic inbound resolution (M13-002 correction, 2026-09-05)

**Defect found post-completion:** inbound bridges used a two-step
`FindWorkspaceId…` → `FindByProviderIdentity…` chain of order-dependent
`FirstOrDefault` matches over rows that include disconnected history. An older
disconnected row could shadow a reconnected active account (same provider
identity), and one routing identity active in two workspaces resolved to
whichever row the database returned first. "Exact account routing" was therefore
not guaranteed.

**Identity verdict (Outcome A, first-party Meta evidence):** for Instagram Login
the OAuth code-exchange `user_id` IS the professional IG_ID carried by webhook
`entry.id` — proven by the Business Login page (user_id = "Instagram-scoped
user ID") chained with the Get Started guide (`/me` "represents the app user's
ID received from the access token"; `/me?fields=user_id` returns "the Instagram
professional account ID, IG_ID... This ID is value of the `id` field received
in webhook notifications"). No mapping step exists in the official flow. The
stored `ConnectedAccount.ProviderUserId` is therefore already the canonical
routing identity; no second column was created. Misleading "app-scoped" labels
were corrected to IG_ID terminology.

**Final routing model:** webhook `entry.id` (surfaced as
`ProviderAccountId` on integration events) → one-query
`ResolveActiveAccountAsync` over active rows only →
Resolved(account)/NotFound/Ambiguous → `WorkspaceId` from the resolved account
→ `ChannelAccountId.From(account.Id)`. No intermediate workspace selection, no
"first", no row-order dependence.

**Reconnect semantics:** disconnect-then-reconnect creates a history row plus a
new active row with the same routing identity; resolution ignores disconnected
rows in any physical order, so the new active account always wins (proven by
reconnect E2E + insertion-order repository tests).

**Ownership policy:** one professional account has one inbox, so one workspace
may actively own a routing identity at a time. Enforced at connect time
(`account.alreadyConnectedElsewhere`, 409); duplicate active owners can only
arise from legacy data or races and resolve as Ambiguous → dropped with an
observable warning, never routed. A database-level global partial-unique index
is deferred until production duplicate state can be audited (M13-005); the
resolver's non-unique partial index
(`IX_connected_accounts_active_routing_identity`) backs the hot path.

**Legacy handling:** no new legacy class was introduced — pre-correction rows
already carry the canonical identity, so existing active accounts route
correctly under the corrected resolver with no repair flow and no fabricated
ids (migration `20260905015456_AddActiveRoutingIdentityIndex` is a purely
additive non-unique index; old image ignores it).
