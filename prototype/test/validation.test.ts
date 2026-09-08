import test from 'node:test';
import assert from 'node:assert/strict';
import { DsnHost } from '../src/host.js';
import { DsnMock } from '../src/mock.js';
import { connect, eventually, notification } from '../src/client.js';
import { CollectEcho, gate } from './helpers.js';
import type { IPayloadLease } from '../src/contracts.js';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

test('A01 RPC -> dynamic Workspaces -> records -> HTTP View/export; no reply', async t => {
  const host = new DsnHost({ echo: new CollectEcho() });
  const ports = await host.Start(); t.after(() => host.Stop());
  const socket = await connect(ports.rpcPort); t.after(() => socket.destroy());
  let replies = 0; socket.on('data', data => { replies += data.length; });
  socket.write(notification('a', ['echo', 'hex'], Buffer.from('hello')));
  await eventually(() => host.records.Query({ workspaces: ['echo', 'hex'] }).length === 2);
  await host.Drain();
  const rows = host.records.Query({ workspaces: ['echo', 'hex'] });
  assert.equal(rows[0]!.fields.message_id, rows[1]!.fields.message_id);
  const response = await fetch(`http://127.0.0.1:${ports.viewPort}/view?workspaces=hex&fields=source_id,payload_hex`);
  assert.deepEqual(await response.json(), [{ source_id: 'a', payload_hex: '68656c6c6f' }]);
  const exported = await fetch(`http://127.0.0.1:${ports.viewPort}/export?workspaces=echo,hex`);
  assert.equal((await exported.text()).split('\n').length, 2);
  assert.equal(host.lifetime.Stats().references, 0); assert.equal(replies, 0);
  assert.deepEqual(await host.Stop(), { complete: true });
});

test('A02 standalone Mock echoes opaque binary and releases message', async t => {
  const echo = new CollectEcho(); const mock = new DsnMock(echo);
  const port = await mock.Start(); t.after(() => mock.Stop());
  const socket = await connect(port); t.after(() => socket.destroy());
  let replies = 0; socket.on('data', data => { replies += data.length; });
  socket.write(notification('kernel-fixture', ['any-workspace'], Buffer.from([0, 255, 10, 128])));
  await eventually(() => echo.events.length === 1);
  assert.equal(echo.events[0]!.payload_hex, '00ff0a80');
  assert.equal(mock.lifetime.Stats().references, 0); assert.equal(replies, 0);
});

test('A03 fragmented and coalesced frames retain ingress FIFO', async t => {
  const host = new DsnHost({ echo: new CollectEcho() });
  const { rpcPort } = await host.Start(); t.after(() => host.Stop());
  const socket = await connect(rpcPort); t.after(() => socket.destroy());
  const first = notification('fifo', ['echo'], Buffer.from('1'));
  socket.write(first.slice(0, 13));
  await new Promise(resolve => setTimeout(resolve, 10));
  assert.equal(host.lifetime.Stats().created, 0);
  socket.write(first.slice(13) + notification('fifo', ['echo'], Buffer.from('2')) + notification('fifo', ['echo'], Buffer.from('3')));
  await eventually(() => host.records.Query({ workspaces: ['echo'] }).length === 3);
  assert.deepEqual(host.records.Query({ workspaces: ['echo'] }).map(row => row.fields.payload_utf8), ['1', '2', '3']);
});

test('A04 unsupported version drops, aggregates, then same connection recovers', async t => {
  const host = new DsnHost({ echo: new CollectEcho() });
  const { rpcPort } = await host.Start(); t.after(() => host.Stop());
  const socket = await connect(rpcPort); t.after(() => socket.destroy());
  const bad = notification('v', ['echo']).replace('"version":1', '"version":99');
  socket.write(bad + bad + notification('v', ['echo']));
  await eventually(() => host.records.Query({ workspaces: ['echo'] }).length === 1);
  await host.Drain();
  assert.equal(host.errors.Snapshot().find(e => e.body.code === 'UNSUPPORTED_VERSION')?.count, 2);
  assert.equal(host.records.Query({ workspaces: ['admin'] }).length, 1);
  assert.equal(socket.destroyed, false);
});

