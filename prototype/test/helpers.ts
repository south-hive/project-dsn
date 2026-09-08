import type { IEchoSink } from '../src/contracts.js';
import { NotificationDecoder, type DecodedMessage } from '../src/protocol.js';
import { notification } from '../src/client.js';
export class CollectEcho implements IEchoSink {
  readonly events: Readonly<Record<string, unknown>>[] = [];
  Emit(event: Readonly<Record<string, unknown>>): void { this.events.push(event); }
}
export function message(workspaces: readonly string[] = ['echo'], source = 'source', payload = Buffer.from('hello')): DecodedMessage {
  return new NotificationDecoder().Decode(Buffer.from(notification(source, workspaces, payload)));
}
export function gate(): { promise: Promise<void>; open: () => void } {
  let open!: () => void;
  const promise = new Promise<void>(resolve => { open = resolve; });
  return { promise, open };
}
