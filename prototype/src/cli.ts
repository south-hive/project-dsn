import { pathToFileURL } from 'node:url';
import { resolve } from 'node:path';
import { parseArgs } from 'node:util';
import { DsnHost } from './host.js';
import { DsnMock } from './mock.js';

async function main(): Promise<void> {
  const { values } = parseArgs({ options: {
    mock: { type: 'boolean', default: false },
    host: { type: 'string', default: '127.0.0.1' },
    'rpc-port': { type: 'string', default: '7070' },
    'view-port': { type: 'string', default: '7071' },
    plugin: { type: 'string', multiple: true },
  } });
  const port = (value: string): number => {
    const n = Number(value);
    if (!Number.isSafeInteger(n) || n < 0 || n > 65535) throw new Error('port must be 0..65535');
    return n;
  };
  const options = { host: values.host, port: port(values['rpc-port']) };
  const host = values.mock ? new DsnMock(undefined, options) : new DsnHost({ ...options,
    viewPort: port(values['view-port']),
    ...(values.plugin ? { plugins: values.plugin.map(file => pathToFileURL(resolve(file))) } : {}),
  });
  const endpoints = await host.Start();
  console.log(JSON.stringify({ kind: 'ready', mode: values.mock ? 'mock' : 'dsn', endpoints }));
  let stopping = false;
  const stop = async (): Promise<void> => {
    if (stopping) return;
    stopping = true;
    const result = await host.Stop();
    console.log(JSON.stringify({ kind: 'stopped', result, lifetime: host.lifetime.Stats(), errors: host.errors.Snapshot() }));
    if (result && !result.complete) process.exitCode = 2;
  };
  for (const signal of ['SIGINT', 'SIGTERM'] as const) process.once(signal, () => { void stop().catch(error => {
    console.error(error); process.exitCode = 1;
  }); });
}
void main().catch(error => { console.error(error); process.exitCode = 1; });
