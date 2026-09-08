import test from 'node:test';
import assert from 'node:assert/strict';
import { EnvelopeV1 } from '../src/protocol.js';
import { MessageLifetime } from '../src/lifetime.js';
import { SignalBuffer, BulletinBoard, SequentialExecutor } from '../src/runtime.js';
import { ErrorSink } from '../src/diagnostics.js';
import { MemoryRecordStore } from '../src/storage.js';
import { AdminStub } from '../src/admin.js';
import { notification } from '../src/client.js';
import { message, CollectEcho } from './helpers.js';

test('B01 payload boundary and invalid configuration', () => {
  const decoder = new EnvelopeV1(3);
  for (const size of [2, 3, 4]) {
    const params: unknown = JSON.parse(notification('s', ['echo'], Buffer.alloc(size))).params;
    if (size <= 3) assert.equal(decoder.Decode(params).payload.length, size);
    else assert.throws(() => decoder.Decode(params));
  }
  for (const limit of [NaN, Infinity, -1, 1.5]) assert.throws(() => new EnvelopeV1(limit));
  assert.equal(new EnvelopeV1(0).Decode(JSON.parse(notification('s', ['echo'], Buffer.alloc(0))).params).payload.length, 0);
});

test('B02 queue byte limit preserves rejected ownership and is reusable', () => {
  const memory = new MessageLifetime(); const root = memory.TryCreate(message())!;
  const bytes = root.bytes; const queue = new SignalBuffer(10, bytes);
  assert.ok(queue.TryEnqueueOwned(root));
  const next = memory.TryCreate(message())!; assert.equal(queue.TryEnqueueOwned(next), false);
  queue.Take()!.Checkin(); assert.ok(queue.TryEnqueueOwned(next)); queue.Discard();
  const small = new SignalBuffer(10, bytes - 1); const last = memory.TryCreate(message())!;
  assert.equal(small.TryEnqueueOwned(last), false); last.Checkin();
  assert.equal(memory.Stats().references, 0);
});

test('B03 timestamps do not reorder and mixed destinations are resolved once', async () => {
  const memory = new MessageLifetime(); const queue = new SignalBuffer(); const errors = new ErrorSink();
  const board = new BulletinBoard(errors); const seen: string[] = [];
  board.Register({ name: 'echo', Process(ctx) { const lease = ctx.Checkout(); seen.push(lease.envelope.source_id); ctx.Checkin(lease); } });
  for (const [source, time] of [['first', '2030-01-01T00:00:00Z'], ['second', '2020-01-01T00:00:00Z']] as const) {
    const decoded = message(['missing', 'echo', 'echo'], source);
    queue.TryEnqueueOwned(memory.TryCreate({ ...decoded, envelope: { ...decoded.envelope, time } })!);
  }
  await new SequentialExecutor(queue, board, memory, errors).Idle();
  assert.deepEqual(seen, ['first', 'second']); assert.equal(errors.Snapshot().length, 2);
  assert.equal(memory.Stats().references, 0);
});

test('B04 asynchronous failure and storage failure after Checkin preserve next target', async () => {
  const memory = new MessageLifetime(); const queue = new SignalBuffer(); const errors = new ErrorSink();
  const board = new BulletinBoard(errors); const store = new MemoryRecordStore(); store.Close(); let good = false;
  board.Register({ name: 'parse', async Process(ctx) { ctx.Checkout(); await Promise.resolve(); throw new Error('parse'); } });
  board.Register({ name: 'save', Process(ctx) { const lease = ctx.Checkout(); const text = lease.payload.toUtf8(); ctx.Checkin(lease); store.Append({ workspace: 'save', fields: { text } }); } });
  board.Register({ name: 'good', Process(ctx) { const lease = ctx.Checkout(); good = lease.payload.toUtf8() === 'hello'; ctx.Checkin(lease); } });
  queue.TryEnqueueOwned(memory.TryCreate(message(['parse', 'save', 'good']))!);
  await new SequentialExecutor(queue, board, memory, errors).Idle();
  assert.ok(good); assert.equal(memory.Stats().references, 0);
  assert.equal(errors.Snapshot().filter(e => e.body.code === 'WORKSPACE_FAILED').length, 2);
});

test('B05 record byte boundary, export values and pagination', () => {
  const row = { workspace: 'echo', fields: { value: 'abc' } };
  const bytes = Buffer.byteLength(JSON.stringify({ id: 1, ...row }));
  assert.throws(() => new MemoryRecordStore(10, bytes - 1).Append(row));
  const exact = new MemoryRecordStore(10, bytes); exact.Append(row); exact.Append(row);
  assert.deepEqual(JSON.parse(exact.Export()), { id: 2, ...row });
  const store = new MemoryRecordStore();
  for (const value of ['a', 'b', 'c']) store.Append({ workspace: 'echo', fields: { value } });
  assert.deepEqual(JSON.parse(store.Export({ offset: 1, limit: 1 })), { id: 2, workspace: 'echo', fields: { value: 'b' } });
  assert.deepEqual(store.Query({ offset: 100 }), []);
});

test('B06 each error body field participates in equality under capacity', () => {
  const base = { code: 'c', component: 'p', source_id: 's', workspace: 'w', detail: 'd' };
  const errors = new ErrorSink(6); errors.Report(base);
  for (const key of Object.keys(base)) errors.Report({ ...base, [key]: 'changed' });
  assert.equal(errors.Snapshot().length, 6);
  errors.Report({ code: 'overflow', component: 'x' }); errors.Report(base);
  assert.equal(errors.dropped, 1); assert.equal(errors.Snapshot()[0]!.count, 2);
});

test('B07 Admin failed projection can retry the same snapshot', () => {
  const errors = new ErrorSink(); const store = new MemoryRecordStore(); let fail = true;
  const admin = new AdminStub({ errors, echo: new CollectEcho(), records: { Append(record) {
    if (fail) throw new Error('unavailable'); store.Append(record);
  } } });
  errors.Report({ code: 'c', component: 'p' });
  assert.throws(() => admin.Project(errors.Snapshot())); fail = false;
  admin.Project(errors.Snapshot()); admin.Project(errors.Snapshot());
  assert.equal(store.Query().length, 1);
});
