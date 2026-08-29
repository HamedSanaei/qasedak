#!/usr/bin/env bash
set -Eeuo pipefail

DEPLOY_DIR="${DEPLOY_DIR:-/opt/qasedak}"
COMPOSE_FILE="${COMPOSE_FILE:-$DEPLOY_DIR/compose.production.yml}"
ENV_FILE="${ENV_FILE:-$DEPLOY_DIR/.env.production}"
cd "$DEPLOY_DIR"
STATE_DIR="${STATE_DIR:-$DEPLOY_DIR/state}"
BACKUP_DIR="${BACKUP_DIR:-$DEPLOY_DIR/backups}"
LOCK_FILE="${LOCK_FILE:-$STATE_DIR/deploy.lock}"
NEW_IMAGE_TAG="${IMAGE_TAG:?IMAGE_TAG must be an immutable sha- tag}"

if [[ ! "$NEW_IMAGE_TAG" =~ ^sha-[0-9a-fA-F]{12}$ ]]; then
  echo "IMAGE_TAG must match sha-<12 hex characters>" >&2
  exit 2
fi

if [[ ! -r "$COMPOSE_FILE" ]]; then echo "Missing compose file: $COMPOSE_FILE" >&2; exit 2; fi
if [[ ! -r "$ENV_FILE" ]]; then echo "Missing production env file: $ENV_FILE" >&2; exit 2; fi
# Load variable names/values for validation and script logic. The env file must contain
# paths/references for secrets, never secrets committed to this repository.
# shellcheck disable=SC1090
source "$ENV_FILE"
: "${IMAGE_NAMESPACE:?IMAGE_NAMESPACE is required}"
: "${POSTGRES_DB:?POSTGRES_DB is required}"
: "${POSTGRES_USER:?POSTGRES_USER is required}"
: "${POSTGRES_PASSWORD_FILE:?POSTGRES_PASSWORD_FILE is required}"
: "${PUBLIC_WEB_ORIGIN:?PUBLIC_WEB_ORIGIN is required}"
: "${IDENTITY_AUTH_TOKEN_SIGNING_KEY:?IDENTITY_AUTH_TOKEN_SIGNING_KEY is required}"
: "${INSTAGRAM_META_APP_SECRET:?INSTAGRAM_META_APP_SECRET is required}"
: "${INSTAGRAM_META_VERIFY_TOKEN:?INSTAGRAM_META_VERIFY_TOKEN is required}"
: "${INSTAGRAM_PROTECTION_KEY_BASE64:?INSTAGRAM_PROTECTION_KEY_BASE64 is required}"
: "${QASEDAK_DB_CONNECTION_STRING:?QASEDAK_DB_CONNECTION_STRING is required}"

command -v docker >/dev/null || { echo "docker is required" >&2; exit 2; }
docker compose version >/dev/null || { echo "Docker Compose plugin is required" >&2; exit 2; }
mkdir -p "$STATE_DIR" "$BACKUP_DIR"
chmod 700 "$STATE_DIR" "$BACKUP_DIR"

exec 9>"$LOCK_FILE"
if ! flock -n 9; then
  echo "Another Qasedak deployment is already running." >&2
  exit 75
fi

compose() {
  docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" "$@"
}

CURRENT_TAG=""
if [[ -r "$STATE_DIR/last-successful.env" ]]; then
  # shellcheck disable=SC1091
  source "$STATE_DIR/last-successful.env"
  CURRENT_TAG="${LAST_SUCCESSFUL_IMAGE_TAG:-}"
fi
PREVIOUS_IMAGE_TAG="$CURRENT_TAG"
export IMAGE_TAG="$NEW_IMAGE_TAG"

if [[ -z "$PREVIOUS_IMAGE_TAG" ]]; then
  echo "No previous successful release recorded; rollback is unavailable for this first deployment."
else
  echo "Previous successful image tag: $PREVIOUS_IMAGE_TAG"
fi
echo "Deploying immutable image tag: $NEW_IMAGE_TAG"

backup_database() {
  local stamp backup_file
  stamp="$(date -u +%Y%m%dT%H%M%SZ)"
  backup_file="$BACKUP_DIR/qasedak-${stamp}-${NEW_IMAGE_TAG}.dump"
  echo "Creating database backup: $(basename "$backup_file")"
  compose exec -T postgres pg_dump \
    --format=custom \
    --file=/tmp/qasedak-backup.dump \
    --dbname="$POSTGRES_DB" \
    --username="$POSTGRES_USER"
  compose cp postgres:/tmp/qasedak-backup.dump "$backup_file"
  compose exec -T postgres rm -f /tmp/qasedak-backup.dump
  chmod 600 "$backup_file"
  echo "Database backup created."
}

