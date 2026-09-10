"""DSN source transport. Payload schemas belong to the application."""

import base64
from collections import deque
from dataclasses import dataclass
from datetime import datetime, timezone
import ipaddress
import json
import math
import re
import socket
import threading


@dataclass(frozen=True)
class Stats:
    accepted: int
    sent: int
    failed: int
    rejected: int
    discarded: int


def _identifier(value, limit):
    if not isinstance(value, str) or not re.fullmatch(r"[A-Za-z0-9._:-]{1,%d}" % limit, value):
        raise ValueError("metadata must be an ASCII identifier")
    return value


class Source:
    """One fixed source_id per connection. Explicit close/context manager required.

    publish copies/encodes bytes and takes a short lock; it never waits for network
    I/O or queue space. This is not a hard real-time or lock-free API.
    """

    def __init__(self, source_id, host="127.0.0.1", port=7070, *, capacity=256, timeout=1.0):
        self._source_id = _identifier(source_id, 128)
        address = ipaddress.ip_address(host)  # Numeric addresses avoid unbounded DNS work.
        if isinstance(port, bool) or not isinstance(port, int) or not 1 <= port <= 65535:
            raise ValueError("port must be in 1..65535")
        if isinstance(capacity, bool) or not isinstance(capacity, int) or not 1 <= capacity <= 65536:
            raise ValueError("capacity must be in 1..65536")
        if not math.isfinite(timeout) or not 0 < timeout <= 60:
            raise ValueError("timeout must be in (0, 60] seconds")
        self._address = (str(address), port)
        self._family = socket.AF_INET6 if address.version == 6 else socket.AF_INET
        self._capacity, self._timeout = capacity, timeout
        self._condition = threading.Condition()
        self._queue = deque()
        self._closed = self._active = False
        self._accepted = self._sent = self._failed = self._rejected = self._discarded = 0
        self._thread = threading.Thread(target=self._run, name="dsn-source", daemon=True)
        self._thread.start()

    def publish(self, payload, workspaces, *, event_type="normal"):
        """True means local queue admission only; False means full or closed."""
        _identifier(event_type, 64)
        if not isinstance(payload, (bytes, bytearray, memoryview)):
            raise TypeError("payload must be bytes-like")
        if memoryview(payload).nbytes > 16384:
            raise ValueError("payload exceeds 16384 bytes")
        if not isinstance(workspaces, (list, tuple)) or not 1 <= len(workspaces) <= 32:
            raise ValueError("workspaces must contain 1..32 names")
        targets = list(workspaces)
        if any(not isinstance(w, str) or not re.fullmatch(r"[a-z][a-z0-9-]{0,63}", w) for w in targets):
            raise ValueError("invalid workspace name")
        frame = (json.dumps({"jsonrpc": "2.0", "method": "dsn.publish", "params": {
            "version": 1, "source_id": self._source_id,
            "time": datetime.now(timezone.utc).isoformat(timespec="microseconds"),
            "event_type": event_type, "workspace": targets,
            "payload": base64.b64encode(bytes(payload)).decode("ascii")
        }}, separators=(",", ":")) + "\n").encode("utf-8")
        with self._condition:
            if self._closed or len(self._queue) >= self._capacity:
                self._rejected += 1
                return False
            self._queue.append(frame)
            self._accepted += 1
            self._condition.notify_all()
            return True

    def flush(self, timeout=5.0):
        """Wait for local attempts, including failed ones. Does not await server ACK."""
        if not math.isfinite(timeout) or timeout < 0:
            raise ValueError("flush timeout must be finite and nonnegative")
        with self._condition:
            return self._condition.wait_for(lambda: not self._queue and not self._active, timeout)

    @property
    def stats(self):
        with self._condition:
            return Stats(self._accepted, self._sent, self._failed, self._rejected, self._discarded)

    def close(self):
        """Discard queued frames and wait for the current bounded network attempt."""
        with self._condition:
            self._closed = True
            self._discarded += len(self._queue)
            self._queue.clear()
            self._condition.notify_all()
        self._thread.join()

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.close()

    def _run(self):
        connection = None
        try:
            while True:
                with self._condition:
                    self._condition.wait_for(lambda: self._closed or self._queue)
                    if self._closed:
                        return
                    frame = self._queue.popleft()
                    self._active = True
                success = False
                try:
                    if connection is None:
                        connection = socket.socket(self._family, socket.SOCK_STREAM)
                        connection.settimeout(self._timeout)
                        connection.connect(self._address)
                    connection.sendall(frame)
                    success = True
                except OSError:
                    if connection is not None:
                        connection.close()
                        connection = None
                    # Never replay a possibly partially sent frame. Later frames may reconnect.
                finally:
                    with self._condition:
                        if success:
                            self._sent += 1
                        else:
                            self._failed += 1
                        self._active = False
                        self._condition.notify_all()
        finally:
            if connection is not None:
                connection.close()
