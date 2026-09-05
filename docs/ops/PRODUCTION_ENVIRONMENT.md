# Qasedak production environment contract (v1 baseline)

Status: **contract frozen for v1** — this document is normative for any deployment.
Every required input is listed with source of truth and failure behavior. The automated
checker `python scripts/check_environment_contract.py` validates that this document stays
in sync with what the code actually reads.

## 1. Runtime topology

| Component | Image | Notes |
|---|---|---|
| API | `qasedak-api` | ASP.NET Core modular monolith; all modules in one process |
| Web | `qasedak-web` | Next.js standalone output; static assets served by Node |
| Database | `postgres:18-alpine` or managed equivalent | One physical database, module-owned schemas |

One physical PostgreSQL instance hosts all module schemas (`identity`, `instagram`,
`conversations`, `automations`, `contacts`, `billing`, `audit`, `platform`). Splitting to separate
databases later is an ADR-level change.

## 2. Connection strings (required)

All eight are **mandatory for the production deployment contract**. Missing/unreachable
 databases fail migration and startup health probes (`/health/ready`), never silently degrade.
All eight may safely point to the same physical PostgreSQL database; schemas remain module-owned.

| Key | Used by | Schema | Notes |
|---|---|---|---|
| `ConnectionStrings:Identity` | IdentityDbContext | `identity` | users, credentials, workspaces, memberships |
| `ConnectionStrings:Instagram` | InstagramDbContext | `instagram` | connected accounts, webhook log |
| `ConnectionStrings:Conversations` | ConversationsDbContext | `conversations` | inbox threads/messages |
| `ConnectionStrings:Automations` | AutomationsDbContext | `automations` | definitions, runs |
| `ConnectionStrings:Contacts` | ContactsDbContext | `contacts` | contacts, identities, tags, notes, ledger |
| `ConnectionStrings:Billing` | BillingDbContext | `billing` | plans, entitlements, subscriptions, periods |
| `ConnectionStrings:Audit` | AuditDbContext | `audit` | append-only audit trail (required by the production deployment contract) |
| `ConnectionStrings:Platform` | ScheduledWorkDbContext | `platform` | durable scheduled-work records (required since M13-004; dispatcher no-ops without handlers) |

## 3. Application settings (required unless marked optional)

| Key | Purpose | Failure behavior if missing |
|---|---|---|
| `Identity:Auth:TokenSigningKey` | HMAC signing key for bearer tokens | tokens cannot be issued/validated — auth fails closed; MUST be ≥ 32 bytes of entropy, stored as a secret |
| `Identity:Auth:TokenLifetimeHours` | access-token lifetime | default applies (see code) |
| `Instagram:Meta:AppSecret` | HMAC verification of Meta webhook payloads | webhooks rejected — ingest dead but API healthy |
| `Instagram:Meta:VerifyToken` | hub.subscription handshake echo | subscription verification fails |
| `Instagram:Meta:GraphHost` | versioned Graph API host (Instagram Login path) | optional; defaults to `https://graph.instagram.com` |
| `Instagram:Meta:ApiVersion` | Graph API version segment for versioned paths | optional; defaults to the M13-001-observed `v26.0`; OAuth token endpoints stay unversioned by contract |
| `Instagram:Meta:TimeoutSeconds` | per-request timeout for Graph calls | optional; defaults to 100 (previous HttpClient behavior) |
| `Instagram:Protection:KeyBase64` | exactly-32-byte key encrypting stored Meta tokens | account connections unusable; rotate via re-connect flow |
| `Cors:AllowedOrigins` | browser origins allowed by API | optional; empty = same-origin only |
| `Qasedak:RateLimits:{Public,Authenticated,Webhook,Sensitive}:{Limit,WindowSeconds}` | abuse-control budgets | optional; defaults apply (240/600/2000/30 per minute) |
| `Platform:ScheduledWork:{PollIntervalSeconds,BatchSize,LeaseSeconds,MaxAttemptsDefault,BackoffBaseSeconds,BackoffMaxSeconds}` | durable scheduled-work poll/lease/retry policy | optional; defaults apply (30/10/300/8/30/3600) |
| `ASPNETCORE_ENVIRONMENT` | host environment | must be `Production` in real deployments |

## 4. Secrets policy

- Secrets come from the orchestrator's secret store (env injection); never baked into
  images, never committed. `.env` files are for local development only.
- Rotation expectations: signing keys rotate by dual-key overlap window ≤ 24h;
  `Instagram:Protection:KeyBase64` rotation requires reconnecting accounts (documented
  user-visible cost).
- The audit trail records fingerprints, not secrets; log pipelines need no redaction for
  audit rows but application logs still follow least-disclosure.

## 5. Probes

| Path | Meaning | Wiring guidance |
|---|---|---|
| `/health/live` | process is up (no dependency checks) | liveness: restart on repeated failure |
| `/health/ready` | process can serve (dependency-backed) | readiness: remove from LB on failure |

## 6. Networking & storage

- API listens on its container port (default 8080); TLS terminates at the reverse proxy.
- Only the database connection is outbound-critical; Meta's Graph API is called from the
  API egress (webhooks inbound require public reachability for Meta).
- Persistent state lives exclusively in PostgreSQL: provision durable volumes/backups per
  the backup/restore rehearsal (`scripts/rehearse_backup_restore.py`).
- No object storage, queues or caches are part of the v1 contract.

## 7. Environment-file mapping and deployment-time migration

The repository's production Compose contract uses these shell-safe names:

- `QASEDAK_DB_CONNECTION_STRING` is expanded into all seven `ConnectionStrings__*` values.
- `IDENTITY_AUTH_TOKEN_SIGNING_KEY` maps to `Identity:Auth:TokenSigningKey`.
- `INSTAGRAM_META_APP_SECRET`, `INSTAGRAM_META_VERIFY_TOKEN`, and
  `INSTAGRAM_PROTECTION_KEY_BASE64` map to the corresponding `Instagram:*` settings.

The exact template is `deploy/.env.production.example`; the real file stays on the server
with mode `600`.

The API release image exposes a deterministic one-shot migration command:

```bash
docker compose --env-file /opt/qasedak/.env.production \
  -f /opt/qasedak/compose.production.yml run --rm migrate
```

It runs `dotnet Qasedak.Api.dll --migrate`, migrates all seven contexts, does not start
HTTP, and returns non-zero without logging secrets if a migration fails.

## 8. Deployment-time migration procedure

1. Back up the database (validated procedure in the rehearsal script).
2. Apply migrations with the API image before switching traffic:
   each module context migrates independently under its own schema.
3. Start new API pods; `/health/ready` gates traffic switch.
4. Rollback = redeploy previous image tag; migrations are additive by policy, so the
   previous version remains compatible within one release train.

## 9. Contract enforcement

`python scripts/check_environment_contract.py` fails when code reads a connection string
or required setting that this document does not list. Run it in CI alongside the other
gates.
