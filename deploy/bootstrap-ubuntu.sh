#!/usr/bin/env bash
set -Eeuo pipefail

INSTALL_DIR="${INSTALL_DIR:-/opt/qasedak}"
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"

if [[ "${EUID}" -ne 0 ]]; then
  echo "Run as root: sudo bash deploy/bootstrap-ubuntu.sh" >&2
  exit 2
fi

install_docker() {
  if command -v docker >/dev/null 2>&1; then
    echo "Docker already installed: $(docker --version)"
  else
    echo "Installing Docker Engine from the official repository installer..."
    apt-get update
    apt-get install -y ca-certificates curl
    curl -fsSL https://get.docker.com | sh
  fi

  if docker compose version >/dev/null 2>&1; then
    echo "Docker Compose plugin already installed: $(docker compose version)"
  else
    echo "Installing Docker Compose plugin..."
    apt-get update
    apt-get install -y docker-compose-plugin
  fi

  systemctl enable --now docker
}

install_docker
install -d -m 0750 "$INSTALL_DIR" "$INSTALL_DIR/state" "$INSTALL_DIR/backups" "$INSTALL_DIR/secrets"

# Deployment artifacts are safe to refresh. The real env and secret files are explicitly
# never overwritten by this script.
install -m 0644 "$SCRIPT_DIR/compose.production.yml" "$INSTALL_DIR/compose.production.yml"
install -m 0750 "$SCRIPT_DIR/remote-deploy.sh" "$INSTALL_DIR/remote-deploy.sh"
install -m 0644 "$SCRIPT_DIR/.env.production.example" "$INSTALL_DIR/.env.production.example"
chown -R root:root "$INSTALL_DIR"
chmod 0750 "$INSTALL_DIR" "$INSTALL_DIR/state" "$INSTALL_DIR/backups" "$INSTALL_DIR/secrets"

if [[ -e "$INSTALL_DIR/.env.production" ]]; then
  echo "Preserved existing $INSTALL_DIR/.env.production (not overwritten)."
else
  echo "No production env created. Copy $INSTALL_DIR/.env.production.example to"
  echo "$INSTALL_DIR/.env.production and populate it manually, then chmod 600 it."
fi

if [[ -e "$INSTALL_DIR/secrets/postgres_password.txt" ]]; then
  echo "Preserved existing PostgreSQL secret file (not overwritten)."
else
  echo "Create $INSTALL_DIR/secrets/postgres_password.txt manually with a strong password."
  echo "Then run: chmod 600 $INSTALL_DIR/secrets/postgres_password.txt"
fi

echo
echo "Ubuntu Qasedak bootstrap complete."
echo "Required manual files:"
echo "  $INSTALL_DIR/.env.production"
echo "  $INSTALL_DIR/secrets/postgres_password.txt"
echo "Required permissions: chmod 600 on both files."
echo "The first deployment is started by the CI workflow after IMAGE_TAG is supplied."
