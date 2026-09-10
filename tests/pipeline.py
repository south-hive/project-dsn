"""Application SDK -> local filter chain -> SQLite -> replay/restart. No hardware access."""
import json
import os
from pathlib import Path
import sqlite3
import sys
import tempfile
import time
import urllib.request

sys.path.insert(0, str(Path(__file__).parent / 'sdk'))
from test_sources import ROOT, host
from dsn_source import Source
opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))

def get(address, path):
    with opener.open(address + path, timeout=5) as response:
        return json.load(response)

def rows(address):
    return get(address, '/view?workspaces=bench&fields=id,latency_ms,classification,message_id,record_id,node_id,pipeline_revision,replay_id')

def wait_rows(address, count):
    deadline = time.monotonic() + 10
    while time.monotonic() < deadline:
        try:
            result = rows(address)
            if len(result) == count:
                return result
        except urllib.error.HTTPError as error:
            if error.code != 400:
                raise
        time.sleep(.03)
    raise AssertionError('Expected result count ' + str(count))

with tempfile.TemporaryDirectory(prefix='dsn-pipeline-') as directory:
    settings = Path(directory) / 'settings.json'
    config = json.loads((ROOT / 'samples/pipeline/settings.json').read_text())
    config.update(rpcPort=0, viewPort=0, dataDirectory=str(Path(directory)/'data'))
    settings.write_text(json.dumps(config))
    with host(settings) as ready:
        with Source('pipeline-app', port=ready['rpcPort']) as source:
            for i, latency in enumerate([80, 100, 200]):
                payload = {'schema':'bench.v1','pc_id':'pc-01','dut_id':'dut-02','run_id':'run-01',
                           'instance_id':'app-start-01','role':'telemetry-app','sequence':i,'values':{'latency_us':latency}}
                assert source.publish(json.dumps(payload).encode(), ['bench'])
            assert source.flush(5) and source.stats.failed == 0
        saved = wait_rows(ready['view'], 2)
        assert [r['latency_ms'] for r in saved] == [.1,.2]
        assert all(r['classification']=='latency-observation' for r in saved)
        raw = get(ready['view'], '/raw')['items']
        assert len(raw) == 3
        original_id = raw[0]['messageId']
        old_revision = saved[0]['pipeline_revision']
        with sqlite3.connect(Path(directory)/'data/dsn.db') as db:
            assert db.execute('pragma journal_mode').fetchone()[0]=='wal'
            assert db.execute('select count(*) from raw').fetchone()[0]==3
            assert db.execute('select count(*) from records where workspace="bench"').fetchone()[0]==2
            assert db.execute('select typeof(payload) from raw limit 1').fetchone()[0]=='blob'
    # A later analysis retains previously excluded raw data and records a new pipeline revision.
    config['pipelines']['bench']['filters'][2]['options']['value'] = .07
    settings.write_text(json.dumps(config))
    with host(settings) as ready:
        assert rows(ready['view']) == saved
        request = urllib.request.Request(ready['view']+'/raw/1/replay',
            data=json.dumps({'workspaces':['bench']}).encode(), headers={'Content-Type':'application/json'})
        with opener.open(request, timeout=5) as response:
            assert response.status == 202
            replay = json.load(response)
        changed = wait_rows(ready['view'], 3)[-1]
        assert changed['message_id'] == original_id
        assert changed['latency_ms'] == .08
        assert changed['pipeline_revision'] != old_revision
        assert changed['replay_id'] == replay['replayId']
        assert len(get(ready['view'], '/raw')['items']) == 3
        assert len({r['record_id'] for r in rows(ready['view'])}) == 3
print('PASS application Source -> ordered filters -> SQLite, preserved dropped raw, revised replay and restart')
