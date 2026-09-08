import test from 'node:test';
import assert from 'node:assert/strict';
import { DsnHost } from '../src/host.js';
import { DsnMock } from '../src/mock.js';
import { connect, eventually, notification } from '../src/client.js';
import { CollectEcho, gate } from './helpers.js';

test('N01 exact frame limit and adjacent boundaries', async t => {
  const frame = notification('s', ['echo']); const size = Buffer.byteLength(frame) - 1;
  for (const limit of [size - 1, size, size + 1]) {
    const echo = new CollectEcho(); const mock = new DsnMock(echo, { maxFrameBytes: limit });
    const port = await mock.Start(); t.after(() => mock.Stop());
    const socket = await connect(port); t.after(() => socket.destroy()); socket.write(frame);
    if (limit < size) { await eventually(() => socket.destroyed); assert.equal(echo.events.length, 0); }
    else { await eventually(() => echo.events.length === 1); assert.equal(socket.destroyed, false); }
    await mock.Stop(); assert.equal(mock.lifetime.Stats().references, 0);
  }
});

test('N02 split UTF-8 and incomplete frame detach', async t => {
  const echo = new CollectEcho(); const mock = new DsnMock(echo); const port = await mock.Start();
  t.after(() => mock.Stop()); const socket = await connect(port); t.after(() => socket.destroy());
  const frame = Buffer.from(notification('한글', ['echo'])); const split = frame.indexOf(Buffer.from('한')) + 1;
  socket.write(frame.subarray(0, split)); await new Promise(resolve => setTimeout(resolve, 10));
  socket.write(frame.subarray(split)); await eventually(() => echo.events.length === 1);
  assert.equal((echo.events[0]!.envelope as { source_id: string }).source_id, '한글');
  socket.end('{partial');
  await eventually(() => mock.rpc.sessions === 0);
  assert.ok(mock.errors.Snapshot().some(e => e.body.code === 'TRUNCATED_FRAME'));
  assert.equal(mock.lifetime.Stats().created, 1); assert.equal(mock.lifetime.Stats().references, 0);
});

test('N03 session capacity preserves established source and allows reuse', async t => {
  const echo = new CollectEcho(); const mock = new DsnMock(echo, { maxSessions: 1 });
  const port = await mock.Start(); t.after(() => mock.Stop());
  const first = await connect(port); t.after(() => first.destroy()); first.write(notification('one'));
  await eventually(() => echo.events.length === 1);
  const extra = await connect(port); t.after(() => extra.destroy()); await eventually(() => extra.destroyed);
  assert.equal(first.destroyed, false); first.destroy(); await eventually(() => mock.rpc.sessions === 0);
  const next = await connect(port); t.after(() => next.destroy()); next.write(notification('two'));
  await eventually(() => echo.events.length === 2);
  assert.ok(mock.errors.Snapshot().some(e => e.body.code === 'SESSION_LIMIT'));
});

test('N04 live memory exhaustion drops then recovers', async t => {
  const block = gate(); let calls = 0;
  const host = new DsnHost({ plugins: [], maxLiveMessages: 1, echo: new CollectEcho(), workspaces: [{ name: 'slow',
    async Process(ctx) { const lease = ctx.Checkout(); calls++; await block.promise; ctx.Checkin(lease); },
  }] });
  const { rpcPort } = await host.Start(); t.after(async () => { block.open(); await host.Stop(); });
  const socket = await connect(rpcPort); t.after(() => socket.destroy()); socket.write(notification('s', ['slow']));
  await eventually(() => calls === 1); socket.write(notification('s', ['slow']));
  await eventually(() => host.errors.Snapshot().some(e => e.body.code === 'MEMORY_FULL'));
  assert.equal(socket.destroyed, false); block.open(); await host.Drain();
  socket.write(notification('s', ['slow'])); await eventually(() => calls === 2); await host.Drain();
  assert.equal(host.lifetime.Stats().references, 0); assert.equal(host.lifetime.Stats().created, 2);
});

test('N05 concurrent and repeated drain Stop handles active and queued messages', async t => {
  const block = gate(); let calls = 0;
  const host = new DsnHost({ plugins: [], echo: new CollectEcho(), workspaces: [{ name: 'slow',
    async Process(ctx) { const lease = ctx.Checkout(); calls++; await block.promise; ctx.Checkin(lease); },
  }] });
  const { rpcPort } = await host.Start(); t.after(async () => { block.open(); await host.Stop(); });
  const socket = await connect(rpcPort); t.after(() => socket.destroy()); socket.write(notification('s', ['slow']));
  await eventually(() => calls === 1); socket.write(notification('s', ['slow']));
  await eventually(() => host.queue.size === 1);
  const stopping = host.Stop(); assert.equal(host.Stop(), stopping); block.open();
  assert.deepEqual(await stopping, { complete: true }); assert.equal(calls, 2);
  assert.deepEqual(await host.Stop(), { complete: true });
  assert.equal(host.lifetime.Stats().references, 0);
  await eventually(() => host.rpc.sessions === 0);
});

test('N06 View bind failure rolls back and released ports are reusable', async t => {
  const first = new DsnHost({ echo: new CollectEcho() }); const ports = await first.Start(); t.after(() => first.Stop());
  const failed = new DsnHost({ viewPort: ports.viewPort, echo: new CollectEcho() }); t.after(() => failed.Stop());
  await assert.rejects(failed.Start(), /EADDRINUSE/); assert.equal(failed.state, 'failed');
  await first.Stop();
  const next = new DsnHost({ port: ports.rpcPort, viewPort: ports.viewPort, echo: new CollectEcho() }); t.after(() => next.Stop());
  assert.deepEqual(await next.Start(), ports);
});

test('N07 Mock truncates display without changing reported payload size', async t => {
  const echo = new CollectEcho(); const mock = new DsnMock(echo); const port = await mock.Start(); t.after(() => mock.Stop());
  const socket = await connect(port); t.after(() => socket.destroy()); const payload = Buffer.alloc(257, 255);
  socket.write(notification('s', ['arbitrary'], payload)); await eventually(() => echo.events.length === 1);
  assert.equal(echo.events[0]!.payload_bytes, 257); assert.equal(echo.events[0]!.payload_hex, 'ff'.repeat(256));
  assert.equal(echo.events[0]!.truncated, true); assert.equal(mock.lifetime.Stats().references, 0);
});
