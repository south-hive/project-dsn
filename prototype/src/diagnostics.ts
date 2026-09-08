import type { ErrorAggregate, ErrorBody, IErrorSink, IEchoSink } from './contracts.js';
import type { Writable } from 'node:stream';

/** Basic error aggregation, not production monitoring. No payload retained. */
export class ErrorSink implements IErrorSink {
  readonly #items = new Map<string, ErrorAggregate>();
  #dropped = 0;
  constructor(readonly capacity = 128) {
    if (!Number.isSafeInteger(capacity) || capacity < 1) throw new Error('invalid error capacity');
  }
  Report(body: ErrorBody, time = new Date().toISOString()): void {
    const copy = Object.freeze({ ...body });
    const key = JSON.stringify(Object.entries(copy).sort(([a], [b]) => a.localeCompare(b)));
    const old = this.#items.get(key);
    if (old) {
      this.#items.set(key, Object.freeze({ body: old.body,
        first_seen: time < old.first_seen ? time : old.first_seen,
        last_seen: time > old.last_seen ? time : old.last_seen, count: old.count + 1 }));
    } else if (this.#items.size < this.capacity) {
      this.#items.set(key, Object.freeze({ body: copy, first_seen: time, last_seen: time, count: 1 }));
    } else {
      this.#dropped++; // Never recurse into Report when full.
    }
  }
  Snapshot(): readonly ErrorAggregate[] { return Object.freeze([...this.#items.values()]); }
  get dropped(): number { return this.#dropped; }
}

export class ConsoleEcho implements IEchoSink {
  #failed = false;
  #dropped = 0;
  constructor(readonly output: Writable = process.stdout, readonly maxPendingBytes = 64 * 1024) {
    output.on('error', () => { this.#failed = true; });
  }
  Emit(event: Readonly<Record<string, unknown>>): void {
    try {
      const line = JSON.stringify(event) + '\n';
      if (this.#failed || this.output.writableLength + Buffer.byteLength(line) > this.maxPendingBytes) {
        this.#dropped++; return;
      }
      this.output.write(line);
    } catch { this.#dropped++; }
  }
  get dropped(): number { return this.#dropped; }
}

export function detail(error: unknown): string {
  return (error instanceof Error ? error.message : String(error)).slice(0, 256);
}
