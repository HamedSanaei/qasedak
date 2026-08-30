# Qasedak production deployment

Qasedak deploys two immutable runtime images and one PostgreSQL database:

- `ghcr.io/<lowercase-owner>/qasedak-api:sha-<12-hex-sha>`
- `ghcr.io/<lowercase-owner>/qasedak-web:sha-<12-hex-sha>`
- `postgres:18-alpine` with one persistent named volume

The API image contains the one-shot migration runner. The Ubuntu server needs only Docker
Engine and the Docker Compose plugin; it does not need the repository, Node.js, .NET SDK,
or `dotnet-ef`.

## First-time Ubuntu bootstrap

From a checkout containing this directory, copy the bootstrap script to the server and run:

```bash
scp -P "$DEPLOY_PORT" -o StrictHostKeyChecking=yes deploy/bootstrap-ubuntu.sh user@server:/tmp/qasedak-bootstrap.sh
ssh -p "$DEPLOY_PORT" -o StrictHostKeyChecking=yes user@server \
  'chmod 700 /tmp/qasedak-bootstrap.sh && /tmp/qasedak-bootstrap.sh'
```

The script is idempotent. It creates `/opt/qasedak`, `state/`, `backups/`, and `secrets/`,
installs the production compose contract and deployment script, and never overwrites an
existing `.env.production` or PostgreSQL secret file.

Manually create and secure the two server-only files:

```bash
cp /opt/qasedak/.env.production.example /opt/qasedak/.env.production
chmod 600 /opt/qasedak/.env.production
install -m 600 /dev/null /opt/qasedak/secrets/postgres_password.txt
# Edit both files and replace every CHANGE_ME value; do not use the example defaults.
```

`QASEDAK_DB_CONNECTION_STRING` must use `Host=postgres;Port=5432` and the same database
credentials as `postgres_password.txt`. It is used by all seven EF contexts, each of which
owns a separate logical schema: `identity`, `instagram`, `conversations`, `automations`,
`contacts`, `billing`, and `audit`.

## GitHub Actions secrets

Required repository or environment secrets:

- `DEPLOY_HOST` — Ubuntu server hostname/IP.
- `DEPLOY_PORT` — SSH port.
- `DEPLOY_USER` — restricted deployment user with access to `/opt/qasedak` and Docker.
- `DEPLOY_SSH_KEY` — private key for that user; never printed.
- `DEPLOY_KNOWN_HOSTS` — exact pinned `known_hosts` line(s), collected out-of-band.

Optional:

- `GHCR_READ_TOKEN` — only if the GHCR packages are private and the server cannot pull
  anonymously. When present, the deploy workflow sends it only over the already-validated
  SSH channel to `docker login --password-stdin`; it is never printed or stored by Git.

The server `.env.production` contains application/database/payment secrets and is never
stored in Git or transferred by the workflow.

## Automated lifecycle

```text
git push master
  -> CI (repository, backend, frontend, Docker gates)
  -> Publish Images (only after successful CI)
  -> GHCR immutable images: sha-<12-char-sha>
  -> Deploy Production workflow
  -> strict SSH known_hosts validation
  -> /opt/qasedak remote-deploy.sh + compose artifact update
  -> flock deployment lock
  -> docker compose pull immutable API/Web images
  -> PostgreSQL health check
  -> custom-format backup in /opt/qasedak/backups/
  -> one-shot migrate container (--migrate, all seven contexts)
  -> API/Web switch
  -> /health/live, /health/ready, /api/v1/system, Web and public auth-routing smoke checks
  -> persist state/last-successful.env
```

The deployment workflow uses `concurrency.group=qasedak-production` with
`cancel-in-progress: false`. The server script also uses `flock` at
`/opt/qasedak/state/deploy.lock`.

## Migration

The explicit production migration command is:

```bash
docker compose --env-file /opt/qasedak/.env.production \
  -f /opt/qasedak/compose.production.yml run --rm migrate
```

The command runs `dotnet Qasedak.Api.dll --migrate` from the exact API release image,
without starting the HTTP server. It is idempotent and migrates all seven contexts. A
non-zero exit prevents the deployment script from switching API/Web containers.

## Backups

Before every migration, `remote-deploy.sh` runs `pg_dump --format=custom` inside the
PostgreSQL container and copies the dump to:

```text
/opt/qasedak/backups/qasedak-<UTC-timestamp>-<image-tag>.dump
```

Backups are mode `600`. The deployment never deletes or recreates the PostgreSQL volume.
Retention/remote replication should be added according to the operator's backup policy.

## Rollback

If the new API/Web fails health or smoke checks after the switch, the script restores the
last value in `/opt/qasedak/state/last-successful.env`, starts the previous immutable API
and Web images, and repeats all health/smoke/image checks. It still exits non-zero so the
GitHub Action reports the failed commit. A rollback failure is reported as `CRITICAL` and
recent API/Web logs are preserved in the Action output.

Database rollback is never attempted automatically. Migrations are additive by release
policy; investigate/repair forward or restore from a separately approved database backup.

Manual binary rollback:

```bash
cd /opt/qasedak
IMAGE_TAG=sha-<previous-12-char-sha> ./remote-deploy.sh
```

## Operations

```bash
cd /opt/qasedak
docker compose --env-file .env.production -f compose.production.yml ps
docker compose --env-file .env.production -f compose.production.yml logs -f api
docker compose --env-file .env.production -f compose.production.yml logs -f web
docker compose --env-file .env.production -f compose.production.yml logs -f postgres
cat state/last-successful.env
curl -fsS http://127.0.0.1:${API_PORT:-8080}/health/live
curl -fsS http://127.0.0.1:${API_PORT:-8080}/health/ready
curl -fsS http://127.0.0.1:${API_PORT:-8080}/api/v1/system
curl -fsS http://127.0.0.1:${WEB_PORT:-3000}/
```

Current running image identities:

```bash
docker compose --env-file .env.production -f compose.production.yml ps -q api web \
  | xargs -r docker inspect --format '{{.Name}} {{.Config.Image}}'
```

Production PostgreSQL has no published host port. Put a reverse proxy/TLS terminator in
front of the loopback-bound API/Web ports. The public Web origin must route `/api/` to
`127.0.0.1:${API_PORT}` and every other application path, including `/web-api/`, to
`127.0.0.1:${WEB_PORT}`; route `/health/` to the API if operational checks require it.
Browser backend calls are same-origin relative `/api/...` paths. Web-owned session
handlers deliberately use `/web-api/...` so the API proxy prefix cannot bypass their
HttpOnly cookie logic. The deployment smoke requires a public invalid-login request to
`/web-api/auth/login` to return HTTP 401. `QASEDAK_API_INTERNAL_URL=http://api:8080` is
Docker-internal only and must never be exposed to browser code. Keep `PUBLIC_WEB_ORIGIN`
aligned with the public Web origin for CORS. DNS/TLS, Meta public webhook reachability,
managed PostgreSQL, and real payment-provider credentials remain production-only checks.
