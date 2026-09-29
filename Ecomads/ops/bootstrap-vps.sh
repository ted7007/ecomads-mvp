#!/usr/bin/env bash
set -Eeuo pipefail

SOURCE_DIR="${1:?Usage: bootstrap-vps.sh <source-directory>}"
ROOT=/opt/ecomads

if [[ "$(id -u)" -ne 0 ]]; then
  echo "Run bootstrap-vps.sh as root." >&2
  exit 1
fi

for required in compose.production.yml Caddyfile deploy-vps.sh rollback-vps.sh set-openai-key-vps.py; do
  test -f "$SOURCE_DIR/$required" || { echo "Missing $SOURCE_DIR/$required" >&2; exit 1; }
done

if ! docker compose version >/dev/null 2>&1; then
  apt-get update
  if apt-cache show docker-compose-v2 >/dev/null 2>&1; then
    DEBIAN_FRONTEND=noninteractive apt-get install -y docker-compose-v2
  else
    DEBIAN_FRONTEND=noninteractive apt-get install -y docker-compose-plugin
  fi
fi

install -d -m 0755 "$ROOT" "$ROOT/releases"
install -d -m 0700 "$ROOT/backups"
install -m 0644 "$SOURCE_DIR/compose.production.yml" "$ROOT/compose.production.yml"
if [[ -f "$ROOT/Caddyfile" ]]; then
  cat "$SOURCE_DIR/Caddyfile" > "$ROOT/Caddyfile"
  chmod 0644 "$ROOT/Caddyfile"
else
  install -m 0644 "$SOURCE_DIR/Caddyfile" "$ROOT/Caddyfile"
fi
install -m 0755 "$SOURCE_DIR/bootstrap-vps.sh" "$ROOT/bootstrap-vps.sh"
install -m 0755 "$SOURCE_DIR/deploy-vps.sh" "$ROOT/deploy-vps.sh"
install -m 0755 "$SOURCE_DIR/rollback-vps.sh" "$ROOT/rollback-vps.sh"
install -m 0700 "$SOURCE_DIR/set-openai-key-vps.py" "$ROOT/set-openai-key-vps.py"

if [[ ! -f "$ROOT/.env" ]]; then
  umask 077
  db_password="$(openssl rand -hex 24)"
  jwt_secret="$(openssl rand -hex 48)"
  cat > "$ROOT/.env" <<EOF
ECOMADS_IMAGE=ecomads:bootstrap
POSTGRES_DB=ecomads_db
POSTGRES_USER=ecomads_user
POSTGRES_PASSWORD=$db_password
JWT_SECRET=$jwt_secret
OPENAI_API_KEY=
OPENAI_BASE_URL=https://openai.bothub.chat/v1/chat/completions
OPENAI_MODEL=gpt-4o-mini
CADDY_EMAIL=
EOF
fi

chmod 0600 "$ROOT/.env"
docker compose --env-file "$ROOT/.env" -f "$ROOT/compose.production.yml" -p ecomads config --quiet
echo "VPS bootstrap is ready at $ROOT"
