import type { IErrorSink, IWorkspace } from './contracts.js';
import { detail } from './diagnostics.js';
import { OwnedMessage, MessageLifetime } from './lifetime.js';

export interface IQueuePolicy { readonly name: string; SelectIndex(size: number): number }
export class NoPolicy implements IQueuePolicy {
  readonly name = 'no_policy';
  SelectIndex(_size: number): number { return 0; }
}
export interface ISignalBufferWriter { TryEnqueueOwned(root: OwnedMessage): boolean }
export interface ISignalBufferReader { Take(): OwnedMessage | undefined }
export class SignalBuffer implements ISignalBufferReader, ISignalBufferWriter {
  readonly #items: OwnedMessage[] = [];
  #bytes = 0;
  #closed = false;
  constructor(readonly maxMessages = 128, readonly maxBytes = 2 * 1024 * 1024,
    readonly policy: IQueuePolicy = new NoPolicy()) {
    for (const n of [maxMessages, maxBytes]) {
      if (!Number.isSafeInteger(n) || n < 1) throw new Error('invalid queue limit');
    }
  }
  TryEnqueueOwned(root: OwnedMessage): boolean {
    if (this.#closed || this.#items.length >= this.maxMessages || this.#bytes + root.bytes > this.maxBytes) return false;
    const bytes = root.bytes;
    this.#items.push(root.Transfer()); this.#bytes += bytes;
    return true;
  }
  Take(): OwnedMessage | undefined {
    if (this.#items.length === 0) return;
    const index = this.policy.SelectIndex(this.#items.length);
    if (!Number.isSafeInteger(index) || index < 0 || index >= this.#items.length) throw new Error('invalid queue policy index');
    const root = this.#items.splice(index, 1)[0]!;
    this.#bytes -= root.bytes;
    return root.Transfer();
  }
  CompleteWrites(): void { this.#closed = true; }
  Discard(): number {
    const count = this.#items.length;
    for (const root of this.#items.splice(0)) root.Checkin();
    this.#bytes = 0;
    return count;
  }
  get size(): number { return this.#items.length; }
  get bytes(): number { return this.#bytes; }
}

export interface IBulletinBoard { Resolve(names: readonly string[], source: string): readonly IWorkspace[] }
export class BulletinBoard implements IBulletinBoard {
  readonly #registry = new Map<string, IWorkspace>();
  #sealed = false;
  constructor(readonly errors: IErrorSink) {}
  Register(workspace: IWorkspace): boolean {
    if (this.#sealed) throw new Error('registry is sealed');
    if (!/^[a-z][a-z0-9-]{0,63}$/.test(workspace.name) || typeof workspace.Process !== 'function') {
      this.errors.Report({ code: 'INVALID_PLUGIN', component: 'registry' }); return false;
    }
    if (this.#registry.has(workspace.name)) {
      this.errors.Report({ code: 'DUPLICATE_WORKSPACE', component: 'registry', workspace: workspace.name }); return false;
    }
    this.#registry.set(workspace.name, workspace); return true;
  }
  Seal(): void { this.#sealed = true; }
  Names(): readonly string[] { return Object.freeze([...this.#registry.keys()]); }
  Resolve(names: readonly string[], source: string): readonly IWorkspace[] {
    const result: IWorkspace[] = [];
    // Prototype policy: first occurrence wins, missing target does not suppress valid ones.
    for (const name of new Set(names)) {
      const workspace = this.#registry.get(name);
      if (workspace) result.push(workspace);
      else this.errors.Report({ code: 'UNKNOWN_WORKSPACE', component: 'routing', source_id: source, workspace: name });
    }
    return result;
  }
}

export interface IWorkspaceExecutor { Wake(): void; Idle(): Promise<void> }
export class SequentialExecutor implements IWorkspaceExecutor {
  #running = false;
  #scheduled = false;
  readonly #waiters: (() => void)[] = [];
  constructor(readonly queue: SignalBuffer, readonly board: IBulletinBoard,
    readonly lifetime: MessageLifetime, readonly errors: IErrorSink) {}
  Wake(): void {
    if (this.#running || this.#scheduled || this.queue.size === 0) return;
    this.#scheduled = true;
    setImmediate(() => { this.#scheduled = false; void this.#run(); });
  }
  Idle(): Promise<void> {
    if (!this.#running && !this.#scheduled && this.queue.size === 0) return Promise.resolve();
    this.Wake();
    return new Promise(resolve => this.#waiters.push(resolve));
  }
  async #run(): Promise<void> {
    this.#running = true;
    try {
      let root: OwnedMessage | undefined;
      while ((root = this.queue.Take())) {
        try {
          for (const workspace of this.board.Resolve(root.envelope.workspace, root.envelope.source_id)) {
            const invocation = this.lifetime.OpenInvocation(root);
            try { await workspace.Process(invocation.context); }
            catch (error) {
              this.errors.Report({ code: 'WORKSPACE_FAILED', component: 'executor',
                source_id: root.envelope.source_id, workspace: workspace.name, detail: detail(error) });
            } finally {
              if (invocation.Close() > 0) this.errors.Report({ code: 'LEASE_LEAK_CLEANED', component: 'executor',
                source_id: root.envelope.source_id, workspace: workspace.name });
            }
          }
        } finally { root.Checkin(); }
      }
    } catch (error) {
      this.errors.Report({ code: 'EXECUTOR_FAILED', component: 'executor', detail: detail(error) });
      this.queue.Discard();
    } finally {
      this.#running = false;
      for (const resolve of this.#waiters.splice(0)) resolve();
    }
  }
}
