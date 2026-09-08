import type { Envelope } from './contracts.js';

export interface DecodedMessage { readonly envelope: Envelope; readonly payload: Buffer }
export class DecodeError extends Error {
  constructor(readonly code: string, message: string) { super(message); }
}
export interface IMessageDecoder { readonly version: number; Decode(value: unknown): DecodedMessage }
function object(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
function text(value: unknown, name: string, max = 128): string {
  if (typeof value !== 'string' || value.length === 0 || value.length > max) {
    throw new DecodeError('INVALID_ENVELOPE', `invalid ${name}`);
  }
  return value;
}

export class EnvelopeV1 implements IMessageDecoder {
  readonly version = 1;
  constructor(readonly maxPayloadBytes = 16 * 1024) {
    if (!Number.isSafeInteger(maxPayloadBytes) || maxPayloadBytes < 0) {
      throw new Error('invalid payload limit');
    }
  }
  Decode(value: unknown): DecodedMessage {
    if (!object(value)) throw new DecodeError('INVALID_ENVELOPE', 'envelope must be an object');
    if (value.version !== 1) throw new DecodeError('UNSUPPORTED_VERSION', 'expected envelope v1');
    const source_id = text(value.source_id, 'source_id');
    const time = text(value.time, 'time', 40);
    if (!/^\d{4}-\d{2}-\d{2}T/.test(time) || !Number.isFinite(Date.parse(time))) {
      throw new DecodeError('INVALID_ENVELOPE', 'time must be ISO-8601');
    }
    const event_type = text(value.event_type, 'event_type', 64);
    if (!Array.isArray(value.workspace) || value.workspace.length < 1 || value.workspace.length > 32) {
      throw new DecodeError('INVALID_ENVELOPE', 'workspace must contain 1..32 names');
    }
    const workspace = Object.freeze(value.workspace.map((name: unknown) => {
      const result = text(name, 'workspace name', 64);
      if (!/^[a-z][a-z0-9-]*$/.test(result)) throw new DecodeError('INVALID_ENVELOPE', 'invalid workspace name');
      return result;
    }));
    const description = value.source_description;
    if (description !== undefined && (typeof description !== 'string' || description.length > 512)) {
      throw new DecodeError('INVALID_ENVELOPE', 'source_description must be a string <=512 characters');
    }
    if (typeof value.payload !== 'string' || value.payload.length > 4 * Math.ceil(this.maxPayloadBytes / 3)
      || !/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(value.payload)) {
      throw new DecodeError('INVALID_PAYLOAD', 'payload must be bounded canonical base64');
    }
    const payload = Buffer.from(value.payload, 'base64');
    if (payload.length > this.maxPayloadBytes || payload.toString('base64') !== value.payload) {
      throw new DecodeError('INVALID_PAYLOAD', 'payload must be bounded canonical base64');
    }
    const envelope: Envelope = Object.freeze({ version: 1, source_id, time, event_type, workspace,
      ...(description === undefined ? {} : { source_description: description }) });
    return { envelope, payload };
  }
}

/** A restricted notification-only profile, not a general JSON-RPC server. */
export class NotificationDecoder {
  readonly #versions = new Map<number, IMessageDecoder>();
  constructor(decoders: readonly IMessageDecoder[] = [new EnvelopeV1()]) {
    for (const decoder of decoders) {
      if (this.#versions.has(decoder.version)) throw new Error('duplicate decoder version');
      this.#versions.set(decoder.version, decoder);
    }
  }
  Decode(line: Uint8Array): DecodedMessage {
    let value: unknown;
    try { value = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(line)); }
    catch { throw new DecodeError('INVALID_RPC', 'invalid UTF-8 or JSON'); }
    if (!object(value) || value.jsonrpc !== '2.0' || value.method !== 'dsn.publish'
      || Object.hasOwn(value, 'id') || !object(value.params)) {
      throw new DecodeError('INVALID_RPC', 'expected dsn.publish notification without id');
    }
    const version = value.params.version;
    if (typeof version !== 'number' || !Number.isSafeInteger(version)) {
      throw new DecodeError('INVALID_ENVELOPE', 'version must be an integer');
    }
    const decoder = this.#versions.get(version);
    if (!decoder) throw new DecodeError('UNSUPPORTED_VERSION', `unsupported envelope version ${version}`);
    return decoder.Decode(value.params);
  }
}
