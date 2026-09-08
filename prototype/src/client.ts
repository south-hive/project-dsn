import { createConnection, type Socket } from 'node:net';

/** Test-only RPC client. Not a nonblocking production Source SDK. */
export async function connect(port: number, host = '127.0.0.1'): Promise<Socket> {
  const socket = createConnection({ port, host });
  socket.on('error', () => { /* harness callers observe connection/close state */ });
  await new Promise<void>((resolve, reject) => {
    socket.once('connect', resolve); socket.once('error', reject);
  });
  return socket;
}
export function notification(source = 'demo-source', workspaces: readonly string[] = ['echo', 'hex'],
  payload = Buffer.from('hello DSN')): string {
  return JSON.stringify({ jsonrpc: '2.0', method: 'dsn.publish', params: {
    version: 1, source_id: source, time: '2026-09-08T00:00:00.000Z',
    event_type: 'normal', workspace: workspaces, payload: payload.toString('base64'),
  } }) + '\n';
}
export async function eventually(check: () => boolean, timeoutMs = 2000): Promise<void> {
  const deadline = performance.now() + timeoutMs;
  while (!check()) {
    if (performance.now() > deadline) throw new Error('condition timed out');
    await new Promise(resolve => setTimeout(resolve, 5));
  }
}
