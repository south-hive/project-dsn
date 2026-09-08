/** Supported Workspace surface. No queue, dispatcher, socket or allocation APIs. */
export interface Envelope {
  readonly version: number;
  readonly source_id: string;
  readonly source_description?: string;
  readonly time: string;
  readonly event_type: string;
  readonly workspace: readonly string[];
}

export interface ReadonlyPayload {
  readonly byteLength: number;
  at(index: number): number | undefined;
  toHex(): string;
  toUtf8(): string;
}

export interface IPayloadLease {
  readonly messageId: number;
  readonly envelope: Envelope;
  readonly payload: ReadonlyPayload;
}

export interface IWorkspaceMessageContext {
  Checkout(): IPayloadLease;
  /** false means this context already returned that lease; no second decrement. */
  Checkin(lease: IPayloadLease): boolean;
}

export interface IWorkspace {
  readonly name: string;
  Process(context: IWorkspaceMessageContext): void | Promise<void>;
}

export type FieldValue = string | number | boolean | null;
export interface RecordInput {
  readonly workspace: string;
  readonly fields: Readonly<Record<string, FieldValue>>;
}
export interface StoredRecord extends RecordInput { readonly id: number }
export interface IRecordStore { Append(record: RecordInput): void }
export interface RecordQuery {
  readonly workspaces?: readonly string[];
  readonly offset?: number;
  readonly limit?: number;
}
export interface IRecordQuery { Query(query: RecordQuery): readonly StoredRecord[] }
export interface IRecordExporter { Export(query: RecordQuery): string }

export interface ErrorBody {
  readonly code: string;
  readonly component: string;
  readonly source_id?: string;
  readonly workspace?: string;
  readonly detail?: string;
}
export interface ErrorAggregate {
  readonly body: ErrorBody;
  readonly first_seen: string;
  readonly last_seen: string;
  readonly count: number;
}
export interface IErrorSink { Report(body: ErrorBody, time?: string): void }
export interface IEchoSink { Emit(event: Readonly<Record<string, unknown>>): void }
export interface WorkspaceServices {
  readonly records: IRecordStore;
  readonly errors: IErrorSink;
  readonly echo: IEchoSink;
}
export interface WorkspacePlugin {
  readonly apiVersion: 1;
  createWorkspace(services: WorkspaceServices): IWorkspace;
}
