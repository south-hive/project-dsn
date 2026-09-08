import { createServer, type Server, type Socket, type AddressInfo } from 'node:net';
import type { IErrorSink } from './contracts.js';
import { DecodeError, NotificationDecoder, type DecodedMessage } from './protocol.js';
import { detail } from './diagnostics.js';

export interface IRpcSink { Accept(message: DecodedMessage): void }
export interface RpcOptions { readonly host?: string; readonly port?: number; readonly maxFrameBytes?: number; readonly maxSessions?: number }
interface Session { readonly socket: Socket; pending: Buffer; source?: string }

/** LF-framed, notification-only JSON-RPC-shaped input. Never sends application replies. */
export class RpcServer {
  readonly #server: Server;
  readonly #sessions = new Set<Session>();
  readonly #sources = new Map<string, Socket>();
  #accepting = false;
  constructor(readonly sink: IRpcSink, readonly errors: IErrorSink,
    readonly decoder = new NotificationDecoder(), readonly options: RpcOptions = {}) {
    for (const n of [options.maxFrameBytes ?? 64 * 1024, options.maxSessions ?? 32]) {
      if (!Number.isSafeInteger(n) || n < 1) throw new Error('invalid RPC limit');
    }
    this.#server = createServer(socket => this.#attach(socket));
    this.#server.on('error', error => errors.Report({ code: 'RPC_SERVER_ERROR', component: 'rpc', detail: detail(error) }));
  }
  async Start(): Promise<number> {
    await new Promise<void>((resolve, reject) => {
      const onError = (error: Error) => reject(error);
      this.#server.once('error', onError);
      this.#server.listen(this.options.port ?? 0, this.options.host ?? '127.0.0.1', () => {
        this.#server.removeListener('error', onError); this.#accepting = true; resolve();
      });
    });
    return (this.#server.address() as AddressInfo).port;
  }
  async Stop(): Promise<void> {
    this.#accepting = false;
    const closed = this.#server.listening
      ? new Promise<void>((resolve, reject) => this.#server.close(error => error ? reject(error) : resolve()))
      : Promise.resolve();
    for (const session of this.#sessions) session.socket.destroy();
    await closed;
  }
  get sessions(): number { return this.#sessions.size; }
  #report(session: Session, code: string, message?: string): void {
    this.errors.Report({ code, component: 'rpc',
      ...(session.source === undefined ? {} : { source_id: session.source }),
      ...(message === undefined ? {} : { detail: message }) });
  }
  #attach(socket: Socket): void {
    if (!this.#accepting || this.#sessions.size >= (this.options.maxSessions ?? 32)) {
      this.errors.Report({ code: 'SESSION_LIMIT', component: 'rpc' }); socket.destroy(); return;
    }
    const session: Session = { socket, pending: Buffer.alloc(0) };
    this.#sessions.add(session); socket.setNoDelay(true);
    socket.on('data', chunk => this.#data(session, typeof chunk === 'string' ? Buffer.from(chunk) : chunk));
    socket.on('error', error => this.#report(session, 'SESSION_ERROR', detail(error)));
    socket.on('close', () => {
      if (session.pending.length) this.#report(session, 'TRUNCATED_FRAME');
      session.pending = Buffer.alloc(0);
      this.#sessions.delete(session);
      if (session.source && this.#sources.get(session.source) === socket) this.#sources.delete(session.source);
    });
  }
  #data(session: Session, chunk: Buffer): void {
    let offset = 0;
    while (offset < chunk.length && this.#accepting && !session.socket.destroyed) {
      const newline = chunk.indexOf(10, offset);
      const end = newline === -1 ? chunk.length : newline;
      if (session.pending.length + end - offset > (this.options.maxFrameBytes ?? 64 * 1024)) {
        session.pending = Buffer.alloc(0);
        this.#report(session, 'FRAME_TOO_LARGE'); session.socket.destroy(); return;
      }
      session.pending = Buffer.concat([session.pending, chunk.subarray(offset, end)]);
      if (newline === -1) return;
      const frame = session.pending; session.pending = Buffer.alloc(0); offset = end + 1;
      try {
        const message = this.decoder.Decode(frame);
        const source = message.envelope.source_id;
        if (session.source !== undefined && session.source !== source) {
          this.#report(session, 'SOURCE_CHANGED'); session.socket.destroy(); return;
        }
        const existing = this.#sources.get(source);
        if (existing && existing !== session.socket && !existing.destroyed) {
          this.#report(session, 'SOURCE_ALREADY_ATTACHED', source); session.socket.destroy(); return;
        }
        session.source = source; this.#sources.set(source, session.socket);
        this.sink.Accept(message);
      } catch (error) {
        this.#report(session, error instanceof DecodeError ? error.code : 'INGRESS_FAILED', detail(error));
        if (error instanceof DecodeError && error.code === 'INVALID_RPC') {
          session.socket.destroy(); return;
        }
      }
    }
  }
}
