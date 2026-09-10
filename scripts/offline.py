#!/usr/bin/env python3
"""Export dependencies, verify their manifest, and test isolated local-feed builds."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import zipfile

ROOT=Path(__file__).resolve().parents[1]

def digest(path): return hashlib.sha256(path.read_bytes()).hexdigest()

def locks():
    paths=[]
    for folder in ('src','samples','tests'):
        for project in (ROOT/folder).rglob('*.csproj'):
            if any(part in ('bin','obj') for part in project.parts):continue
            path=project.with_name('packages.lock.json')
            if not path.is_file():raise RuntimeError('Missing lock: '+str(path))
            paths.append(path)
    return sorted(paths)

def source_files():return [ROOT/'global.json',ROOT/'requirements-dev.lock',*locks()]

def verify():
    path=ROOT/'vendor/manifest.json'
    if not path.exists():raise RuntimeError('Run make offline-pack online, or extract the dependency ZIP at the repository root')
    manifest=json.loads(path.read_text())
    if manifest.get('format')!=1:raise RuntimeError('Unsupported dependency manifest version')
    expected={p.relative_to(ROOT).as_posix():digest(p) for p in source_files()}
    if manifest['sources']!=expected:raise RuntimeError('Dependency bundle does not match current SDK/lock files; regenerate it')
    for name,sha in manifest['files'].items():
        relative=Path(name)
        if relative.is_absolute() or '..' in relative.parts or relative.parts[:2] not in (('vendor','nuget'),('vendor','python')):
            raise RuntimeError('Invalid dependency path')
        file=ROOT/relative
        if not file.is_file() or digest(file)!=sha:raise RuntimeError('Missing/modified dependency: '+name)
    print('PASS dependency manifest: '+str(len(manifest['files']))+' files',flush=True)
    return manifest

def pack():
    cache=Path(os.environ['NUGET_PACKAGES'])
    packages={}
    for path in locks():
        for target in json.loads(path.read_text())['dependencies'].values():
            for name,entry in target.items():
                if entry['type'] in ('Direct','Transitive'):
                    key=(name.lower(),entry['resolved'].lower())
                    if key in packages and packages[key]!=entry['contentHash']:raise RuntimeError('Conflicting package hash')
                    packages[key]=entry['contentHash']
    files=[]
    (ROOT/'vendor/nuget').mkdir(parents=True,exist_ok=True)
    (ROOT/'vendor/python').mkdir(parents=True,exist_ok=True)
    for (name,version),expected in sorted(packages.items()):
        file=cache/name/version/f'{name}.{version}.nupkg'
        if not file.is_file():raise RuntimeError('Restore missing dependency: '+str(file))
        # NuGet validates signed package content against the lock on restore. Preserve its bytes here.
        metadata=json.loads((file.parent/'.nupkg.metadata').read_text())
        if metadata['contentHash']!=expected:raise RuntimeError('Restored package differs from lock: '+name)
        target=ROOT/'vendor/nuget'/file.name
        shutil.copyfile(file,target);files.append(target)
    with tempfile.TemporaryDirectory(prefix='dsn-wheels-',dir=ROOT/'artifacts') as temporary:
        source=['--no-index','--find-links',str(ROOT/'vendor/python')] if os.environ.get('OFFLINE')=='1' else ['--index-url','https://pypi.org/simple']
        subprocess.run([sys.executable,'-m','pip','download',*source,'--only-binary=:all:',
                        '--require-hashes','-r',str(ROOT/'requirements-dev.lock'),'--dest',temporary],check=True)
        for file in sorted(Path(temporary).glob('*.whl')):
            target=ROOT/'vendor/python'/file.name;shutil.copyfile(file,target);files.append(target)
    manifest={'format':1,'sources':{p.relative_to(ROOT).as_posix():digest(p) for p in source_files()},
              'files':{p.relative_to(ROOT).as_posix():digest(p) for p in files}}
    manifest_path=ROOT/'vendor/manifest.json'
    manifest_path.write_text(json.dumps(manifest,indent=2)+'\n')
    verify()
    output=ROOT/'artifacts/dsn-offline-dependencies.zip'
    with zipfile.ZipFile(output,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as archive:
        for p in [*files,manifest_path,ROOT/'vendor/README.md']:
            archive.write(p,p.relative_to(ROOT).as_posix())
    output.with_suffix('.zip.sha256').write_text(digest(output)+'  '+output.name+'\n')
    print(str(output),output.stat().st_size,'bytes',flush=True)

def check():
    verify()
    # Deliberately start with no DSN packages or Python SDK build tools. Keep the environment for inspection.
    (ROOT/'.dev').mkdir(exist_ok=True)
    directory=Path(tempfile.mkdtemp(prefix='offline-check-',dir=ROOT/'.dev'))
    env={**os.environ,'OFFLINE':'1','DSN_DEV_DIR':str(directory),'PIP_NO_INDEX':'1'}
    # Reject accidental network access through conventional proxies as an extra check;
    # the actual guarantee here is explicit local-only package sources, not an OS sandbox.
    for key in ('http_proxy','https_proxy','HTTP_PROXY','HTTPS_PROXY','ALL_PROXY','all_proxy'):
        env[key]='http://127.0.0.1:9'
    env['NO_PROXY']=env['no_proxy']='127.0.0.1,localhost,::1'
    print('Fresh offline environment: '+str(directory),flush=True)
    subprocess.run(['make','-f','make/sdk.mk','sdk-check'],cwd=ROOT,env=env,check=True)
    subprocess.run(['bash','scripts/dev.sh','python','samples/e2e/run.py','--check'],cwd=ROOT,env=env,check=True)
    print('PASS local-feed restore/build, C# tests, SDK packaging/tests and demo with fresh caches',flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action',choices=['pack','verify','check']);args=parser.parse_args()
    (ROOT/'artifacts').mkdir(exist_ok=True)
    try: {'pack':pack,'verify':verify,'check':check}[args.action]()
    except (RuntimeError,KeyError) as e:raise SystemExit(str(e))
