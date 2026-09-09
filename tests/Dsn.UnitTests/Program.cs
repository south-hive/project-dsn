using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Dsn.Contracts;
using Dsn.Runtime;
using Dsn.Ingress;
using Dsn.Persistence;
using Dsn.Diagnostics;
using Dsn.View;
using Dsn.Workspaces.Examples;

var suite = new Suite();
await suite.Test("version, envelope, opaque bytes and strict notification validation", async () =>
{
    var protocol = new NotificationProtocol();
    var message = protocol.Decode(Suite.Frame("a", ["echo"], [0, 255, 10]));
    Suite.Equal("00ff0a", Convert.ToHexString(message.Payload).ToLowerInvariant());
    Suite.Equal(0, protocol.Decode(Suite.Frame("a", ["echo"], [])).Payload.Length);
    foreach (var frame in new[] { "[]", "{}", "{", "{\"jsonrpc\":\"2.0\",\"method\":\"dsn.publish\",\"id\":1,\"params\":{}}" })
        Suite.Throws<ProtocolException>(() => protocol.Decode(Encoding.UTF8.GetBytes(frame)));
    var valid = Encoding.UTF8.GetString(Suite.Frame("a", ["echo"], [1]));
    foreach (var broken in new[] { valid.Replace("\"version\":1", "\"version\":2"), valid.Replace("\"version\":1", "\"version\":\"1\""), valid.Replace("AQ==", "AR=="), valid.Replace("AQ==", "A Q=="), valid.Replace("2026-09-08", "2026-02-30") })
        Suite.Throws<ProtocolException>(() => protocol.Decode(Encoding.UTF8.GetBytes(broken)));
    Suite.Throws<ProtocolException>(() => new NotificationProtocol(0).Decode(Suite.Frame("a", ["echo"], [1])));
    await Task.CompletedTask;
});
await suite.Test("shared original, concurrent checkin, use-after-checkin and context cleanup", async () =>
{
    var lifetime = new Lifetime();
    var owned = lifetime.Create(Suite.Message("a", ["echo"], [1, 2]))!;
    using var context = new MessageContext(owned);
    var lease = context.Checkout(); var payload = lease.Payload;
    Suite.Equal((byte)2, payload.At(1));
    var returns = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => context.Checkin(lease))));
    Suite.Equal(1, returns.Count(x => x));
    Suite.Throws<ObjectDisposedException>(() => payload.Copy());
    var retained = context.Checkout(); context.Dispose();
    Suite.Throws<ObjectDisposedException>(() => retained.Payload.Copy());
    Suite.Throws<ObjectDisposedException>(() => context.Checkout());
    owned.Release(); Suite.Equal(0L, lifetime.Stats.References); Suite.Equal(1L, lifetime.Stats.Reclaimed);
});
await suite.Test("FIFO, distinct targets, unknown destination and failed Workspace isolation", async () =>
{
    var errors = new ErrorSink(); var seen = new List<string>(); IPayloadLease? leaked = null;
    await using var runtime = new DsnRuntime(errors);
    runtime.Register(new DelegateWorkspace("broken", (c, _) => { leaked = c.Checkout(); throw new IOException(); }));
    runtime.Register(new DelegateWorkspace("good", (c, _) => { var l = c.Checkout(); seen.Add(l.Payload.ToUtf8()); c.Checkin(l); return ValueTask.CompletedTask; }));
    Suite.Check(!runtime.Register(new DelegateWorkspace("good", (_, _) => ValueTask.CompletedTask)));
    runtime.Start(); Suite.Throws<InvalidOperationException>(() => runtime.Register(new DelegateWorkspace("late", (_, _) => ValueTask.CompletedTask)));
    for (var i = 0; i < 20; i++) Suite.Check(runtime.TrySubmit(Suite.Message("a", ["unknown", "broken", "good", "good"], Encoding.UTF8.GetBytes(i.ToString()))));
    await runtime.DrainAsync(new CancellationTokenSource(5000).Token);
    Suite.Equal(string.Join(',', Enumerable.Range(0, 20)), string.Join(',', seen));
    Suite.Throws<ObjectDisposedException>(() => leaked!.Payload.Copy());
    Suite.Equal(0L, runtime.LifetimeStats.References);
    Suite.Equal(20L, errors.Snapshot().Single(x => x.Body.Code == "WORKSPACE_FAILED").Count);
});
await suite.Test("queue saturation and shutdown retain active original until completion", async () =>
{
    var errors = new ErrorSink(); var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
    IPayloadLease? active = null;
    await using var runtime = new DsnRuntime(errors, capacity: 1);
    runtime.Register(new DelegateWorkspace("slow", async (c, _) => { active = c.Checkout(); entered.SetResult(); await release.Task; Suite.Equal("x", active.Payload.ToUtf8()); }));
    runtime.Start(); runtime.TrySubmit(Suite.Message("a", ["slow"], "x"u8.ToArray())); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Suite.Check(runtime.TrySubmit(Suite.Message("a", ["slow"], "y"u8.ToArray())));
    Suite.Check(!runtime.TrySubmit(Suite.Message("a", ["slow"], "z"u8.ToArray())));
    Suite.Check(!await runtime.StopAsync(TimeSpan.FromMilliseconds(20)));
    Suite.Equal("x", active!.Payload.ToUtf8()); Suite.Equal(1L, runtime.LifetimeStats.Created - runtime.LifetimeStats.Reclaimed);
    release.SetResult(); await runtime.DrainAsync();
    Suite.Equal(0L, runtime.LifetimeStats.References);
});
await suite.Test("lifetime admission rejects memory overflow", async () =>
{
    var errors = new ErrorSink(); var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
    await using var runtime = new DsnRuntime(errors, maxMessages: 1);
    runtime.Register(new DelegateWorkspace("slow", async (_, _) => { entered.SetResult(); await release.Task; }));
    runtime.Start(); runtime.TrySubmit(Suite.Message("a", ["slow"], [])); await entered.Task;
    Suite.Check(!runtime.TrySubmit(Suite.Message("a", ["slow"], [])));
    Suite.Check(errors.Snapshot().Any(e => e.Body.Code == "MEMORY_FULL")); release.SetResult(); await runtime.DrainAsync();
});
await suite.Test("journal durable restart, scalar copies, export, quota and partial-tail recovery", async () =>
{
    var path = suite.Path("journal/records.ndjson");
    using (var store = new JournalStore(path))
    {
        var values = new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement("before") };
        await store.AppendAsync(new("echo", values)); values["value"] = JsonSerializer.SerializeToElement("after");
        Suite.Equal("before", store.Query(new())[0].Fields["value"].GetString());
        Suite.Throws<ArgumentException>(() => store.AppendAsync(new("echo", Fields.From(("nested", new[] { 1 })))));
        Suite.Throws<ArgumentException>(() => store.Query(new(Limit: 0)));
        Suite.Check(store.Export(new()).Contains("before"));
    }
    await File.AppendAllTextAsync(path, "{\"id\":2");
    using (var store = new JournalStore(path))
    {
        Suite.Equal(1, store.Query(new()).Count); await store.AppendAsync(new("hex", Fields.From(("value", 42))));
        Suite.Equal(2L, store.Query(new(AfterId: 1))[0].Id);
    }
    using var limited = new JournalStore(suite.Path("quota.ndjson"), maxRecords: 1);
    await limited.AppendAsync(new("echo", Fields.From(("value", 1))));
    Suite.Throws<IOException>(() => limited.AppendAsync(new("echo", Fields.From(("value", 2)))));
});
await suite.Test("memory/journal adapter parity and early checkin before slow storage", async () =>
{
    var memory = new MemoryRecordStore();
    using var journal = new JournalStore(suite.Path("parity.ndjson"));
    foreach (var adapter in new IRecordStore[] { memory, journal })
    {
        await adapter.AppendAsync(new("echo", Fields.From(("value", 42), ("nullable", null))));
        await adapter.AppendAsync(new("hex", Fields.From(("value", "2a"))));
    }
    Suite.Equal(memory.Export(new()), journal.Export(new()));
    Suite.Equal(1, memory.Query(new(["hex"])).Count);
    var errors = new ErrorSink();
    await using var runtime = new DsnRuntime(errors);
    var blocking = new BlockingStore();
    runtime.Register(new PayloadWorkspace("echo", blocking, false)); runtime.Start();
    runtime.TrySubmit(Suite.Message("a", ["echo"], "hello"u8.ToArray()));
    await blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Suite.Equal(1L, runtime.LifetimeStats.Checkouts);
    Suite.Equal(1L, runtime.LifetimeStats.Checkins); // Workspace lease already returned; root retained.
    Suite.Equal(1L, runtime.LifetimeStats.References);
    blocking.Release.SetResult(); await runtime.DrainAsync();
    Suite.Equal(0L, runtime.LifetimeStats.References);
});
await suite.Test("error identity and Admin snapshot persistence", async () =>
{
    var errors = new ErrorSink(2);
    errors.Report(new("TEST", "tests", "a")); errors.Report(new("TEST", "tests", "a")); errors.Report(new("TEST", "tests", "b")); errors.Report(new("OTHER", "tests"));
    Suite.Equal(2, errors.Snapshot().Length); Suite.Equal(2L, errors.Snapshot()[0].Count); Suite.Equal(1L, errors.Dropped);
    using var store = new JournalStore(suite.Path("admin.ndjson")); var admin = new AdminWorkspace(errors, store);
    await admin.PublishAsync(); await admin.PublishAsync(); Suite.Equal(2, store.Query(new()).Count);
    errors.Report(new("TEST", "tests", "a")); await admin.PublishAsync(); Suite.Equal(4, store.Query(new()).Count);
});
await suite.Test("invocation completion owns context cleanup while Runtime retains root", async () =>
{
    var errors = new ErrorSink();
    var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
    IPayloadLease? retained = null; var secondCalled = false;
    await using var runtime = new DsnRuntime(errors);
    runtime.Register(new DelegateWorkspace("first", async (context, _) =>
    {
        retained = context.Checkout(); entered.SetResult(); await release.Task;
        Suite.Equal("shared", retained.Payload.ToUtf8());
        throw new InvalidOperationException(); // Invocation must clean the leaked lease before the next call.
    }));
    runtime.Register(new DelegateWorkspace("second", (context, _) =>
    {
        Suite.Throws<ObjectDisposedException>(() => retained!.Payload.Copy());
        var lease = context.Checkout(); Suite.Equal("shared", lease.Payload.ToUtf8());
        context.Checkin(lease); secondCalled = true; return ValueTask.CompletedTask;
    }));
    runtime.Start(); runtime.TrySubmit(Suite.Message("a", ["first", "second"], "shared"u8.ToArray()));
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Suite.Equal(2L, runtime.LifetimeStats.References); Suite.Check(!secondCalled);
    release.SetResult(); await runtime.DrainAsync();
    Suite.Check(secondCalled); Suite.Equal(0L, runtime.LifetimeStats.References);
});
await suite.Test("shutdown cancels unstarted targets and retains active context until return", async () =>
{
    var errors = new ErrorSink(); var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
    var laterCalls = 0; IPayloadLease? retained = null;
    await using var runtime = new DsnRuntime(errors);
    runtime.Register(new DelegateWorkspace("first", async (context, token) =>
    {
        retained = context.Checkout(); entered.SetResult(); await release.Task;
        Suite.Check(token.IsCancellationRequested); Suite.Equal("safe", retained.Payload.ToUtf8());
    }));
    runtime.Register(new DelegateWorkspace("later", (_, _) => { laterCalls++; return ValueTask.CompletedTask; }));
    runtime.Start(); runtime.TrySubmit(Suite.Message("a", ["first", "later"], "safe"u8.ToArray()));
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Suite.Check(!await runtime.StopAsync(TimeSpan.FromMilliseconds(20)));
    Suite.Equal("safe", retained!.Payload.ToUtf8());
    Suite.Check(!runtime.TrySubmit(Suite.Message("a", ["later"], [])));
    release.SetResult(); await runtime.DrainAsync();
    Suite.Equal(0, laterCalls); Suite.Equal(0L, runtime.LifetimeStats.References);
    Suite.Throws<ObjectDisposedException>(() => retained.Payload.Copy());
});
await suite.Test("separate contexts may read concurrently without sharing cleanup ownership", async () =>
{
    var lifetime = new Lifetime(); var root = lifetime.Create(Suite.Message("a", ["one", "two"], "bytes"u8.ToArray()))!;
    using var first = new MessageContext(root); using var second = new MessageContext(root);
    var left = first.Checkout(); var right = second.Checkout();
    await Task.WhenAll(Task.Run(() => { for (var i = 0; i < 100; i++) Suite.Equal("bytes", left.Payload.ToUtf8()); first.Checkin(left); }),
        Task.Run(() => { for (var i = 0; i < 100; i++) Suite.Equal("bytes", right.Payload.ToUtf8()); }));
    first.Dispose(); Suite.Equal("bytes", right.Payload.ToUtf8());
    second.Dispose(); root.Release(); Suite.Equal(0L, lifetime.Stats.References);
});
suite.Finish();
