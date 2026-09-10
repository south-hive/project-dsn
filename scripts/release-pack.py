#!/usr/bin/env python3
"""Package already published Host/Mock and SDK assets without uploading anything."""
import argparse
import hashlib
from pathlib import Path
import re
import zipfile

ROOT = Path(__file__).resolve().parent.parent
parser = argparse.ArgumentParser()
parser.add_argument('version', help='Release tag, e.g. v0.1.0')
args = parser.parse_args()
if not re.fullmatch(r'v[0-9]+\.[0-9]+\.[0-9]+(?:-[a-zA-Z0-9.-]+)?', args.version):
    parser.error('Invalid release version')
out = ROOT / 'artifacts/release'
out.mkdir(parents=True, exist_ok=True)
wheel, = (ROOT / 'artifacts/sdk').glob('dsn_source-*.whl')
contracts, = (ROOT / 'artifacts/sdk').glob('Dsn.Contracts.*.nupkg')
for required in ('host/Dsn.Host.dll', 'host/plugins/Dsn.Workspaces.Examples.dll', 'mock/Dsn.Mock.dll'):
    if not (ROOT / 'artifacts' / required).is_file():
        raise SystemExit('Run make publish first: missing ' + required)
entries = {}
for folder in ('host', 'mock'):
    for path in (ROOT / 'artifacts' / folder).rglob('*'):
        if path.is_file() and path.suffix != '.pdb':
            entries[path.relative_to(ROOT / 'artifacts').as_posix()] = path
entries.update({
    'settings.example.json': ROOT / 'settings.example.json',
    'settings.pipeline.json': ROOT / 'samples/pipeline/settings.json',
    'examples/source.py': ROOT / 'samples/bench/source.py',
    'README.md': ROOT / 'artifacts/release/RELEASE-NOTES.md',
    'sdk/' + wheel.name: wheel,
    'sdk/' + contracts.name: contracts,
})
for path in entries.values():
    if not path.is_file():
        raise SystemExit('Missing asset: ' + str(path))
archive = out / f'dsn-{args.version}-portable.zip'
with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as bundle:
    for name, path in sorted(entries.items()):
        bundle.write(path, name)
assets = [archive]
for package in (wheel, contracts):
    target = out / package.name
    target.write_bytes(package.read_bytes())
    assets.append(target)
(out / 'SHA256SUMS').write_text(''.join(
    hashlib.sha256(path.read_bytes()).hexdigest() + '  ' + path.name + '\n' for path in assets))
for path in assets:
    print(path.relative_to(ROOT), path.stat().st_size, 'bytes')
