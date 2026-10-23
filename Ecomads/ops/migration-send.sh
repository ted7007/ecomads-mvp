#!/usr/bin/env bash
set -Eeuo pipefail
case "${SSH_ORIGINAL_COMMAND:-}" in
  images)
    docker image save ecomads:20261020T135351Z-b6a2a69-dirty ecomads:20261020T134103Z-336a559-dirty postgres:15-alpine caddy:2-alpine | gzip -1
    exit 0
    ;;
  preliminary) directory=/opt/ecomads-migration/preliminary-20261024 ;;
  final) directory=/opt/ecomads-migration/final-20261024 ;;
  *) exit 2 ;;
esac
test -f "$directory/SHA256SUMS"
cd "$directory"
tar -cf - SHA256SUMS manifest.json database.dump application.tar.gz
