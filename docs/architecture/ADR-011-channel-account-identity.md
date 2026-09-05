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
6. **Inbound**: bridges resolve the exact `ConnectedAccount` behind the event's
   provider identity (`FindWorkspaceIdByProviderIdentityAsync` then
   `FindByProviderIdentityAsync`), drop unknown/disconnected accounts without
   guessing, and cross into modules with the opaque identity only.
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
