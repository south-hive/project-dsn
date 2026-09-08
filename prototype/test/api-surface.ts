import type { IWorkspaceMessageContext, IPayloadLease, WorkspaceServices } from '../src/contracts.js';

// Compile-only verification: tsc must reject these plugin operations.
export function unsupported(context: IWorkspaceMessageContext, lease: IPayloadLease, services: WorkspaceServices): void {
  // @ts-expect-error Workspace cannot allocate original buffers.
  context.TryCreate({});
  // @ts-expect-error Workspace cannot close the executor's invocation scope.
  context.Close();
  // @ts-expect-error Workspace cannot mutate the envelope's destinations.
  lease.envelope.workspace.push('other');
  // @ts-expect-error Payload has no mutable byte index API.
  lease.payload[0] = 1;
  // @ts-expect-error Record writer does not expose the query API.
  services.records.Query({});
  // @ts-expect-error Workspace cannot close the store.
  services.records.Close();
}
