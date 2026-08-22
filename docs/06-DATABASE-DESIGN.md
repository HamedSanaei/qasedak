# Qasedak — Database Design Document

## 1. Database strategy

Qasedak begins with PostgreSQL 18 as one physical database for simple operations and transactional locality. Business ownership is preserved through separate logical schemas rather than a single public schema shared indiscriminately.

Planned schema ownership:

| Schema | Owner module | Example future data |
|---|---|---|
| `identity` | Identity | users, workspaces, memberships, auth metadata |
| `instagram` | Instagram | connected accounts, protected token metadata, webhook inbox |
| `conversations` | Conversations | conversations, messages, projections |
| `automations` | Automations | definitions/versions/executions/effects |
| `contacts` | Contacts | contacts, social identities, tags, notes |
| `billing` | Billing | plans/subscriptions/entitlements/provider events |

Table names are not frozen by this starter; each implementation milestone validates actual access patterns/invariants first.

## 2. Ownership rules

- Only the owning module Infrastructure creates/migrates its schema/tables.
- A module must not query/update another module's tables directly.
- Cross-module references should use stable IDs plus explicit application/integration contracts.
- Cross-schema foreign keys or database views require an ADR because they create coupling that can defeat module boundaries.
- Shared technical tables are avoided unless the ownership model is unambiguous.

## 3. Identity and timestamps

UUIDv7 is the default candidate for server-created internal identifiers because it is globally unique and time-ordered, but each aggregate may choose a stronger value-object ID. PostgreSQL timestamps representing instants use `timestamptz` and application code treats them as UTC instants. User/business time zones are separate presentation/business concepts.

## 4. Candidate logical data model

The following is a planning model, not generated migrations:

- Identity: workspace, user, membership, role/permission assignment/audit metadata.
- Instagram: connection, external account identity, protected credential/token metadata, webhook delivery/inbox, provider subscription/health.
- Conversations: conversation, participant/external identity reference, message, delivery/reply status.
- Automations: automation root, immutable/versioned definition, execution, action/effect attempt/dedup identity.
- Contacts: workspace contact, social identity mapping, tag relation, note.
- Billing: plan/version, subscription, entitlement snapshot/rule, provider event inbox.

## 5. Idempotency and event processing

Webhook/provider delivery is at least once. The database must support a unique provider/event/delivery identity or another verified deduplication key. Accepting a webhook should durably preserve enough state before acknowledging when subsequent processing can fail.

Automation side effects need their own effect identity. A webhook inbox uniqueness check alone is insufficient because an internal retry may occur after partial processing. M04/M06 must define the exact inbox/outbox/effect ledger implementation and transactional boundaries.

## 6. Indexing and query design

Indexes are created from measured/known query paths rather than blanket indexing every foreign key/string. Expected hot dimensions include workspace ownership, external connection/account identifiers, provider event IDs, conversation recency, automation active/trigger selection and execution status. Query plans should be inspected for production-like data before release.

## 7. Security

- Database credentials are injected at runtime and scoped to the application/environment.
- Meta credentials/tokens require application-level protection/encryption at rest before M03 completion; plaintext secrets are not an acceptable production baseline.
- Backups inherit the sensitivity of production data and must be encrypted/access-controlled.
- Logs/migration scripts must not print secrets.
- Consider database roles/permissions by runtime/migration responsibility as production operations mature.

## 8. Migrations

Migrations are module-owned and applied in a controlled pre-deploy/startup process selected before production. Application instances must not race uncontrolled schema changes. Destructive changes use expand/migrate/contract patterns when zero/low-downtime compatibility is required.

Every migration task requires a test against a real PostgreSQL container and an explicit rollback/recovery assessment. Backward-incompatible changes require coordinated deployment sequencing.

## 9. Testing

Persistence semantics are tested with PostgreSQL Testcontainers rather than an in-memory fake when behavior depends on constraints, transactions, JSON, timestamp handling, indexes or database concurrency. Integration test data must be isolated/repeatable and must not depend on a developer's local database.

## 10. Backup and recovery

Before M11, operations must demonstrate automated backup, retention, integrity checking, restore into a clean environment and application smoke verification. Recovery-point/recovery-time objectives are defined from actual business requirements rather than guessed in this scaffold.
