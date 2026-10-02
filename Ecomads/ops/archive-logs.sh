#!/usr/bin/env bash
set -Eeuo pipefail

ROOT=/opt/ecomads
LOG_DIR="$ROOT/logs"
REASON="${1:-manual}"

[[ "$(id -u)" -eq 0 ]] || { echo "Run archive-logs.sh as root." >&2; exit 1; }
[[ "$REASON" =~ ^[a-z0-9_-]+$ ]] || { echo "Invalid archive reason." >&2; exit 1; }
umask 077
install -d -m 0700 "$LOG_DIR"

exec 9>"$LOG_DIR/.archive.lock"
flock -w 60 9
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
temporary=""
cleanup() {
  if [[ -n "$temporary" ]]; then
    rm -f -- "$temporary"
  fi
}
trap cleanup EXIT

for service in web caddy db; do
  container="ecomads-$service"
  if ! docker inspect "$container" >/dev/null 2>&1; then
    continue
  fi
  archive="$LOG_DIR/$timestamp-$REASON-$service.log.gz"
  temporary="$(mktemp "$LOG_DIR/.archive.XXXXXX")"
  if [[ "$REASON" == daily ]]; then
    docker logs --timestamps --since 48h "$container" 2>&1 | gzip -c > "$temporary"
  else
    docker logs --timestamps "$container" 2>&1 | gzip -c > "$temporary"
  fi
  chmod 0600 "$temporary"
  mv -f -- "$temporary" "$archive"
  temporary=""
done

find "$LOG_DIR" -maxdepth 1 -type f -name '*.log.gz' -mmin +20160 -delete
total_bytes="$(find "$LOG_DIR" -maxdepth 1 -type f -name '*.log.gz' -printf '%s\n' |
  awk '{ sum += $1 } END { print sum + 0 }')"
while IFS= read -r name && (( total_bytes > 1000000000 )); do
  archive="$LOG_DIR/$name"
  size="$(stat -c %s "$archive")"
  rm -f -- "$archive"
  total_bytes=$((total_bytes - size))
done < <(find "$LOG_DIR" -maxdepth 1 -type f -name '*.log.gz' -printf '%f\n' | sort)
echo "Log archives saved in $LOG_DIR ($REASON)."
