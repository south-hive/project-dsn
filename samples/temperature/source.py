"""Application-specific payload encoding; the SDK only transports bytes."""
import argparse
import json
from dsn_source import Source

parser = argparse.ArgumentParser()
parser.add_argument("--host", default="127.0.0.1")
parser.add_argument("--port", type=int, default=7070)
parser.add_argument("--count", type=int, default=3)
args = parser.parse_args()
if not 1 <= args.count <= 256:
    parser.error("count must be in 1..256")
with Source("temperature-python", args.host, args.port) as source:
    for sequence in range(1, args.count + 1):
        payload = json.dumps({"schema": "temperature.v1", "sensor": "lab-01",
                              "celsius": 20 + sequence * 0.5, "sequence": sequence}).encode("utf-8")
        if not source.publish(payload, ["temperature"]):
            raise SystemExit("Local queue rejected a sample")
    if not source.flush():
        raise SystemExit("Local send timeout")
    print(source.stats)
    if source.stats.sent != args.count:
        raise SystemExit("A local send failed; no automatic replay")
print("Local writes completed; verify persisted records through HTTP View.")
