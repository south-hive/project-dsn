import type { Envelope, IPayloadLease, IWorkspaceMessageContext, ReadonlyPayload } from './contracts.js';
import type { DecodedMessage } from './protocol.js';

export interface LifetimeStats {
  readonly created: number; readonly reclaimed: number;
  readonly liveMessages: number; readonly liveBytes: number;
  readonly checkouts: number; readonly checkins: number; readonly references: number;
}
class Entry {
  references = 1;
  payload: Buffer | undefined;
  constructor(readonly id: number, readonly envelope: Envelope, payload: Buffer, readonly bytes: number) {
    this.payload = payload;
  }
}

/** Internal root handle. Transfer revokes the old handle without adding a reference. */
export class OwnedMessage {
  #entry: Entry | undefined;
  constructor(readonly lifetime: MessageLifetime, entry: Entry) { this.#entry = entry; }
  Entry(): Entry {
    if (!this.#entry) throw new Error('root is transferred or returned');
    return this.#entry;
  }
  get envelope(): Envelope { return this.Entry().envelope; }
  get bytes(): number { return this.Entry().bytes; }
  Transfer(): OwnedMessage {
    const entry = this.Entry();
    this.#entry = undefined;
    return new OwnedMessage(this.lifetime, entry);
  }
  Checkin(): void {
    const entry = this.Entry();
    this.#entry = undefined;
    this.lifetime.Release(entry);
  }
}

export interface Invocation {
  readonly context: IWorkspaceMessageContext;
  /** Called only after Process returns/rejects, never on a live-call timeout. */
  Close(): number;
}
export class MessageLifetime {
  #nextId = 1;
  #created = 0; #reclaimed = 0; #liveBytes = 0;
  #checkouts = 0; #checkins = 0; #references = 0;
  constructor(readonly maxMessages = 256, readonly maxBytes = 4 * 1024 * 1024) {
    for (const n of [maxMessages, maxBytes]) {
      if (!Number.isSafeInteger(n) || n < 1) throw new Error('invalid lifetime limit');
    }
  }
  TryCreate(decoded: DecodedMessage): OwnedMessage | undefined {
    const bytes = decoded.payload.length + Buffer.byteLength(JSON.stringify(decoded.envelope));
    if (this.#created - this.#reclaimed >= this.maxMessages || this.#liveBytes + bytes > this.maxBytes) return;
    const envelope = Object.freeze({ ...decoded.envelope, workspace: Object.freeze([...decoded.envelope.workspace]) });
    // One ownership copy at ingress. No per-Workspace byte copies in Checkout.
    const entry = new Entry(this.#nextId++, envelope, Buffer.from(decoded.payload), bytes);
    this.#created++; this.#liveBytes += bytes; this.#references++;
    return new OwnedMessage(this, entry);
  }
  OpenInvocation(root: OwnedMessage): Invocation {
    if (root.lifetime !== this) throw new Error('foreign root');
    const entry = root.Entry();
    let closed = false;
    const leases = new Map<IPayloadLease, { active: boolean }>();
    const Checkin = (lease: IPayloadLease): boolean => {
      const state = leases.get(lease);
      if (!state) throw new Error('foreign lease');
      if (!state.active) return false;
      state.active = false;
      this.Release(entry);
      return true;
    };
    const context: IWorkspaceMessageContext = Object.freeze({
      Checkout: (): IPayloadLease => {
        if (closed || !entry.payload) throw new Error('invocation is closed');
        const state = { active: true };
        const read = (): Buffer => {
          if (!state.active || !entry.payload) throw new Error('lease is returned');
          return entry.payload;
        };
        const payload: ReadonlyPayload = Object.freeze({
          get byteLength() { return read().length; },
          at: (index: number) => {
            const bytes = read();
            return Number.isSafeInteger(index) && index >= 0 && index < bytes.length ? bytes[index] : undefined;
          },
          toHex: () => read().toString('hex'),
          toUtf8: () => read().toString('utf8'),
        });
        const lease: IPayloadLease = Object.freeze({
          get messageId() { read(); return entry.id; },
          get envelope() { read(); return entry.envelope; },
          get payload() { read(); return payload; },
        });
        leases.set(lease, state);
        entry.references++; this.#references++; this.#checkouts++;
        return lease;
      }, Checkin,
    });
    return { context, Close: (): number => {
      if (closed) return 0;
      closed = true;
      let returned = 0;
      for (const lease of leases.keys()) if (Checkin(lease)) returned++;
      return returned;
    } };
  }
  /** Internal only: exported Workspace contracts never expose this operation. */
  Release(entry: Entry): void {
    if (entry.references < 1 || !entry.payload) throw new Error('invalid reference decrement');
    entry.references--; this.#references--; this.#checkins++;
    if (entry.references === 0) {
      entry.payload = undefined;
      this.#liveBytes -= entry.bytes;
      this.#reclaimed++;
    }
  }
  Stats(): LifetimeStats {
    return Object.freeze({ created: this.#created, reclaimed: this.#reclaimed,
      liveMessages: this.#created - this.#reclaimed, liveBytes: this.#liveBytes,
      checkouts: this.#checkouts, checkins: this.#checkins, references: this.#references });
  }
}
