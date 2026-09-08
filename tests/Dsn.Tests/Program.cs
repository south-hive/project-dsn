using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Dsn.Contracts;
using Dsn.Core;
using Dsn.Host;
using Dsn.Workspaces;

var suite = new Suite();
await suite.Test("version, envelope, opaque bytes and strict notification validation", async () =>
{
    var protocol = new Protocol();
    var message = protocol.Decode(Suite.Frame("a", ["echo"], [0, 255, 10]));
    Suite.Equal("00ff0a", Convert.ToHexString(message.Payload).ToLowerInvariant());
    Suite.Equal(0, protocol.Decode(Suite.Frame("a", ["echo"], [])).Payload.Length);
    foreach (var frame in new[] { "[]", "{}", "{", "{\"jsonrpc\":\"2.0\",\"method\":\"dsn.publish\",\"id\":1,\"params\":{}}" })
        Suite.Throws<ProtocolException>(() => protocol.Decode(Encoding.UTF8.GetBytes(frame)));
    var valid = Encoding.UTF8.GetString(Suite.Frame("a", ["echo"], [1]));
    foreach (var broken in new[] { valid.Replace("\"version\":1", "\"version\":2"), valid.Replace("\"version\":1", "\"version\":\"1\""), valid.Replace("AQ==", "AR=="), valid.Replace("AQ==", "A Q=="), valid.Replace("2026-09-08", "2026-02-30") })
        Suite.Throws<ProtocolException>(() => protocol.Decode(Encoding.UTF8.GetBytes(broken)));
    Suite.Throws<ProtocolException>(() => new Protocol(0).Decode(Suite.Frame("a", ["echo"], [1])));
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
    for (var i = 0; i < 20; i++) Suite.Check(runtime.Submit(Suite.Message("a", ["unknown", "broken", "good", "good"], Encoding.UTF8.GetBytes(i.ToString()))));
    await runtime.DrainAsync(new CancellationTokenSource(5000).Token);
    Suite.Equal(string.Join(',', Enumerable.Range(0, 20)), string.Join(',', seen));
    Suite.Throws<ObjectDisposedException>(() => leaked!.Payload.Copy());
    Suite.Equal(0L, runtime.Lifetime.Stats.References);
    Suite.Equal(20L, errors.Snapshot().Single(x => x.Body.Code == "WORKSPACE_FAILED").Count);
});
await suite.Test("queue saturation and shutdown retain active original until completion", async () =>
{
    var errors = new ErrorSink(); var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
    IPayloadLease? active = null;
    await using var runtime = new DsnRuntime(errors, capacity: 1);
    runtime.Register(new DelegateWorkspace("slow", async (c, _) => { active = c.Checkout(); entered.SetResult(); await release.Task; Suite.Equal("x", active.Payload.ToUtf8()); }));
    runtime.Start(); runtime.Submit(Suite.Message("a", ["slow"], "x"u8.ToArray())); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Suite.Check(runtime.Submit(Suite.Message("a", ["slow"], "y"u8.ToArray())));
    Suite.Check(!runtime.Submit(Suite.Message("a", ["slow"], "z"u8.ToArray())));
    Suite.Check(!await runtime.StopAsync(TimeSpan.FromMilliseconds(20)));
    Suite.Equal("x", active!.Payload.ToUtf8()); Suite.Equal(1L, runtime.Lifetime.Stats.Created - runtime.Lifetime.Stats.Reclaimed);
    release.SetResult(); await runtime.DrainAsync();
    Suite.Equal(0L, runtime.Lifetime.Stats.References);
});
await suite.Test("lifetime admission rejects memory overflow", async () =>
{
    var errors = new ErrorSink(); var entered = new TaskCompletionSource(); var release = new TaskCompletionSource();
    await using var runtime = new DsnRuntime(errors, maxMessages: 1);
    runtime.Register(new DelegateWorkspace("slow", async (_, _) => { entered.SetResult(); await release.Task; }));
    runtime.Start(); runtime.Submit(Suite.Message("a", ["slow"], [])); await entered.Task;
    Suite.Check(!runtime.Submit(Suite.Message("a", ["slow"], [])));
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
    runtime.Submit(Suite.Message("a", ["echo"], "hello"u8.ToArray()));
    await blocking.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Suite.Equal(1L, runtime.Lifetime.Stats.Checkouts);
    Suite.Equal(1L, runtime.Lifetime.Stats.Checkins); // Workspace lease already returned; root retained.
    Suite.Equal(1L, runtime.Lifetime.Stats.References);
    blocking.Release.SetResult(); await runtime.DrainAsync();
    Suite.Equal(0L, runtime.Lifetime.Stats.References);
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
await suite.Test("real TCP split/coalesced frames, bad-version continuation and source session rules", async () =>
{
    var errors = new ErrorSink(); var received = new System.Collections.Concurrent.ConcurrentQueue<DecodedMessage>();
    await using var rpc = new RpcServer(new Protocol(), received.Enqueue, errors);
    rpc.Start("127.0.0.1", 0);
    using var first = new TcpClient(); await first.ConnectAsync(IPAddress.Loopback, rpc.Port);
    var frame = Suite.Frame("a", ["echo"], [0, 255]); var stream = first.GetStream();
    await stream.WriteAsync(frame.AsMemory(0, 7)); await stream.WriteAsync(frame.AsMemory(7));
    var bad = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(frame).Replace("\"version\":1", "\"version\":999"));
    await stream.WriteAsync(bad.Concat(frame).ToArray()); await Suite.Eventually(() => received.Count == 2);
    using var duplicate = new TcpClient(); await duplicate.ConnectAsync(IPAddress.Loopback, rpc.Port); await duplicate.GetStream().WriteAsync(frame);
    await Suite.Eventually(() => errors.Snapshot().Any(e => e.Body.Code == "SOURCE_IN_USE"));
    using var second = new TcpClient(); await second.ConnectAsync(IPAddress.Loopback, rpc.Port); await second.GetStream().WriteAsync(Suite.Frame("b", ["echo"], []));
    await Suite.Eventually(() => received.Count == 3);
    rpc.DisconnectSource("a"); await Suite.Eventually(() => rpc.SessionCount == 1);
    await second.GetStream().WriteAsync(Suite.Frame("b", ["echo"], [])); await Suite.Eventually(() => received.Count == 4);
    using var reconnect = new TcpClient(); await reconnect.ConnectAsync(IPAddress.Loopback, rpc.Port); await reconnect.GetStream().WriteAsync(frame); await Suite.Eventually(() => received.Count == 5);
    await reconnect.GetStream().WriteAsync(Suite.Frame("changed", ["echo"], [])); await Suite.Eventually(() => errors.Snapshot().Any(e => e.Body.Code == "SOURCE_CHANGED"));
});
await suite.Test("invalid RPC, oversized/truncated frames and session capacity", async () =>
{
    var errors = new ErrorSink();
    await using var rpc = new RpcServer(new Protocol(), _ => { }, errors, maxSessions: 1, maxFrameBytes: 256);
    rpc.Start("127.0.0.1", 0);
    using (var client = new TcpClient()) { await client.ConnectAsync(IPAddress.Loopback, rpc.Port); await client.GetStream().WriteAsync("[]\n"u8.ToArray()); await Suite.Eventually(() => errors.Snapshot().Any(e => e.Body.Code == "INVALID_RPC")); }
    await Suite.Eventually(() => rpc.SessionCount == 0);
    using (var client = new TcpClient()) { await client.ConnectAsync(IPAddress.Loopback, rpc.Port); await client.GetStream().WriteAsync(new byte[257]); await Suite.Eventually(() => errors.Snapshot().Any(e => e.Body.Code == "FRAME_TOO_LARGE")); }
    await Suite.Eventually(() => rpc.SessionCount == 0);
    using (var client = new TcpClient()) { await client.ConnectAsync(IPAddress.Loopback, rpc.Port); await client.GetStream().WriteAsync("unfinished"u8.ToArray()); client.Client.Shutdown(SocketShutdown.Send); await Suite.Eventually(() => errors.Snapshot().Any(e => e.Body.Code == "TRUNCATED_FRAME")); }
    await Suite.Eventually(() => rpc.SessionCount == 0);
    using var one = new TcpClient(); await one.ConnectAsync(IPAddress.Loopback, rpc.Port); await Suite.Eventually(() => rpc.SessionCount == 1);
    using var two = new TcpClient(); await two.ConnectAsync(IPAddress.Loopback, rpc.Port); await Suite.Eventually(() => errors.Snapshot().Any(e => e.Body.Code == "SESSION_LIMIT"));
});
await suite.Test("DLL plugin discovery and required load failure", async () =>
{
    using var store = new JournalStore(suite.Path("plugins.ndjson")); var errors = new ErrorSink();
    await using var runtime = new DsnRuntime(errors); var loader = new PluginLoader();
    loader.Load(typeof(EchoPlugin).Assembly.Location, runtime, new(store, errors)); runtime.Start();
    runtime.Submit(Suite.Message("a", ["echo", "hex"], "hello"u8.ToArray())); await runtime.DrainAsync();
    Suite.Equal(2, store.Query(new()).Count);
    Suite.Throws<FileNotFoundException>(() => loader.Load(suite.Path("absent.dll"), runtime, new(store, errors)));
});
await suite.Test("HTTP View authorization, per-user saved definitions, field combination, export and restart", async () =>
{
    var settings = new Settings { RpcPort = 0, ViewPort = 0, DataDirectory = suite.Path("host"), Users = new() {
        ["alice"] = new("alice-test-token-1234", ["echo", "hex"]), ["bob"] = new("bob-test-token-5678", ["echo"]) } };
    await using (var host = new DsnApplication(settings))
    {
        await host.StartAsync();
        using var source = new TcpClient(); await source.ConnectAsync(IPAddress.Loopback, host.Rpc.Port);
        await source.GetStream().WriteAsync(Suite.Frame("test", ["echo", "hex"], "hello"u8.ToArray()));
        await Suite.Eventually(() => host.Runtime.Lifetime.Stats.Reclaimed == 1);
        using var client = new HttpClient { BaseAddress = new(host.ViewAddress) };
        Suite.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/view")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "alice-test-token-1234");
        Suite.Check((await client.GetStringAsync("/fields")).Contains("payload_hex"));
        Suite.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/view?fields=typo")).StatusCode);
        Suite.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/view?limit=0")).StatusCode);
        Suite.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/views/mine", new ViewDefinition(["echo", "hex"], ["id", "workspace", "message_id", "payload_utf8", "payload_hex"]))).StatusCode);
        var rows = (await client.GetFromJsonAsync<JsonElement>("/views/mine")).EnumerateArray().ToArray();
        Suite.Equal(2, rows.Length); Suite.Equal(rows[0].GetProperty("message_id").GetString(), rows[1].GetProperty("message_id").GetString());
        Suite.Equal("hello", rows[0].GetProperty("payload_utf8").GetString()); Suite.Equal(JsonValueKind.Null, rows[0].GetProperty("payload_hex").ValueKind);
        Suite.Check((await client.GetStringAsync("/export")).Contains("68656c6c6f"));
        client.DefaultRequestHeaders.Authorization = new("Bearer", "bob-test-token-5678");
        Suite.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/views/mine")).StatusCode);
        Suite.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/export?workspaces=hex")).StatusCode);
        Suite.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/views/mine", new ViewDefinition(["hex"], ["id"]))).StatusCode);
        Suite.Check(!(await client.GetStringAsync("/fields")).Contains("payload_hex"));
    }
    await using (var host = new DsnApplication(settings))
    {
        await host.StartAsync(); using var client = new HttpClient { BaseAddress = new(host.ViewAddress) };
        client.DefaultRequestHeaders.Authorization = new("Bearer", "alice-test-token-1234");
        Suite.Equal(2, (await client.GetFromJsonAsync<JsonElement>("/views/mine")).GetArrayLength());
    }
});
await suite.Test("startup rollback releases RPC/View/storage and invalid settings fail early", async () =>
{
    Suite.Throws<ArgumentException>(() => new Settings { Bind = "0.0.0.0" }.Validate());
    Suite.Throws<ArgumentException>(() => new Settings { QueueCapacity = 0 }.Validate());
    var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
    var settings = new Settings { DataDirectory = suite.Path("rollback"), RpcPort = ((IPEndPoint)listener.LocalEndpoint).Port, ViewPort = 0 };
    await using (var host = new DsnApplication(settings))
        await Suite.ThrowsAsync<SocketException>(() => host.StartAsync());
    listener.Stop();
    using var reopened = new JournalStore(System.IO.Path.Combine(settings.DataDirectory, "records.ndjson"));
    Suite.Equal(0, reopened.Query(new()).Count);
});
suite.Finish();

sealed class DelegateWorkspace(string name, Func<IMessageContext, CancellationToken, ValueTask> action) : IWorkspace
{
    public string Name => name;
    public ValueTask ProcessAsync(IMessageContext message, CancellationToken token) => action(message, token);
}
sealed class BlockingStore : IRecordStore
{
    public readonly TaskCompletionSource Entered = new(), Release = new();
    public async ValueTask AppendAsync(RecordInput input, CancellationToken cancellationToken = default)
    { Entered.SetResult(); await Release.Task; Suite.Equal("hello", input.Fields["payload_utf8"].GetString()); }
}
sealed class Suite
{
    private int passed, failed;
    private readonly string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsn-tests-" + Guid.NewGuid().ToString("N"));
    public string Path(string name) => System.IO.Path.Combine(root, name);
    public async Task Test(string name, Func<Task> action)
    {
        try { await action().WaitAsync(TimeSpan.FromSeconds(30)); passed++; Console.WriteLine($"PASS {name}"); }
        catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e}"); }
    }
    public void Finish() { Console.WriteLine($"RESULT passed={passed} failed={failed}"); Environment.ExitCode = failed == 0 ? 0 : 1; if (Directory.Exists(root)) Directory.Delete(root, true); }
    public static void Check(bool value) { if (!value) throw new Exception("Assertion failed"); }
    public static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
    public static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
    public static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }
    public static async Task Eventually(Func<bool> condition) { using var timeout = new CancellationTokenSource(5000); while (!condition()) await Task.Delay(10, timeout.Token); }
    public static byte[] Frame(string source, string[] workspace, byte[] payload) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { jsonrpc = "2.0", method = "dsn.publish", @params = new {
        version = 1, source_id = source, time = "2026-09-08T12:00:00Z", event_type = "normal", workspace, payload = Convert.ToBase64String(payload) } }) + "\n");
    public static DecodedMessage Message(string source, string[] workspace, byte[] payload) => new Protocol().Decode(Frame(source, workspace, payload));
}
