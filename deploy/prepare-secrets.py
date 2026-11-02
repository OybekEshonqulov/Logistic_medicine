#!/usr/bin/env python3
"""Run once on the server. Secrets are generated there and never printed."""
import os
from pathlib import Path
import secrets

os.umask(0o077)
root = Path(__file__).resolve().parent / 'secrets'
root.mkdir(mode=0o700, exist_ok=True)
db = root / 'db.env'
api = root / 'api.env'
if db.exists() or api.exists():
    raise SystemExit('Secrets already exist; refusing to overwrite.')
password = secrets.token_hex(32)
db.write_text(f'POSTGRES_USER=pharmatrack\nPOSTGRES_DB=pharmatrack\nPOSTGRES_PASSWORD={password}\n')
api.write_text(
    f'ConnectionStrings__Default=Host=db;Port=5432;Database=pharmatrack;Username=pharmatrack;Password={password}\n'
    f'Jwt__SigningKey={secrets.token_hex(48)}\n'
    'Seed__AdminPhone=+998900000000\n'
    f'Seed__AdminPassword={secrets.token_urlsafe(24)}\n'
    'AllowedHosts=pharm-api.agorarestobar.uz;localhost;127.0.0.1\n'
    'Swagger__Enabled=true\n'
)
print('Server secrets created; values were not printed.')