test('A05 oversized sender alone disconnects; reconnect is allowed', async t => {
  const host = new DsnHost({ echo: new CollectEcho(), maxFrameBytes: 512 });
  const { rpcPort } = await host.Start(); t.after(() => host.Stop());
  const good = await connect(rpcPort); const bad = await connect(rpcPort);
  t.after(() => { good.destroy(); bad.destroy(); });
  good.write(notification('good', ['echo'])); bad.write(notification('bad', ['echo']));
  await eventually(() => host.records.Query({ workspaces: ['echo'] }).length === 2);
  bad.write('x'.repeat(513)); await eventually(() => bad.destroyed && host.rpc.sessions === 1);
  good.write(notification('good', ['echo']));
  const retry = await connect(rpcPort); t.after(() => retry.destroy()); retry.write(notification('bad', ['echo']));
  await eventually(() => host.records.Query({ workspaces: ['echo'] }).length === 4);
  assert.equal(good.destroyed, false);
  assert.equal(host.errors.Snapshot().find(e => e.body.code === 'FRAME_TOO_LARGE')?.body.source_id, 'bad');
});

test('A06 duplicate attachment refused; detach permits the same source again', async t => {
  const host = new DsnHost({ echo: new CollectEcho() });
  const { rpcPort } = await host.Start(); t.after(() => host.Stop());
  const first = await connect(rpcPort); t.after(() => first.destroy()); first.write(notification('same', ['echo']));
  await eventually(() => host.records.Query({ workspaces: ['echo'] }).length === 1);
  const duplicate = await connect(rpcPort); t.after(() => duplicate.destroy()); duplicate.write(notification('same', ['echo']));
  await eventually(() => duplicate.destroyed);
  assert.equal(first.destroyed, false);
  first.destroy(); await eventually(() => host.rpc.sessions === 0);
  const next = await connect(rpcPort); t.after(() => next.destroy()); next.write(notification('same', ['echo']));
  await eventually(() => host.records.Query({ workspaces: ['echo'] }).length === 2);
});

test('A07 full magazine drops new messages without waiting or leaking', async t => {
  const block = gate(); let entered = 0;
  const host = new DsnHost({ plugins: [], maxQueueMessages: 1, echo: new CollectEcho(), workspaces: [{ name: 'slow',
    async Process(context) { const lease = context.Checkout(); entered++; await block.promise; context.Checkin(lease); },
  }] });
  const { rpcPort } = await host.Start(); t.after(async () => { block.open(); await host.Stop(); });
  const socket = await connect(rpcPort); t.after(() => socket.destroy());
  socket.write(notification('load', ['slow'])); await eventually(() => entered === 1);
  socket.write(notification('load', ['slow']).repeat(3));
  await eventually(() => host.errors.Snapshot().some(e => e.body.code === 'QUEUE_FULL' && e.count === 2));
  assert.equal(socket.destroyed, false);
  block.open(); await host.Drain();
  assert.equal(entered, 2); assert.equal(host.lifetime.Stats().references, 0);
  assert.equal(host.lifetime.Stats().created, host.lifetime.Stats().reclaimed);
});

test('A08 shutdown timeout retains active lease and discards waiting roots', async t => {
  const block = gate(); let active: IPayloadLease | undefined;
  const host = new DsnHost({ plugins: [], echo: new CollectEcho(), workspaces: [{ name: 'slow',
    async Process(context) { active = context.Checkout(); await block.promise; context.Checkin(active); },
  }] });
  const { rpcPort } = await host.Start(); t.after(async () => { block.open(); await host.Stop(); });
  const socket = await connect(rpcPort); t.after(() => socket.destroy());
  socket.write(notification('stop', ['slow'])); await eventually(() => active !== undefined);
  socket.write(notification('stop', ['slow'])); await eventually(() => host.lifetime.Stats().created === 2);
  assert.deepEqual(await host.Stop('discard', 15), { complete: false });
  assert.equal(host.state, 'stopping'); assert.equal(host.lifetime.Stats().liveMessages, 1);
  assert.equal(active!.payload.toUtf8(), 'hello DSN');
  assert.doesNotThrow(() => host.records.Query());
  block.open(); assert.deepEqual(await host.Stop(), { complete: true });
  assert.equal(host.lifetime.Stats().references, 0);
  assert.throws(() => active!.payload.byteLength);
});

