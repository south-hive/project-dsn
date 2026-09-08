import type { IRecordExporter, IRecordQuery, IRecordStore, RecordInput, RecordQuery, StoredRecord, FieldValue } from './contracts.js';

/** Bounded echo storage. Append means memory acceptance, never durable commit. */
export class MemoryRecordStore implements IRecordStore, IRecordQuery, IRecordExporter {
  readonly #rows: { record: StoredRecord; bytes: number }[] = [];
  #nextId = 1; #bytes = 0; #closed = false;
  constructor(readonly maxRecords = 256, readonly maxBytes = 2 * 1024 * 1024) {
    for (const n of [maxRecords, maxBytes]) {
      if (!Number.isSafeInteger(n) || n < 1) throw new Error('invalid record limit');
    }
  }
  Append(input: RecordInput): void {
    if (this.#closed) throw new Error('record store closed');
    const fields: Record<string, FieldValue> = Object.create(null) as Record<string, FieldValue>;
    for (const [key, value] of Object.entries(input.fields)) {
      if (value !== null && !['string', 'number', 'boolean'].includes(typeof value)) throw new Error('record fields must be scalar copies');
      if (typeof value === 'number' && !Number.isFinite(value)) throw new Error('invalid record number');
      fields[key] = value;
    }
    const record = Object.freeze({ id: this.#nextId, workspace: input.workspace, fields: Object.freeze(fields) });
    const bytes = Buffer.byteLength(JSON.stringify(record));
    if (bytes > this.maxBytes) throw new Error('record too large');
    while (this.#rows.length >= this.maxRecords || this.#bytes + bytes > this.maxBytes) {
      this.#bytes -= this.#rows.shift()!.bytes;
    }
    this.#rows.push({ record, bytes }); this.#bytes += bytes; this.#nextId++;
  }
  Query(query: RecordQuery = {}): readonly StoredRecord[] {
    if (this.#closed) throw new Error('record store closed');
    const { offset = 0, limit = 100 } = query;
    if (!Number.isSafeInteger(offset) || offset < 0 || !Number.isSafeInteger(limit) || limit < 1 || limit > 1000) throw new Error('invalid query range');
    const selected = this.#rows.filter(({ record }) => !query.workspaces || query.workspaces.includes(record.workspace));
    return Object.freeze(selected.slice(offset, offset + limit).map(({ record }) => record));
  }
  Export(query: RecordQuery = {}): string { return this.Query(query).map(row => JSON.stringify(row)).join('\n'); }
  Close(): void { this.#closed = true; }
}

export interface ViewDefinition {
  readonly workspaces: readonly string[];
  readonly fields: readonly string[];
  readonly offset?: number;
  readonly limit?: number;
}
/** Only the record query interface is injected: no Workspace or registry dependency. */
export class ViewService {
  constructor(readonly query: IRecordQuery) {}
  Read(view: ViewDefinition): readonly Readonly<Record<string, FieldValue>>[] {
    if (view.workspaces.length > 32 || view.fields.length < 1 || view.fields.length > 32
      || view.fields.some(name => !/^[a-z][a-z0-9_]{0,63}$/.test(name))) throw new Error('invalid View definition');
    return this.query.Query({ workspaces: view.workspaces,
      ...(view.offset === undefined ? {} : { offset: view.offset }),
      ...(view.limit === undefined ? {} : { limit: view.limit }) }).map(record => {
      const result: Record<string, FieldValue> = Object.create(null) as Record<string, FieldValue>;
      for (const field of view.fields) {
        result[field] = field === 'workspace' ? record.workspace : field === 'id' ? record.id : record.fields[field] ?? null;
      }
      return Object.freeze(result);
    });
  }
}
