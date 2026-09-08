import type { ErrorAggregate, IWorkspace, IWorkspaceMessageContext, WorkspaceServices } from './contracts.js';

/** In-process projection stub. Does not define the deferred Admin wire contract. */
export class AdminStub implements IWorkspace {
  readonly name = 'admin';
  readonly #seen = new Map<string, number>();
  constructor(readonly services: WorkspaceServices) {}
  Process(context: IWorkspaceMessageContext): void {
    const lease = context.Checkout();
    context.Checkin(lease);
    this.services.errors.Report({ code: 'ADMIN_WIRE_DEFERRED', component: 'admin', workspace: this.name });
  }
  Project(snapshot: readonly ErrorAggregate[]): void {
    for (const item of snapshot) {
      const key = JSON.stringify(Object.entries(item.body).sort(([a], [b]) => a.localeCompare(b)));
      if (this.#seen.get(key) === item.count) continue;
      this.services.records.Append({ workspace: this.name, fields: {
        ...item.body, first_seen: item.first_seen, last_seen: item.last_seen, count: item.count,
      } });
      this.#seen.set(key, item.count);
    }
  }
}
