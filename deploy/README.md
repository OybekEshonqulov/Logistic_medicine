# PharmaTrack production

Domain: https://pharm-api.agorarestobar.uz (Swagger: `/swagger`, database health: `/health`).

2026-11-02: backend deployed, HTTPS and admin authentication verified, 73 tests passed. Automatic CD is enabled for pushes to `main`, using a dedicated SSH key restricted to this project's deployment receiver. Daily backup cron runs at 03:15 Asia/Tashkent and keeps 14 days of dumps on this VPS.

Server directory: `/opt/pharmatrack`. Compose project: `pharmatrack`. Only API loopback port `127.0.0.1:3300` is published. PostgreSQL has a separate volume and no published port. Network `172.30.89.0/24` must remain exclusive to this project. Host Caddy terminates HTTPS; append `PharmaTrack.caddy` to the existing configuration after taking a backup, validate, then reload.

## First deployment

Copy the source and `deploy/` directory to the server. Run `python3 deploy/prepare-secrets.py` once. Set the real `Seed__AdminPhone` in `deploy/secrets/api.env` before starting API. Secrets are generated only on the server; do not commit or print their files.

```sh
cd /opt/pharmatrack
docker build -t pharmatrack-api:initial .
printf 'PHARMATRACK_IMAGE=pharmatrack-api:initial\n' > release.env
docker compose --env-file release.env -f deploy/compose.yaml up -d
curl --fail http://127.0.0.1:3300/health
```

## GitHub Actions

PRs run tests and image build. Pushes to `main` do the same, then deploy when repository variable `PRODUCTION_DEPLOY_ENABLED=true`. Manual workflow dispatch on `main` also works. The image is named with the tested commit SHA. Deployments serialize in GitHub and on the server.

An administrator of `OybekEshonqulov/Logistic_medicine` must set repository Actions secrets:

- `DEPLOY_SSH_KEY`: complete private key dedicated to PharmaTrack, including BEGIN/END lines.
- `DEPLOY_KNOWN_HOSTS`: verified server host-key entries for `169.58.234.231`; use the already trusted host key, not an unverified scan.

Install its public key for `deploy` with this exact restriction, replacing only the public key:

```text
restrict,command="/bin/bash /opt/pharmatrack/deploy/deploy.sh" ssh-ed25519 PUBLIC_KEY pharmatrack-cd
```

Never use an unrestricted administrative SSH key for Actions. Test that `ssh ... hostname` is rejected and `deploy <SHA>` with a valid saved image succeeds before enabling the variable. The receiver and Compose files are managed separately on the server; changes under `deploy/` require a reviewed server update, since CD only transfers the image.

## Backup and recovery

`bash /opt/pharmatrack/deploy/backup.sh` creates a private PostgreSQL custom-format dump before each CD deploy. A daily user cron can run the same command. Completed dumps older than 14 days are removed; backups remain on this VPS. Download copies separately when external storage is available.

Health failure restores the previous API image. EF migrations run on startup, so database schema is **not** rolled back automatically. Use backwards-compatible migrations; destructive migrations need a separate recovery plan. Restore a backup only after stopping this project's API and explicitly choosing the target dump.

Automatic image retention runs after successful deploys and daily at 03:45 server time. It keeps the 5 latest release tags, the configured current image, and all images referenced by any existing container. Only `pharmatrack-api:release-<40-character SHA>` tags are eligible; no global prune, volumes, or other projects are touched. Backup and maintenance logs rotate at 1 MiB. Preview with `python3 /opt/pharmatrack/deploy/cleanup.py --dry-run`.

```sh
cd /opt/pharmatrack
docker compose --env-file release.env -f deploy/compose.yaml ps
docker compose --env-file release.env -f deploy/compose.yaml logs --tail 100 api
```

Future frontend origins go in server-only `Cors__Origins__0`, etc. Then recreate only the API service. Do not use wildcard CORS.

References: [GitHub workflow triggers](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow), [ASP.NET forwarded headers](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0), [Caddy reload](https://caddyserver.com/docs/command-line#caddy-reload).
