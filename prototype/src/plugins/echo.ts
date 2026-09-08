import type { IWorkspace, IWorkspaceMessageContext, WorkspaceServices, FieldValue } from '../contracts.js';

export const apiVersion = 1;
export function createWorkspace(services: WorkspaceServices): IWorkspace {
  return new EchoWorkspace('echo', services, 'utf8');
}
export class EchoWorkspace implements IWorkspace {
  constructor(readonly name: string, readonly services: WorkspaceServices, readonly mode: 'hex' | 'utf8') {}
  Process(context: IWorkspaceMessageContext): void {
    const lease = context.Checkout();
    let fields: Record<string, FieldValue>;
    try {
      fields = { source_id: lease.envelope.source_id, event_time: lease.envelope.time,
        event_type: lease.envelope.event_type, message_id: lease.messageId,
        payload_bytes: lease.payload.byteLength,
        [this.mode === 'hex' ? 'payload_hex' : 'payload_utf8']:
          this.mode === 'hex' ? lease.payload.toHex() : lease.payload.toUtf8() };
    } finally { context.Checkin(lease); }
    // Only independent scalar copies cross the storage boundary.
    this.services.records.Append({ workspace: this.name, fields });
    this.services.echo.Emit({ kind: 'workspace.echo', workspace: this.name, ...fields });
  }
}
