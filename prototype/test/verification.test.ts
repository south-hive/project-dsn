import { test } from 'node:test';
import assert from 'node:assert/strict';
import { Writable } from 'node:stream';
import type { IPayloadLease, IWorkspace, WorkspaceServices } from '../src/contracts.js';
import { ErrorSink, ConsoleEcho } from '../src/diagnostics.js';
import { DecodeError, EnvelopeV1, NotificationDecoder } from '../src/protocol.js';
import { MessageLifetime } from '../src/lifetime.js';
import { BulletinBoard, NoPolicy, SequentialExecutor, SignalBuffer } from '../src/runtime.js';
import { MemoryRecordStore, ViewService } from '../src/storage.js';
import { AdminStub } from '../src/admin.js';
import { loadPlugins, DEFAULT_PLUGINS } from '../src/plugin-loader.js';
import { notification } from '../src/client.js';
import { CollectEcho, gate, message } from './helpers.js';

test('V01: opaque binary payload and frozen envelope are decoded without schema interpretation', () => {
  const decoded = message(['echo', 'hex'], 'fio', Buffer.from([0, 255, 128, 1]));
  assert.deepEqual([...decoded.payload], [0, 255, 128, 1]);
  assert.ok(Object.isFrozen(decoded.envelope)); assert.ok(Object.isFrozen(decoded.envelope.workspace));
});
test('V02: malformed RPC and unsupported envelope versions have distinct failures', () => {
  const decoder = new NotificationDecoder();
  const base = JSON.parse(notification()) as { params: { version: unknown }; id?: number };
  for (const value of [true, '1', 1.1]) {
    base.params.version = value;
    assert.throws(() => decoder.Decode(Buffer.from(JSON.stringify(base))), (e: unknown) => e instanceof DecodeError && e.code === 'INVALID_ENVELOPE');
  }
  base.params.version = 2;
  assert.throws(() => decoder.Decode(Buffer.from(JSON.stringify(base))), (e: unknown) => e instanceof DecodeError && e.code === 'UNSUPPORTED_VERSION');
  base.params.version = 1; base.id = 1;
  assert.throws(() => decoder.Decode(Buffer.from(JSON.stringify(base))), /without id/);
  for (const bytes of [Buffer.from('{'), Buffer.from([255]), Buffer.from('[]')]) assert.throws(() => decoder.Decode(bytes));
});
test('V03: version lookup can use a separate decoder and rejects duplicate registrations', () => {
  let used = false;
  const decoder = new NotificationDecoder([{ version: 2, Decode: () => { used = true; return message(); } }]);
  const request = JSON.parse(notification()) as { params: { version: number } }; request.params.version = 2;
  decoder.Decode(Buffer.from(JSON.stringify(request))); assert.ok(used);
  assert.throws(() => new NotificationDecoder([new EnvelopeV1(), new EnvelopeV1()]), /duplicate/);
});
test('V04: base64 size, canonical encoding, required fields, and empty payload choices are enforced', () => {
  const decoder = new EnvelopeV1(2);
  const params = (JSON.parse(notification()) as { params: Record<string, unknown> }).params;
  for (const payload of ['Zg=', 'Zh==', '!!!!', 'YWJj']) assert.throws(() => decoder.Decode({ ...params, payload }));
  assert.equal(decoder.Decode({ ...params, payload: '' }).payload.length, 0);
  for (const override of [{ source_id: '' }, { time: 'not-time' }, { workspace: [] }, { source_description: {} }]) {
    assert.throws(() => decoder.Decode({ ...params, payload: '', ...override }));
  }
});
test('V05: root transfer revokes the previous owner, and ingress protects against caller buffer mutation', () => {
  const lifetime = new MessageLifetime(); const decoded = message();
  const original = lifetime.TryCreate(decoded)!; decoded.payload.fill(0);
  const root = original.Transfer(); assert.throws(() => original.Checkin(), /transferred/);
  const invocation = lifetime.OpenInvocation(root); const lease = invocation.context.Checkout();
  assert.equal(lease.payload.toUtf8(), 'hello');
  invocation.context.Checkin(lease); invocation.Close(); root.Checkin();
  assert.throws(() => root.Checkin(), /returned/); assert.equal(lifetime.Stats().references, 0);
});
test('V06: two Workspaces share one original and cannot obtain a mutable backing buffer', () => {
  const lifetime = new MessageLifetime(); const root = lifetime.TryCreate(message())!;
  const a = lifetime.OpenInvocation(root); const b = lifetime.OpenInvocation(root);
  const la = a.context.Checkout(); const lb = b.context.Checkout();
  assert.equal(la.messageId, lb.messageId); assert.equal(la.envelope, lb.envelope);
  assert.equal(lifetime.Stats().created, 1); assert.equal(lifetime.Stats().references, 3);
  assert.equal(la.payload.at('buffer' as unknown as number), undefined);
  assert.ok(Object.isFrozen(la)); assert.ok(Object.isFrozen(la.payload));
  assert.deepEqual(Object.keys(a.context).sort(), ['Checkin', 'Checkout']);
  a.context.Checkin(la); assert.equal(lb.payload.toUtf8(), 'hello');
  b.context.Checkin(lb); a.Close(); b.Close(); root.Checkin();
  assert.equal(lifetime.Stats().reclaimed, 1);
});
test('V07: double Checkin is idempotent and cached payload access is revoked', () => {
  const lifetime = new MessageLifetime(); const root = lifetime.TryCreate(message())!;
  const invocation = lifetime.OpenInvocation(root); const lease = invocation.context.Checkout(); const payload = lease.payload;
  assert.equal(invocation.context.Checkin(lease), true);
  assert.equal(invocation.context.Checkin(lease), false);
  assert.throws(() => payload.toHex(), /returned/); assert.throws(() => lease.envelope, /returned/);
  assert.equal(invocation.Close(), 0); assert.equal(invocation.Close(), 0);
  assert.throws(() => invocation.context.Checkout(), /closed/); root.Checkin();
  assert.equal(lifetime.Stats().checkins, 2);
});
test('V08: foreign lease rejection does not decrement someone else’s reference', () => {
  const lifetime = new MessageLifetime(); const root = lifetime.TryCreate(message())!;
  const a = lifetime.OpenInvocation(root); const b = lifetime.OpenInvocation(root); const lease = a.context.Checkout();
  assert.throws(() => b.context.Checkin(lease), /foreign/); assert.equal(lifetime.Stats().references, 2);
  assert.equal(a.Close(), 1); b.Close(); root.Checkin(); assert.equal(lifetime.Stats().references, 0);
});
test('V09: lifetime exhaustion rejects allocation and capacity is reusable after final return', () => {
  const lifetime = new MessageLifetime(1); const root = lifetime.TryCreate(message())!;
  assert.equal(lifetime.TryCreate(message()), undefined); root.Checkin();
  lifetime.TryCreate(message())!.Checkin();
  assert.equal(lifetime.Stats().liveMessages, 0); assert.equal(lifetime.Stats().liveBytes, 0);
  assert.equal(new MessageLifetime(1, 1).TryCreate(message()), undefined);
  assert.equal(lifetime.Stats().created + lifetime.Stats().checkouts - lifetime.Stats().checkins, 0);
});
test('V10: no_policy is FIFO; enqueue rejection preserves caller ownership; closed writers reject', () => {
  const lifetime = new MessageLifetime(); const queue = new SignalBuffer(1);
  assert.equal(queue.policy.name, 'no_policy');
  const a = lifetime.TryCreate(message(['echo'], 'a'))!; const b = lifetime.TryCreate(message(['echo'], 'b'))!;
  assert.equal(queue.TryEnqueueOwned(a), true); assert.throws(() => a.Checkin());
  assert.equal(queue.TryEnqueueOwned(b), false); assert.equal(b.envelope.source_id, 'b');
  const received = queue.Take()!; assert.equal(received.envelope.source_id, 'a'); received.Checkin();
  queue.CompleteWrites(); assert.equal(queue.TryEnqueueOwned(b), false); b.Checkin();
  assert.equal(lifetime.Stats().references, 0);
});
test('V11: queue policy is an explicit extension point and invalid selection preserves queued roots', () => {
  const lifetime = new MessageLifetime();
  const queue = new SignalBuffer(2, 4096, { name: 'bad-test-policy', SelectIndex: () => 100 });
  queue.TryEnqueueOwned(lifetime.TryCreate(message())!);
  assert.throws(() => queue.Take(), /invalid queue policy/);
  assert.equal(queue.Discard(), 1); assert.equal(lifetime.Stats().references, 0);
  assert.equal(new NoPolicy().SelectIndex(20), 0);
});
test('V12: registry rejects duplicate names; destination deduplication and missing targets preserve valid routing', () => {
  const errors = new ErrorSink(); const board = new BulletinBoard(errors);
  const workspace: IWorkspace = { name: 'echo', Process() {} };
  assert.ok(board.Register(workspace)); assert.equal(board.Register(workspace), false);
  board.Seal(); assert.throws(() => board.Register({ name: 'later', Process() {} }), /sealed/);
  assert.deepEqual(board.Resolve(['echo', 'missing', 'echo'], 's'), [workspace]);
  assert.deepEqual(errors.Snapshot().map(e => e.body.code), ['DUPLICATE_WORKSPACE', 'UNKNOWN_WORKSPACE']);
});
test('V13: executor is FIFO and sequential across asynchronous Workspace calls', async () => {
  const errors = new ErrorSink(); const board = new BulletinBoard(errors); const memory = new MessageLifetime(); const queue = new SignalBuffer();
  const lock = gate(); const events: string[] = []; let active = 0; let peak = 0;
  for (const name of ['a', 'b']) board.Register({ name, async Process(ctx) {
    active++; peak = Math.max(peak, active); const lease = ctx.Checkout();
    events.push(`${name}:${lease.envelope.source_id}`);
    if (events.length === 1) await lock.promise;
    ctx.Checkin(lease); active--;
  } });
  board.Seal();
  for (const source of ['first', 'second']) queue.TryEnqueueOwned(memory.TryCreate(message(['a', 'b'], source))!);
  const executor = new SequentialExecutor(queue, board, memory, errors); executor.Wake();
  await new Promise(resolve => setImmediate(resolve)); assert.deepEqual(events, ['a:first']);
  lock.open(); await executor.Idle();
  assert.deepEqual(events, ['a:first', 'b:first', 'a:second', 'b:second']); assert.equal(peak, 1);
  assert.equal(memory.Stats().references, 0);
});
test('V14: failure and leaked lease cleanup still allow the next Workspace to use the original', async () => {
  const errors = new ErrorSink(); const board = new BulletinBoard(errors); const memory = new MessageLifetime(); const queue = new SignalBuffer();
  let leaked: IPayloadLease | undefined; let received = '';
  board.Register({ name: 'broken', Process(ctx) { leaked = ctx.Checkout(); throw new Error('parse failure'); } });
  board.Register({ name: 'good', Process(ctx) { const lease = ctx.Checkout(); received = lease.payload.toUtf8(); ctx.Checkin(lease); } }); board.Seal();
  queue.TryEnqueueOwned(memory.TryCreate(message(['broken', 'good']))!);
  const executor = new SequentialExecutor(queue, board, memory, errors); await executor.Idle();
  assert.equal(received, 'hello'); assert.throws(() => leaked!.payload, /returned/);
  assert.equal(memory.Stats().references, 0); assert.equal(memory.Stats().reclaimed, 1);
  assert.deepEqual(errors.Snapshot().map(e => e.body.code), ['WORKSPACE_FAILED', 'LEASE_LEAK_CLEANED']);
});
test('V15: stored records are independent scalar copies with bounded retention and explicit closure', () => {
  const store = new MemoryRecordStore(2); const fields = { text: 'first' };
  store.Append({ workspace: 'echo', fields }); fields.text = 'mutated';
  assert.equal(store.Query({})[0]!.fields.text, 'first');
  store.Append({ workspace: 'echo', fields: { text: 'second' } }); store.Append({ workspace: 'hex', fields: { text: 'third' } });
  assert.deepEqual(store.Query({}).map(r => r.id), [2, 3]);
  assert.ok(Object.isFrozen(store.Query({})[0]!.fields));
  assert.equal(store.Export({ workspaces: ['hex'] }).split('\n').length, 1);
  assert.throws(() => store.Query({ limit: -1 })); store.Close(); assert.throws(() => store.Query({}), /closed/);
});
test('V16: View only needs IRecordQuery and projects chosen fields without touching a Workspace', () => {
  let calls = 0;
  const view = new ViewService({ Query: () => { calls++; return [{ id: 7, workspace: 'echo', fields: { value: 'ok' } }]; } });
  const rows = view.Read({ workspaces: ['echo'], fields: ['workspace', 'value', 'absent'] });
  assert.deepEqual(JSON.parse(JSON.stringify(rows)), [{ workspace: 'echo', value: 'ok', absent: null }]);
  assert.equal(calls, 1); assert.throws(() => view.Read({ workspaces: [], fields: ['__proto__'] }));
});
test('V17: error equality excludes event times and key ordering; capacity failure never recurses', () => {
  const errors = new ErrorSink(1);
  errors.Report({ code: 'X', component: 'test' }, '2026-09-08T00:00:02.000Z');
  errors.Report({ component: 'test', code: 'X' }, '2026-09-08T00:00:01.000Z');
  errors.Report({ code: 'Y', component: 'test' });
  errors.Report({ code: 'X', component: 'test' }, '2026-09-08T00:00:03.000Z');
  assert.equal(errors.Snapshot().length, 1); assert.equal(errors.Snapshot()[0]!.count, 3);
  assert.equal(errors.Snapshot()[0]!.first_seen, '2026-09-08T00:00:01.000Z');
  assert.equal(errors.Snapshot()[0]!.last_seen, '2026-09-08T00:00:03.000Z'); assert.equal(errors.dropped, 1);
});
test('V18: slow console output is bounded and output failure is contained', () => {
  const writable = new Writable({ highWaterMark: 1, write(_chunk, _encoding, _callback) {} });
  const output = new ConsoleEcho(writable, 100);
  for (let i = 0; i < 30; i++) output.Emit({ value: 'a'.repeat(20) });
  assert.ok(writable.writableLength <= 100); assert.ok(output.dropped > 0);
  writable.emit('error', new Error('closed console')); assert.doesNotThrow(() => output.Emit({ x: 1 })); writable.destroy();
});
test('V19: Admin projection is separate from error aggregation and uses ordinary stored records', () => {
  const errors = new ErrorSink(); const records = new MemoryRecordStore();
  const admin = new AdminStub({ errors, records, echo: new CollectEcho() });
  errors.Report({ code: 'A', component: 'test' }); admin.Project(errors.Snapshot()); admin.Project(errors.Snapshot());
  assert.equal(records.Query({}).length, 1);
  errors.Report({ code: 'A', component: 'test' }); admin.Project(errors.Snapshot());
  assert.equal(records.Query({}).at(-1)!.fields.count, 2); assert.equal(errors.Snapshot().length, 1);
});
test('V20: startup plugin imports reject incompatible versions and duplicate names without replacing valid plugins', async () => {
  const errors = new ErrorSink(); const records = new MemoryRecordStore(); const board = new BulletinBoard(errors);
  const services: WorkspaceServices = { errors, records, echo: new CollectEcho() };
  await loadPlugins([...DEFAULT_PLUGINS, DEFAULT_PLUGINS[0]!, new URL('./fixtures/incompatible-plugin.js', import.meta.url)], services, board, errors);
  assert.deepEqual(board.Names(), ['echo', 'hex']);
  assert.deepEqual(errors.Snapshot().map(e => e.body.code), ['DUPLICATE_WORKSPACE', 'PLUGIN_LOAD_FAILED']);
});
