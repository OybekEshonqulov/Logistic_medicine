#!/usr/bin/env bash
# Installed on the server. SSH forced command accepts only: deploy <commit SHA>.
set -euo pipefail
umask 077
cd /opt/pharmatrack
exec 9>deploy.lock
flock -w 600 9
command=${SSH_ORIGINAL_COMMAND:-${1:-}}
if [[ ! "$command" =~ ^deploy\ ([0-9a-f]{40})$ ]]; then
  echo 'Only deploy <40-character commit SHA> is allowed.' >&2
  exit 1
fi
sha=${BASH_REMATCH[1]}
archive=$(mktemp /opt/pharmatrack/release.XXXXXX.tgz)
trap 'rm -f "$archive"' EXIT
# Bound archive input to 1 GiB; an oversized/truncated image fails docker load.
head -c 1073741824 > "$archive"
gzip -dc "$archive" | docker load >/dev/null
image="pharmatrack-api:release-$sha"
docker image inspect "$image" >/dev/null
old=$(cat release.env)
if docker compose --env-file release.env -f deploy/compose.yaml ps --status running --services | grep -qx db; then
  bash deploy/backup.sh
fi
printf 'PHARMATRACK_IMAGE=%s\n' "$image" > release.env
if docker compose --env-file release.env -f deploy/compose.yaml up -d --no-deps api; then
  for attempt in $(seq 1 90); do
    if curl --fail --silent --max-time 3 http://127.0.0.1:3300/health >/dev/null; then
      printf '%s\n' "$sha" > current-sha
      echo "Deployment healthy: $sha"
      exit 0
    fi
    sleep 2
  done
fi
printf '%s\n' "$old" > release.env
docker compose --env-file release.env -f deploy/compose.yaml up -d --no-deps api
echo 'Deployment failed; previous API image restored. Database backup retained; schema is not automatically rolled back.' >&2
exit 1
