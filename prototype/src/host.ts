import type { IEchoSink, IWorkspace, WorkspaceServices } from './contracts.js';
import { AdminStub } from './admin.js';
import { ConsoleEcho, ErrorSink, detail } from './diagnostics.js';
import { MessageLifetime } from './lifetime.js';
import { DEFAULT_PLUGINS, loadPlugins } from './plugin-loader.js';
import { EnvelopeV1, NotificationDecoder, type DecodedMessage } from './protocol.js';
import { RpcServer, type IRpcSink, type RpcOptions } from './rpc.js';
import { BulletinBoard, SequentialExecutor, SignalBuffer } from './runtime.js';
import { MemoryRecordStore, ViewService } from './storage.js';
import { ViewHttpServer } from './view-http.js';

export interface HostOptions extends RpcOptions {
  readonly viewPort?: number; readonly plugins?: readonly URL[];
  readonly workspaces?: readonly IWorkspace[]; readonly echo?: IEchoSink;
  readonly maxQueueMessages?: number; readonly maxQueueBytes?: number;
  readonly maxLiveMessages?: number; readonly maxLiveBytes?: number;
  readonly maxPayloadBytes?: number; readonly maxRecords?: number;
}
export class DsnHost implements IRpcSink {
  state: 'new' | 'starting' | 'running' | 'stopping' | 'stopped' | 'failed' = 'new';
  readonly errors = new ErrorSink();
  readonly lifetime: MessageLifetime;
  readonly queue: SignalBuffer;
  readonly board: BulletinBoard;
  readonly executor: SequentialExecutor;
  readonly records: MemoryRecordStore;
  readonly view: ViewService;
  readonly rpc: RpcServer;
  readonly http: ViewHttpServer;
  readonly admin: AdminStub;
  readonly #services: WorkspaceServices;
  #stopInFlight: Promise<{ complete: boolean }> | undefined;
  constructor(readonly options: HostOptions = {}) {
    this.lifetime = new MessageLifetime(options.maxLiveMessages, options.maxLiveBytes);
    this.queue = new SignalBuffer(options.maxQueueMessages, options.maxQueueBytes);
    this.board = new BulletinBoard(this.errors);
    this.executor = new SequentialExecutor(this.queue, this.board, this.lifetime, this.errors);
    this.records = new MemoryRecordStore(options.maxRecords);
    this.view = new ViewService(this.records);
    const echo = options.echo ?? new ConsoleEcho();
    // Facades prevent even accidental access to internal Query/Close/registry APIs.
    this.#services = Object.freeze({
      records: Object.freeze({ Append: this.records.Append.bind(this.records) }),
      errors: Object.freeze({ Report: this.errors.Report.bind(this.errors) }),
      echo: Object.freeze({ Emit: echo.Emit.bind(echo) }),
    });
    this.admin = new AdminStub(this.#services);
    this.rpc = new RpcServer(this, this.errors,
      new NotificationDecoder([new EnvelopeV1(options.maxPayloadBytes)]), options);
    this.http = new ViewHttpServer(this.view, this.records);
  }
  async Start(): Promise<{ rpcPort: number; viewPort: number }> {
    if (this.state !== 'new') throw new Error('Host can only start once');
    this.state = 'starting';
    try {
      this.board.Register(this.admin);
      for (const workspace of this.options.workspaces ?? []) this.board.Register(workspace);
      await loadPlugins(this.options.plugins ?? DEFAULT_PLUGINS, this.#services, this.board, this.errors);
      this.board.Seal();
      const viewPort = await this.http.Start(this.options.viewPort ?? 0, this.options.host);
      const rpcPort = await this.rpc.Start();
      this.state = 'running';
      this.#projectAdmin();
      return { rpcPort, viewPort };
    } catch (error) {
      this.errors.Report({ code: 'START_FAILED', component: 'host', detail: detail(error) });
      await this.rpc.Stop(); await this.http.Stop();
      this.queue.CompleteWrites(); this.queue.Discard(); this.records.Close();
      this.state = 'failed'; throw error;
    }
  }
  Accept(message: DecodedMessage): void {
    if (this.state !== 'running') {
      this.errors.Report({ code: 'HOST_NOT_RUNNING', component: 'ingress' }); return;
    }
    const root = this.lifetime.TryCreate(message);
    if (!root) {
      this.errors.Report({ code: 'MEMORY_FULL', component: 'ingress', source_id: message.envelope.source_id }); return;
    }
    if (!this.queue.TryEnqueueOwned(root)) {
      root.Checkin();
      this.errors.Report({ code: 'QUEUE_FULL', component: 'ingress', source_id: message.envelope.source_id }); return;
    }
    this.executor.Wake();
  }
  /** Harness/admin projection checkpoint, not a Source acknowledgement. */
  async Drain(): Promise<void> {
    await this.executor.Idle();
    if (this.state === 'running') this.#projectAdmin();
  }
  #projectAdmin(): void {
    try { this.admin.Project(this.errors.Snapshot()); }
    catch (error) { this.errors.Report({ code: 'ADMIN_PROJECTION_FAILED', component: 'host', detail: detail(error) }); }
  }
  Stop(mode: 'drain' | 'discard' = 'drain', timeoutMs = 1000): Promise<{ complete: boolean }> {
    if (!Number.isSafeInteger(timeoutMs) || timeoutMs < 0) return Promise.reject(new Error('invalid shutdown timeout'));
    if (this.state === 'starting') return Promise.reject(new Error('wait for Start before Stop'));
    if (this.#stopInFlight) return this.#stopInFlight;
    this.#stopInFlight = this.#stop(mode, timeoutMs).finally(() => { this.#stopInFlight = undefined; });
    return this.#stopInFlight;
  }
  async #stop(mode: 'drain' | 'discard', timeoutMs: number): Promise<{ complete: boolean }> {
    if (this.state === 'stopped' || this.state === 'failed') return { complete: true };
    this.state = 'stopping';
    await this.rpc.Stop(); await this.http.Stop();
    this.queue.CompleteWrites();
    if (mode === 'discard') this.#discard();
    let timer: ReturnType<typeof setTimeout> | undefined;
    const idle = await Promise.race([this.executor.Idle().then(() => true),
      new Promise<false>(resolve => { timer = setTimeout(() => resolve(false), timeoutMs); })]);
    clearTimeout(timer);
    if (!idle) {
      this.#discard();
      this.errors.Report({ code: 'STOP_INCOMPLETE', component: 'host' });
      // Keep the store and active message alive until Process actually returns.
      return { complete: false };
    }
    this.#projectAdmin();
    if (this.lifetime.Stats().references !== 0) {
      this.errors.Report({ code: 'STOP_REFERENCES_REMAIN', component: 'host' }); return { complete: false };
    }
    this.records.Close(); this.state = 'stopped';
    return { complete: true };
  }
  #discard(): void {
    const count = this.queue.Discard();
    if (count) this.errors.Report({ code: 'SHUTDOWN_DROP', component: 'host', detail: String(count) });
  }
}
