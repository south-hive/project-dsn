import type { IEchoSink } from './contracts.js';
import { ErrorSink, ConsoleEcho, detail } from './diagnostics.js';
import { MessageLifetime } from './lifetime.js';
import type { DecodedMessage } from './protocol.js';
import { RpcServer, type IRpcSink, type RpcOptions } from './rpc.js';

export class DsnMock implements IRpcSink {
  readonly errors = new ErrorSink();
  readonly lifetime = new MessageLifetime();
  readonly rpc: RpcServer;
  constructor(readonly echo: IEchoSink = new ConsoleEcho(), options: RpcOptions = {}) {
    this.rpc = new RpcServer(this, this.errors, undefined, options);
  }
  Start(): Promise<number> { return this.rpc.Start(); }
  Stop(): Promise<void> { return this.rpc.Stop(); }
  Accept(message: DecodedMessage): void {
    const root = this.lifetime.TryCreate(message);
    if (!root) { this.errors.Report({ code: 'MEMORY_FULL', component: 'mock' }); return; }
    const invocation = this.lifetime.OpenInvocation(root);
    try {
      const lease = invocation.context.Checkout();
      const bytes = lease.payload.byteLength;
      const event = { kind: 'mock.echo', envelope: lease.envelope,
        payload_bytes: bytes, payload_hex: lease.payload.toHex().slice(0, 512), truncated: bytes > 256 };
      invocation.context.Checkin(lease);
      this.echo.Emit(event);
    } catch (error) { this.errors.Report({ code: 'MOCK_FAILED', component: 'mock', detail: detail(error) }); }
    finally { invocation.Close(); root.Checkin(); }
  }
}
