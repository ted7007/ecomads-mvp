#!/usr/bin/env bash
set -euo pipefail
docker inspect ecomads-web --format '{{.Config.Image}} {{.State.Status}} restarts={{.RestartCount}}'
curl -fsS --max-time 15 https://ecomads.ru/health
printf '\n/results HTTP '
curl -sS -o /tmp/ecomads-results-verify.html -w '%{http_code}\n' --max-time 15 https://ecomads.ru/results
asset=$(sed -n 's/.*src="\([^"]*\/assets\/[^" ]*\.js\)".*/\1/p' /tmp/ecomads-results-verify.html | head -1)
if [[ -z "$asset" ]]; then
  asset=$(grep -o '/assets/[^" ]*\.js' /tmp/ecomads-results-verify.html | head -1)
fi
test -n "$asset"
printf 'Frontend asset: %s\n' "$asset"
curl -fsS --max-time 30 "https://ecomads.ru$asset" -o /tmp/ecomads-results-verify.js
sha256sum /tmp/ecomads-results-verify.js
grep -q '/api/wb/results' /tmp/ecomads-results-verify.js
printf 'Results API without authentication HTTP '
curl -sS -o /dev/null -w '%{http_code}\n' --max-time 15 https://ecomads.ru/api/wb/results
docker exec -i ecomads-db sh -c 'psql -v ON_ERROR_STOP=1 -U "$POSTGRES_USER" -d "$POSTGRES_DB"' <<'SQL'
SELECT "MigrationId" FROM "__EFMigrationsHistory" ORDER BY "MigrationId" DESC LIMIT 3;
SELECT tablename FROM pg_tables WHERE schemaname='public' AND tablename LIKE 'wb_result_%' ORDER BY tablename;
SELECT count(*) AS observations FROM wb_result_observations;
SELECT count(*) AS recommendations FROM wb_result_recommendations;
SELECT count(*) AS measurements FROM wb_result_measurements;
SQL
printf '\nRecent application failures (if any):\n'
docker logs --since 5m ecomads-web 2>&1 | grep -E -A 5 'fail:|Unhandled exception|fatal:' || true
printf '\nBackup:\n'
find /opt/ecomads/backups -maxdepth 1 -name '*before-20261020T125513Z-336a559-dirty.dump' -printf '%f %s bytes\n'
