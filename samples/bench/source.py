"""Synthetic bench sources; each role runs in its own OS process. No hardware access."""
import argparse
import json
import os
import subprocess
import sys
import time
import uuid
from dsn_source import Source


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--host', default='127.0.0.1')
    parser.add_argument('--port', type=int, default=7070)
    parser.add_argument('--pc', default='bench-01')
    parser.add_argument('--dut', default='dut-01')
    parser.add_argument('--run', default='demo-run')
    parser.add_argument('--role', choices=['telemetry-app', 'test-app', 'orchestrator'])
    parser.add_argument('--count', type=int, default=10)
    args = parser.parse_args()
    if not 1 <= args.count <= 100000:
        parser.error('count must be 1..100000')
    if args.role is None:
        children = [subprocess.Popen([sys.executable, __file__, '--role', role, *sys.argv[1:]])
                    for role in ['telemetry-app', 'test-app', 'orchestrator']]
        codes = [child.wait() for child in children]
        return int(any(codes))
    instance = uuid.uuid4().hex
    # Unique process identity; shared run/DUT fields enable correlation across producers.
    with Source(f'{args.pc}.{args.role}.{instance}', args.host, args.port) as source:
        for sequence in range(args.count):
            values = {
                'telemetry-app': {'read_iops': 10000 + sequence * 300, 'latency_us': 85 + sequence * 4,
                                  'read_errors': 0, 'queue_depth': 32},
                'test-app': {'api': 'read', 'calls': 100 + sequence, 'errors': int(sequence == args.count - 1),
                             'interval_ms': 1000, 'test_case': 'sequential-read'},
                'orchestrator': {'state': 'completed' if sequence == args.count - 1 else 'running',
                                 'step': sequence, 'firmware': 'demo-fw-1'}
            }[args.role]
            payload = {'schema': 'bench.v1', 'pc_id': args.pc, 'dut_id': args.dut,
                       'run_id': args.run, 'instance_id': instance, 'role': args.role,
                       'sequence': sequence, 'values': values}
            if not source.publish(json.dumps(payload).encode(), ['bench']):
                print('Source queue full', file=sys.stderr)
            time.sleep(.03)
        flushed = source.flush(10)
        print(args.role, os.getpid(), source.stats)
        return int(not flushed or source.stats.failed > 0 or source.stats.rejected > 0)


if __name__ == '__main__':
    raise SystemExit(main())