test('A09 startup port conflict rolls back; running DSN remains usable', async t => {
  const first = new DsnHost({ echo: new CollectEcho() });
  const { rpcPort } = await first.Start(); t.after(() => first.Stop());
  const second = new DsnHost({ port: rpcPort, echo: new CollectEcho() });
  t.after(() => second.Stop()); await assert.rejects(second.Start(), /EADDRINUSE/);
  assert.equal(second.state, 'failed'); assert.throws(() => second.records.Query());
  const socket = await connect(rpcPort); t.after(() => socket.destroy()); socket.write(notification());
  await eventually(() => first.records.Query({ workspaces: ['echo'] }).length === 1);
});

test('A10 invalid View/export requests are contained', async t => {
  const host = new DsnHost({ echo: new CollectEcho() });
  const { viewPort } = await host.Start(); t.after(() => host.Stop());
  for (const path of ['/view?limit=-1', '/export?limit=NaN', '/view?fields=__proto__']) {
    const response = await fetch(`http://127.0.0.1:${viewPort}${path}`);
    assert.equal(response.status, 400); await response.text();
  }
  const response = await fetch(`http://127.0.0.1:${viewPort}/unknown`);
  assert.equal(response.status, 404); await response.text();
});

test('A11 malformed RPC and source identity changes close only their sessions', async t => {
  const mock = new DsnMock(new CollectEcho()); const port = await mock.Start(); t.after(() => mock.Stop());
  const malformed = await connect(port); t.after(() => malformed.destroy());
  malformed.write('{oops}\n'); await eventually(() => malformed.destroyed);
  const changed = await connect(port); t.after(() => changed.destroy());
  changed.write(notification('one') + notification('two')); await eventually(() => changed.destroyed);
  assert.ok(mock.errors.Snapshot().some(e => e.body.code === 'INVALID_RPC'));
  assert.ok(mock.errors.Snapshot().some(e => e.body.code === 'SOURCE_CHANGED'));
  assert.equal(mock.lifetime.Stats().references, 0);
});

test('A12 DSN and Mock CLI start, echo actual RPC, and exit cleanly on SIGTERM', { timeout: 10000 }, async t => {
  for (const mode of ['dsn', 'mock']) {
    const child = spawn(process.execPath, [fileURLToPath(new URL('../src/cli.js', import.meta.url)),
      '--rpc-port', '0', '--view-port', '0', ...(mode === 'mock' ? ['--mock'] : [])], { stdio: ['ignore', 'pipe', 'pipe'] });
    t.after(() => { if (child.exitCode === null) child.kill('SIGKILL'); });
    let output = ''; let stderr = '';
    child.stdout.on('data', chunk => { output += String(chunk); });
    child.stderr.on('data', chunk => { stderr += String(chunk); });
    const exited = new Promise<number | null>(resolve => child.once('exit', resolve));
    await eventually(() => output.includes('"kind":"ready"'));
    const ready = JSON.parse(output.split('\n')[0]!) as { endpoints: number | { rpcPort: number } };
    const socket = await connect(typeof ready.endpoints === 'number' ? ready.endpoints : ready.endpoints.rpcPort);
    t.after(() => socket.destroy()); socket.write(notification());
    await eventually(() => output.includes(mode === 'mock' ? 'mock.echo' : 'workspace.echo'));
    child.kill('SIGTERM'); assert.equal(await exited, 0, stderr);
    assert.ok(output.includes('"kind":"stopped"'));
    const stopped = JSON.parse(output.trim().split('\n').at(-1)!) as { lifetime: { references: number } };
    assert.equal(stopped.lifetime.references, 0); assert.equal(stderr, '');
  }
});
