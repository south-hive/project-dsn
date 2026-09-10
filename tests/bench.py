"""Three application Source processes + optional browser + SQLite restart."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import urllib.request

sys.path.insert(0, str(Path(__file__).parent / 'sdk'))
from test_sources import ROOT, host, run

opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
with tempfile.TemporaryDirectory(prefix='dsn-bench-') as directory:
    settings = Path(directory) / 'settings.json'
    settings.write_text(json.dumps({'rpcPort':0,'viewPort':0,'dataDirectory':str(Path(directory)/'data')}))
    def get(address, path):
        with opener.open(address+path, timeout=5) as response:
            return json.load(response)
    with host(settings) as ready:
        env = {**os.environ, 'PYTHONPATH':str(ROOT/'sdk/source/python/src')}
        run(sys.executable, str(ROOT/'samples/bench/source.py'), '--port',str(ready['rpcPort']),'--count','2',env=env)
        deadline=time.monotonic()+10
        while True:
            try:
                records=get(ready['view'],'/view?workspaces=bench&fields=id,source_id,role,dut_id,run_id,message_id,processing_id')
                if len(records)==6: break
            except urllib.error.HTTPError as error:
                if error.code!=400: raise
            assert time.monotonic()<deadline, 'Six bench records were not persisted'
            time.sleep(.03)
        assert len({r['source_id'] for r in records})==3
        assert {r['role'] for r in records}=={'telemetry-app','test-app','orchestrator'}
        assert len(get(ready['view'],'/raw')['items'])==6
        if '--browser' in sys.argv:
            subprocess.run(['node',str(ROOT/'tests/presenter.mjs')],cwd=ROOT,
                           env={**os.environ,'DSN_VIEW_ADDRESS':ready['view']},check=True,timeout=90)
        saved=get(ready['view'],'/view?workspaces=bench&fields=id,message_id,processing_id')
    with host(settings) as ready:
        assert get(ready['view'],'/view?workspaces=bench&fields=id,message_id,processing_id')==saved
        assert len(get(ready['view'],'/raw')['items'])==6
print('PASS three application processes + SQLite archive + restart')
