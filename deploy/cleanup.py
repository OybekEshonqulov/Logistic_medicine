#!/usr/bin/env python3
"""Bound PharmaTrack image history; never run global Docker pruning."""
import argparse
from contextlib import nullcontext
import fcntl
import json
from pathlib import Path
import re
import subprocess

ROOT = Path('/opt/pharmatrack')
KEEP = 5


def docker(*args):
    return subprocess.check_output(['docker', *args], text=True).strip()


def candidates(images, used_ids, current_tag, keep=KEEP):
    tagged = []
    for item in images:
        for tag in item.get('RepoTags') or []:
            if re.fullmatch(r'pharmatrack-api:release-[0-9a-f]{40}', tag):
                tagged.append((item['Created'], tag, item['Id']))
    tagged.sort(reverse=True)
    protected = {entry[1] for entry in tagged[:keep]}
    protected.add(current_tag)
    return [tag for _, tag, image_id in tagged if tag not in protected and image_id not in used_ids]


def rotate_log(path):
    if path.is_file() and not path.is_symlink() and path.stat().st_size > 1024 * 1024:
        path.replace(path.with_name(path.name + '.1'))


def cleanup(dry_run=False):
    if ROOT.is_symlink() or ROOT.resolve() != ROOT:
        raise RuntimeError('Unexpected project root')
    release = (ROOT / 'release.env').read_text().strip()
    if not release.startswith('PHARMATRACK_IMAGE=pharmatrack-api:'):
        raise RuntimeError('Cannot identify current project image')
    current = release.split('=', 1)[1]
    ids = docker('image', 'ls', 'pharmatrack-api', '-q', '--no-trunc').splitlines()
    images = json.loads(docker('image', 'inspect', *sorted(set(ids)))) if ids else []
    containers = docker('ps', '-aq').splitlines()
    used = set(docker('inspect', '--format', '{{.Image}}', *containers).splitlines()) if containers else set()
    remove = candidates(images, used, current)
    print(('Would remove' if dry_run else 'Removing') + f' {len(remove)} old PharmaTrack image tags (keeping {KEEP} latest plus all container references).')
    for tag in remove:
        print(tag)
        if not dry_run:
            # No --force: Docker independently protects container references.
            subprocess.run(['docker', 'image', 'rm', tag], check=True)
    if not dry_run:
        for name in ('backup.log', 'maintenance.log'):
            rotate_log(ROOT / name)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--dry-run', action='store_true')
    parser.add_argument('--locked', action='store_true')
    args = parser.parse_args()
    with nullcontext() if args.locked else (ROOT / 'deploy.lock').open('a') as lock:
        if lock is not None:
            fcntl.flock(lock, fcntl.LOCK_EX)
        cleanup(args.dry_run)
