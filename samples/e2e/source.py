"""Deterministic synthetic app telemetry: one OS process per DUT, no hardware access."""
import argparse
import json
import signal
import subprocess
import sys
import time
import uuid
from dsn_source import Source

PHASES = ('normal', 'latency', 'errors', 'recovery')

def payload(dut, sequence, run_id, interval):
    phase = PHASES[(sequence // 12) % 4]
    step = sequence % 12
    latency = {'normal': 80 + step * 3, 'latency': 700 + step * 40,
               'errors': 1400 + step * 50, 'recovery': 150 - step * 5}[phase] + dut * 7
    return dict(schema='dsn-demo.v1', pc_id='demo-pc', dut_id=f'dut-{dut:02}', run_id=run_id,
                sequence=sequence, phase=phase, interval_ms=max(1, round(interval * 1000)),
                latency_us=round(latency * (1 + .12 * (dut - 1))), read_iops=(12000 if phase in ('normal','recovery') else 4000) + dut * 100 + step * 60,
                write_iops=2000 + dut * 30 + step * 20, api_calls=100 + step * 5,
                io_errors=(step % 3 + 1) if phase == 'errors' else 0)

def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--port',type=int,default=7070)
    p.add_argument('--duts',type=int,default=4)
    p.add_argument('--samples',type=int,default=240)
    p.add_argument('--interval',type=float,default=.5)
    p.add_argument('--run-id',default=uuid.uuid4().hex)
    p.add_argument('--dut',type=int,help=argparse.SUPPRESS)
    a=p.parse_args()
    if not 2<=a.duts<=8 or not 1<=a.samples<=10000 or not .001<=a.interval<=60:
        p.error('duts=2..8, samples=1..10000, interval=.001..60 required')
    if a.dut is None:
        children=[]
        try:
            for dut in range(1,a.duts+1):
                children.append(subprocess.Popen([sys.executable,__file__,'--dut',str(dut),'--duts',str(a.duts),
                    '--port',str(a.port),'--samples',str(a.samples),'--interval',str(a.interval),'--run-id',a.run_id]))
            return int(any([c.wait() != 0 for c in children]))
        finally:
            for c in children:
                if c.poll() is None: c.terminate()
            for c in children:
                try: c.wait(timeout=5)
                except subprocess.TimeoutExpired: c.kill(); c.wait()
    with Source(f'demo.{a.run_id}.dut-{a.dut:02}',port=a.port) as source:
        for i in range(a.samples):
            data=payload(a.dut,i,a.run_id,a.interval)
            if not source.publish(json.dumps(data).encode(),['telemetry-demo']):
                raise RuntimeError('Demo Source queue rejected a record')
            time.sleep(a.interval)
        flushed=source.flush(10)
        print(f'dut-{a.dut:02}: {source.stats}',flush=True)
        return int(not flushed or source.stats.failed or source.stats.discarded)

if __name__=='__main__':
    def stop(signum, frame): raise KeyboardInterrupt
    signal.signal(signal.SIGTERM, stop)
    try: sys.exit(main())
    except KeyboardInterrupt: sys.exit(130)
