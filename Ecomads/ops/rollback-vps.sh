#!/usr/bin/env bash
set -Eeuo pipefail

ROOT=/opt/ecomads
ENV_FILE="$ROOT/.env"
COMPOSE_FILE="$ROOT/compose.production.yml"
CURRENT_FILE="$ROOT/.current-image"
PREVIOUS_FILE="$ROOT/.previous-image"

test -s "$PREVIOUS_FILE" || { echo "No previous image is recorded." >&2; exit 1; }
previous_image="$(<"$PREVIOUS_FILE")"
current_image="$(<"$CURRENT_FILE")"
docker image inspect "$previous_image" >/dev/null

temp="$(mktemp "$ROOT/.env.XXXXXX")"
awk -v image="$previous_image" '
  BEGIN { replaced = 0 }
  /^ECOMADS_IMAGE=/ { print "ECOMADS_IMAGE=" image; replaced = 1; next }
  { print }
  END { if (!replaced) print "ECOMADS_IMAGE=" image }
' "$ENV_FILE" > "$temp"
chmod 0600 "$temp"
mv -f "$temp" "$ENV_FILE"

docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" -p ecomads up -d --remove-orphans

for _ in $(seq 1 60); do
  if curl --fail --silent --show-error --max-time 3 \
      -H 'Host: 31.76.53.166' http://127.0.0.1/health >/dev/null; then
    printf '%s\n' "$previous_image" > "$CURRENT_FILE"
    printf '%s\n' "$current_image" > "$PREVIOUS_FILE"
    echo "Rollback completed: $previous_image"
    exit 0
  fi
  sleep 2
done

echo "Rollback health check failed." >&2
exit 1