wait_for_http() {
  local url="$1" attempts="${2:-60}" i
  for i in $(seq 1 "$attempts"); do
    if curl --fail --silent --show-error --max-time 5 "$url" >/dev/null; then return 0; fi
    sleep 2
  done
  echo "Timed out waiting for $url" >&2
  return 1
}

wait_for_postgres() {
  local attempts="${1:-60}" i

  for i in $(seq 1 "$attempts"); do
    if compose exec -T postgres \
      pg_isready \
      -U "$POSTGRES_USER" \
      -d "$POSTGRES_DB" >/dev/null 2>&1; then
      echo "PostgreSQL is ready."
      return 0
    fi

    sleep 2
  done

  echo "Timed out waiting for PostgreSQL readiness." >&2
  compose ps postgres >&2 || true
  compose logs --no-color --tail=100 postgres >&2 || true
  return 1
}

health_and_smoke() {
  local api_base="http://${API_BIND_ADDRESS:-127.0.0.1}:${API_PORT:-8080}"
  local web_base="http://${WEB_BIND_ADDRESS:-127.0.0.1}:${WEB_PORT:-3000}"
  wait_for_http "$api_base/health/live"
  wait_for_http "$api_base/health/ready"
  curl --fail --silent --show-error --max-time 10 "$api_base/api/v1/system" | grep -q 'Modular Monolith'
  wait_for_http "$web_base/"
  echo "Health and smoke checks passed."
}

verify_running_tag() {
  local service expected
  for service in api web; do
    expected="ghcr.io/${IMAGE_NAMESPACE}/qasedak-${service}:${IMAGE_TAG}"
    local image
    image="$(compose ps -q "$service" | xargs -r docker inspect --format '{{.Config.Image}}')"
    if [[ "$image" != "$expected" ]]; then
      echo "Running $service image '$image' does not match '$expected'" >&2
      return 1
    fi
  done
}

rollback() {
  local failed_tag="$NEW_IMAGE_TAG"
  if [[ -z "$PREVIOUS_IMAGE_TAG" ]]; then
    echo "deployment failed; no previous release available for rollback" >&2
    return 1
  fi
  echo "Deployment failed after application switch; rolling back to $PREVIOUS_IMAGE_TAG."
  export IMAGE_TAG="$PREVIOUS_IMAGE_TAG"
  compose up -d --no-build api web
  if health_and_smoke && verify_running_tag; then
    echo "deployment failed; rollback succeeded (restored $PREVIOUS_IMAGE_TAG from $failed_tag)" >&2
  else
    echo "CRITICAL: deployment failed; rollback failed" >&2
    compose logs --no-color --tail=200 api web >&2 || true
    return 1
  fi
  return 1
}

compose pull migrate api web
compose up -d postgres
echo "Starting PostgreSQL and waiting for readiness..."
wait_for_postgres
backup_database

# Migration failure must not switch application containers.
if ! compose run --rm migrate; then
  echo "Migration failed; API/Web were not switched." >&2
  exit 1
fi

compose up -d --no-deps api web
if ! health_and_smoke || ! verify_running_tag; then
  rollback
fi

export IMAGE_TAG="$NEW_IMAGE_TAG"
cat > "$STATE_DIR/last-successful.env" <<EOF
LAST_SUCCESSFUL_IMAGE_TAG=$NEW_IMAGE_TAG
LAST_SUCCESSFUL_AT_UTC=$(date -u +%Y-%m-%dT%H:%M:%SZ)
EOF
chmod 600 "$STATE_DIR/last-successful.env"

echo "Deployment succeeded: $NEW_IMAGE_TAG"
# Never prune volumes. Keep current and previous images; only remove stale Qasedak images
# that are not referenced by any container, then trim build cache conservatively.
protected_tags=("$NEW_IMAGE_TAG")
[[ -n "$PREVIOUS_IMAGE_TAG" ]] && protected_tags+=("$PREVIOUS_IMAGE_TAG")
while IFS= read -r image; do
  [[ -z "$image" ]] && continue
  keep=false
  for tag in "${protected_tags[@]}"; do
    [[ "$image" == *":$tag" ]] && keep=true
  done
  if [[ "$keep" == false ]]; then docker image rm "$image" >/dev/null 2>&1 || true; fi
done < <(
  docker images --format '{{.Repository}}:{{.Tag}}' "ghcr.io/${IMAGE_NAMESPACE}/qasedak-api" 2>/dev/null || true
  docker images --format '{{.Repository}}:{{.Tag}}' "ghcr.io/${IMAGE_NAMESPACE}/qasedak-web" 2>/dev/null || true
)
docker builder prune --filter until=168h --force >/dev/null 2>&1 || true
