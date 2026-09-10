"""Start an isolated local demo, or verify the complete pipeline with --check."""
import argparse
from contextlib import contextmanager
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import queue
import signal
import subprocess
import sys
import threading
import time
import urllib.request
import uuid

ROOT=Path(__file__).resolve().parents[2]
OPENER=urllib.request.build_opener(urllib.request.ProxyHandler({}))

@contextmanager
def host(settings, log):
    process=subprocess.Popen(['dotnet',str(ROOT/'src/Dsn.Host/bin/Release/net10.0/Dsn.Host.dll'),str(settings)],
                             cwd=ROOT,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True)
    output=queue.Queue()
    def read():
        with log.open('a') as file:
            for line in process.stdout:
                file.write(line);file.flush();output.put(line)
        output.put(None)
    thread=threading.Thread(target=read,daemon=True);thread.start()
    try:
        deadline=time.monotonic()+30
        while True:
            line=output.get(timeout=max(.01,deadline-time.monotonic()))
            if line is None: raise RuntimeError('Host exited; see '+str(log))
            try: ready=json.loads(line)
            except json.JSONDecodeError: continue
            if ready.get('status')=='ready': break
        yield ready
    finally:
        if process.poll() is None: process.terminate()
        try: process.wait(timeout=20)
        except subprocess.TimeoutExpired:
            process.kill();process.wait();raise RuntimeError('Host did not stop cleanly')
        thread.join(timeout=2)
        if process.returncode: raise RuntimeError('Host failed; see '+str(log))

def get(address,path):
    with OPENER.open(address+path,timeout=10) as response: return json.load(response)

def records(address):
    rows=[];after=0
    fields='id,dut_id,run_id,sequence,phase,assessment,latency_us,latency_ms,api_calls,interval_ms,api_calls_per_sec,io_errors,message_id,record_id'
    while True:
        try: page=get(address,'/view?workspaces=telemetry-demo&fields='+fields+'&limit=100&afterId='+str(after))
        except urllib.error.HTTPError as e:
            if e.code==400 and not rows:return []
            raise
        rows.extend(page)
        if len(page)<100:return rows
        after=page[-1]['id']

def verify(address,duts,samples,run_id):
    deadline=time.monotonic()+20
    while True:
        rows=records(address)
        if len(rows)==duts*samples:break
        if time.monotonic()>deadline:raise AssertionError(f'Expected {duts*samples} records; got {len(rows)}')
        time.sleep(.1)
    assert len({r['message_id'] for r in rows})==len(rows)
    assert len({r['record_id'] for r in rows})==len(rows)
    for dut in range(1,duts+1):
        items=[r for r in rows if r['dut_id']==f'dut-{dut:02}']
        assert [r['sequence'] for r in items]==list(range(samples))
        for r in items:
            phase=('normal','latency','errors','recovery')[(r['sequence']//12)%4]
            assert r['phase']==phase and r['run_id']==run_id
            assert r['assessment']==('error' if phase=='errors' else 'warning' if phase=='latency' else 'normal')
            assert abs(r['latency_ms']-r['latency_us']*.001)<1e-9
            assert abs(r['api_calls_per_sec']-r['api_calls']*1000/r['interval_ms'])<1e-9
    raw=[];after=0
    while True:
        page=get(address,'/raw?limit=100&afterId='+str(after))
        raw.extend(page['items'])
        if page['nextAfterId']==after:break
        after=page['nextAfterId']
    assert len(raw)==len(rows)
    assert {r['messageId'] for r in raw}=={r['message_id'] for r in rows}
    assert get(address,'/pipelines')['telemetry-demo']['filters']==['scale']
    return rows

def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--duts',type=int,default=4)
    p.add_argument('--samples',type=int,default=240)
    p.add_argument('--interval',type=float,default=.5)
    p.add_argument('--view-port',type=int,default=7071)
    p.add_argument('--check',action='store_true')
    p.add_argument('--browser',action='store_true',help='Optional Playwright UI check, with --check')
    a=p.parse_args()
    if not 2<=a.duts<=8 or not 1<=a.samples<=10000 or not .001<=a.interval<=60 or not 0<=a.view_port<=65535:
        p.error('duts=2..8, samples=1..10000, interval=.001..60, view-port=0..65535 required')
    if a.browser and not a.check:p.error('--browser requires --check')
    if a.check:a.samples=48;a.interval=.01
    run_id=uuid.uuid4().hex
    directory=ROOT/('artifacts/e2e' if a.check else 'data/e2e')/(datetime.now(timezone.utc).strftime('%Y%m%d-%H%M%S')+'-'+run_id[:8])
    directory.mkdir(parents=True)
    settings=directory/'settings.json'
    config=json.loads((ROOT/'samples/e2e/settings.json').read_text())
    config.update(rpcPort=0,viewPort=0 if a.check else a.view_port,dataDirectory=str(directory/'store'))
    settings.write_text(json.dumps(config,indent=2))
    env={**os.environ,'PYTHONPATH':str(ROOT/'sdk/source/python/src')}
    with host(settings,directory/'host.log') as ready:
        print('View: '+ready['view']+'/demo?run='+run_id,flush=True)
        print('Data: '+str(directory/'store/dsn.db'),flush=True)
        print('Scenario: normal -> latency -> errors -> recovery, 12 samples each',flush=True)
        source=subprocess.Popen([sys.executable,str(ROOT/'samples/e2e/source.py'),'--port',str(ready['rpcPort']),
            '--duts',str(a.duts),'--samples',str(a.samples),'--interval',str(a.interval),'--run-id',run_id],env=env,cwd=ROOT)
        try:
            if source.wait():raise RuntimeError('Demo Source failed')
            saved=verify(ready['view'],a.duts,a.samples,run_id)
            print(f'PASS Source -> Workspace -> scale -> SQLite -> HTTP: {len(saved)} results and originals',flush=True)
            if a.browser:
                subprocess.run(['node',str(ROOT/'tests/e2e-view.mjs')],cwd=ROOT,
                    env={**env,'DSN_VIEW_ADDRESS':ready['view'],'DSN_DEMO_COUNT':str(len(saved))},check=True,timeout=90)
            if not a.check:
                print('Source finished. View remains available; Ctrl+C stops Host. Data is preserved.',flush=True)
                threading.Event().wait()
        finally:
            if source.poll() is None:source.terminate()
            try:source.wait(timeout=10)
            except subprocess.TimeoutExpired:source.kill();source.wait()
    if a.check:
        with host(settings,directory/'host.log') as ready:
            assert verify(ready['view'],a.duts,a.samples,run_id)==saved
        print('PASS restart: original/record identity and derived values unchanged',flush=True)

if __name__=='__main__':
    def stop(signum, frame): raise KeyboardInterrupt
    signal.signal(signal.SIGTERM, stop)
    try: main()
    except KeyboardInterrupt: print('\nStopped; stored data retained.')
