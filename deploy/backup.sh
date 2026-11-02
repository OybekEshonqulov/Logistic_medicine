#!/usr/bin/env bash
set -euo pipefail
cd /opt/pharmatrack
umask 077
mkdir -p backups
file="backups/$(date -u +%Y%m%dT%H%M%S)-${RANDOM}.dump"
docker compose --env-file release.env -f deploy/compose.yaml exec -T db pg_dump -U pharmatrack -d pharmatrack -Fc > "$file.tmp"
test -s "$file.tmp"
mv "$file.tmp" "$file"
# Keep 14 days, only this project's completed backup files.
find backups -maxdepth 1 -type f -name '*.dump' -mtime +14 -delete
echo 'PharmaTrack database backup completed.'
