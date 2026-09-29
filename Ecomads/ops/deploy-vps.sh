#!/usr/bin/env bash
set -Eeuo pipefail

VERSION="${1:?Usage: deploy-vps.sh <version> <source-directory>}"
SOURCE_DIR="${2:?Usage: deploy-vps.sh <version> <source-directory>}"
ROOT=/opt/ecomads
ENV_FILE="$ROOT/.env"
COMPOSE_FILE="$ROOT/compose.production.yml"
CURRENT_FILE="$ROOT/.current-image"
PREVIOUS_FILE="$ROOT/.previous-image"

[[ "$VERSION" =~ ^[A-Za-z0-9._-]+$ ]] || { echo "Invalid version: $VERSION" >&2; exit 1; }
[[ "$SOURCE_DIR" == "$ROOT/releases/$VERSION" ]] || { echo "Unexpected source directory" >&2; exit 1; }
test -f "$SOURCE_DIR/Ecomads.WebApplication/Dockerfile"
test -f "$ENV_FILE"

compose() {
  docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" -p ecomads "$@"
}

set_image() {
  local image="$1"
  local temp
  temp="$(mktemp "$ROOT/.env.XXXXXX")"
  awk -v image="$image" '
    BEGIN { replaced = 0 }
    /^ECOMADS_IMAGE=/ { print "ECOMADS_IMAGE=" image; replaced = 1; next }
    { print }
    END { if (!replaced) print "ECOMADS_IMAGE=" image }
  ' "$ENV_FILE" > "$temp"
  chmod 0600 "$temp"
  mv -f "$temp" "$ENV_FILE"
}

new_image="ecomads:$VERSION"
previous_image=""
if [[ -f "$CURRENT_FILE" ]]; then
  previous_image="$(<"$CURRENT_FILE")"
elif docker inspect ecomads-web >/dev/null 2>&1; then
  previous_image="$(docker inspect ecomads-web --format '{{.Config.Image}}')"
fi

echo "Building $new_image"
docker build -t "$new_image" -f "$SOURCE_DIR/Ecomads.WebApplication/Dockerfile" "$SOURCE_DIR"

set -a
# shellcheck disable=SC1090
source "$ENV_FILE"
set +a

# The WB release has one fresh initial migration. Never apply it over the
# removed Excel schema; keep that database for an explicit separate reset.
if docker inspect ecomads-db >/dev/null 2>&1 && [[ "$(docker inspect ecomads-db --format '{{.State.Running}}')" == "true" ]]; then
  public_tables="$(docker exec -e PGPASSWORD="$POSTGRES_PASSWORD" ecomads-db \
    psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atqc \
    "SELECT count(*) FROM pg_tables WHERE schemaname = 'public'")"
  if [[ "$public_tables" -gt 0 ]]; then
    history_table="$(docker exec -e PGPASSWORD="$POSTGRES_PASSWORD" ecomads-db \
      psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atqc \
      "SELECT count(*) FROM pg_tables WHERE schemaname = 'public' AND tablename = '__EFMigrationsHistory'")"
    current_schema=0
    if [[ "$history_table" -gt 0 ]]; then
      current_schema="$(docker exec -e PGPASSWORD="$POSTGRES_PASSWORD" ecomads-db \
        psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Atqc \
        "SELECT count(*) FROM public.\"__EFMigrationsHistory\" WHERE \"MigrationId\" LIKE '%_InitialWbSchema'")"
    fi
    if [[ "$current_schema" -eq 0 ]]; then
      echo "Existing database has a non-WB schema. Deploy to a separate empty database; production was not changed." >&2
      exit 1
    fi
  fi
fi

if docker inspect ecomads-db >/dev/null 2>&1 && [[ "$(docker inspect ecomads-db --format '{{.State.Running}}')" == "true" ]]; then
  backup="$ROOT/backups/ecomads-$(date -u +%Y%m%dT%H%M%SZ)-before-$VERSION.dump"
  echo "Creating database backup $backup"
  docker exec -e PGPASSWORD="$POSTGRES_PASSWORD" ecomads-db \
    pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc > "$backup"
  chmod 0600 "$backup"
fi

set_image "$new_image"
echo "Starting $new_image"
compose up -d --remove-orphans
docker exec ecomads-caddy caddy reload --config /etc/caddy/Caddyfile --adapter caddyfile

healthy=0
for _ in $(seq 1 60); do
  if curl --fail --silent --show-error --max-time 3 \
      -H 'Host: 31.76.53.166' http://127.0.0.1/health >/dev/null; then
    healthy=1
    break
  fi
  sleep 2
done

if [[ "$healthy" -ne 1 ]]; then
  echo "Deployment health check failed." >&2
  compose ps >&2 || true
  compose logs --tail=150 web caddy >&2 || true
  if [[ -n "$previous_image" ]]; then
    echo "Rolling back to $previous_image" >&2
    set_image "$previous_image"
    compose up -d --remove-orphans
  fi
  exit 1
fi

if [[ -n "$previous_image" && "$previous_image" != "$new_image" ]]; then
  printf '%s\n' "$previous_image" > "$PREVIOUS_FILE"
fi
printf '%s\n' "$new_image" > "$CURRENT_FILE"
chmod 0600 "$CURRENT_FILE" "$PREVIOUS_FILE" 2>/dev/null || true

mapfile -t expired_releases < <(
  find "$ROOT/releases" -mindepth 1 -maxdepth 1 -type d -printf '%f\n' | sort -r | tail -n +6
)
for release in "${expired_releases[@]}"; do
  [[ "$release" =~ ^[A-Za-z0-9._-]+$ ]] || continue
  expired_path="$(realpath -m "$ROOT/releases/$release")"
  [[ "$expired_path" == "$ROOT/releases/"* ]] || continue
  if [[ "ecomads:$release" != "$new_image" && "ecomads:$release" != "$previous_image" ]]; then
    docker image rm "ecomads:$release" >/dev/null 2>&1 || true
  fi
  rm -rf -- "$expired_path"
done

mapfile -t expired_backups < <(
  find "$ROOT/backups" -mindepth 1 -maxdepth 1 -type f -name 'ecomads-*.dump' -printf '%f\n' | sort -r | tail -n +11
)
for backup in "${expired_backups[@]}"; do
  [[ "$backup" =~ ^ecomads-[A-Za-z0-9._-]+\.dump$ ]] || continue
  rm -f -- "$ROOT/backups/$backup"
done

compose ps
echo "Deployment completed: $new_image"
echo "Direct URL: http://31.76.53.166"
