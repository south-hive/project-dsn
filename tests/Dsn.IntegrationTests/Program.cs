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
using Dsn.Host;

var suite = new Suite();
await suite.Test("real TCP split/coalesced frames, bad-version continuation and source session rules", async () =>
{
    var errors = new ErrorSink(); var received = new System.Collections.Concurrent.ConcurrentQueue<InboundMessage>();
    await using var rpc = new TcpNotificationReceiver(new NotificationProtocol(), new CollectingSink(received), errors);
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
    await using var rpc = new TcpNotificationReceiver(new NotificationProtocol(), new CollectingSink(), errors, maxSessions: 1, maxFrameBytes: 256);
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
    runtime.TrySubmit(Suite.Message("a", ["echo", "hex"], "hello"u8.ToArray())); await runtime.DrainAsync();
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
        using var source = new TcpClient(); await source.ConnectAsync(IPAddress.Loopback, host.Ingress.Port);
        await source.GetStream().WriteAsync(Suite.Frame("test", ["echo", "hex"], "hello"u8.ToArray()));
        await Suite.Eventually(() => host.Runtime.LifetimeStats.Reclaimed == 1);
        // Connect directly to the local test host regardless of proxy environment variables.
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(host.ViewAddress) };
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
        await host.StartAsync();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(host.ViewAddress) };
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
    using var reopened = new SqliteStore(System.IO.Path.Combine(settings.DataDirectory, "dsn.db"));
    Suite.Equal(0, reopened.Query(new()).Count);
});
await suite.Test("feature assembly dependencies and public ownership boundary", async () =>
{
    var libraries = new[] { typeof(DsnRuntime).Assembly, typeof(NotificationProtocol).Assembly, typeof(JournalStore).Assembly,
        typeof(ErrorSink).Assembly, typeof(ViewService).Assembly, typeof(EchoPlugin).Assembly,
        typeof(Dsn.Workspaces.Temperature.TemperaturePlugin).Assembly };
    foreach (var assembly in libraries)
    {
        var dependencies = assembly.GetReferencedAssemblies().Where(a => a.Name!.StartsWith("Dsn.")).Select(a => a.Name).ToArray();
        Suite.Equal("Dsn.Contracts", string.Join(',', dependencies));
    }
    var exported = typeof(DsnRuntime).Assembly.GetExportedTypes().Select(t => t.Name).Order().ToArray();
    Suite.Equal("DsnRuntime,LifetimeStats,PipelineRouter", string.Join(',', exported));
    Suite.Check(typeof(IMessageSink).IsAssignableFrom(typeof(DsnRuntime)));
    Suite.Check(typeof(IWorkspaceRegistration).IsAssignableFrom(typeof(DsnRuntime)));
    await Task.CompletedTask;
});
await suite.Test("build rejects a feature reference to another feature", async () =>
{
    var root = new DirectoryInfo(AppContext.BaseDirectory);
    while (!File.Exists(System.IO.Path.Combine(root.FullName, "DSN.sln"))) root = root.Parent ?? throw new Exception("Repository not found");
    var project = suite.Path("forbidden/Dsn.View.csproj"); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(project)!);
    var escape = System.Security.SecurityElement.Escape;
    await File.WriteAllTextAsync(project, $"<Project Sdk=\"Microsoft.NET.Sdk\"><Import Project=\"{escape(root.FullName)}/Directory.Build.props\"/><ItemGroup><ProjectReference Include=\"{escape(root.FullName)}/src/Dsn.Runtime/Dsn.Runtime.csproj\"/></ItemGroup><Import Project=\"{escape(root.FullName)}/Directory.Build.targets\"/></Project>");
    var start = new System.Diagnostics.ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var arg in new[] { "msbuild", project, "-t:ValidateArchitecture", "-nologo" }) start.ArgumentList.Add(arg);
    using var process = System.Diagnostics.Process.Start(start)!;
    var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    Suite.Check(process.ExitCode != 0); Suite.Check(((await output) + (await error)).Contains("DSN architecture violation"));
});

await suite.Test("local raw archive, emitted provenance, authorized replay and restart", async () =>
{
    var settings = new Settings { RpcPort = 0, ViewPort = 0, DataDirectory = suite.Path("raw-host"), Users = new() {
        ["reader"] = new("reader-token-123456", ["bench"]),
        ["operator"] = new("operator-token-123456", ["bench"], true),
        ["other"] = new("other-token-123456", ["echo"], true) } };
    string originalId;
    await using (var host = new DsnApplication(settings))
    {
        await host.StartAsync();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(host.ViewAddress) };
        Suite.Check((await client.GetStringAsync("/")).Contains("DSN / LOCAL DATA EXPLORER"));
        Suite.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/raw")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "operator-token-123456");
        var payload = JsonSerializer.SerializeToUtf8Bytes(new { schema = "bench.v1", pc_id = "pc-a", dut_id = "dut-2", run_id = "run-4",
            instance_id = "process-7", role = "telemetry-app", sequence = 9, values = new { iops = 12000, status = "ok" } });
        using var source = new TcpClient(); await source.ConnectAsync(IPAddress.Loopback, host.Ingress.Port);
        await source.GetStream().WriteAsync(Suite.Frame("pc-a.telemetry.process-7", ["bench"], payload));
        await Suite.Eventually(() => host.Runtime.LifetimeStats.Created == 1); await host.Runtime.DrainAsync();
        var raw = await client.GetFromJsonAsync<JsonElement>("/raw");
        var first = raw.GetProperty("items")[0]; originalId = first.GetProperty("messageId").GetString()!;
        Suite.Equal(Convert.ToBase64String(payload), first.GetProperty("payload").GetString());
        var rows = await client.GetFromJsonAsync<JsonElement>("/view?workspaces=bench&fields=message_id,processing_id,replay_id,processor_version,value_iops");
        Suite.Equal(12000, rows[0].GetProperty("value_iops").GetInt32());
        Suite.Equal(originalId, rows[0].GetProperty("message_id").GetString());
        var processingId = rows[0].GetProperty("processing_id").GetString();
        Suite.Equal(JsonValueKind.Null, rows[0].GetProperty("replay_id").ValueKind);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "reader-token-123456");
        Suite.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/raw/1/replay", new { workspaces = new[] { "bench" } })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "other-token-123456");
        Suite.Equal(0, (await client.GetFromJsonAsync<JsonElement>("/raw")).GetProperty("items").GetArrayLength());
        Suite.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/raw/1/replay", new { workspaces = new[] { "echo" } })).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", "operator-token-123456");
        Suite.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/raw?limit=101")).StatusCode);
        using var response = await client.PostAsJsonAsync("/raw/1/replay", new { workspaces = new[] { "bench" } });
        Suite.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var replayId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayId").GetString();
        await host.Runtime.DrainAsync();
        rows = await client.GetFromJsonAsync<JsonElement>("/view?workspaces=bench&fields=message_id,processing_id,replay_id,value_iops");
        Suite.Equal(2, rows.GetArrayLength()); Suite.Equal(originalId, rows[1].GetProperty("message_id").GetString());
        Suite.Equal(replayId, rows[1].GetProperty("replay_id").GetString());
        Suite.Check(processingId != rows[1].GetProperty("processing_id").GetString());
        Suite.Equal(1, (await client.GetFromJsonAsync<JsonElement>("/raw")).GetProperty("items").GetArrayLength());
    }
    await using (var host = new DsnApplication(settings))
    {
        await host.StartAsync();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(host.ViewAddress) };
        client.DefaultRequestHeaders.Authorization = new("Bearer", "operator-token-123456");
        Suite.Equal(originalId, (await client.GetFromJsonAsync<JsonElement>("/raw")).GetProperty("items")[0].GetProperty("messageId").GetString());
        Suite.Equal(2, (await client.GetFromJsonAsync<JsonElement>("/view?workspaces=bench&fields=id")).GetArrayLength());
    }
});
await suite.Test("unknown or failed workspace retains raw; quota failure isolates collector and releases memory", async () =>
{
    var errors = new ErrorSink();
    using var results = new JournalStore(suite.Path("raw-results.ndjson"));
    using var archive = new RawArchive(suite.Path("raw-quota.ndjson"), 1024 * 1024, 2);
    await using var runtime = new DsnRuntime(errors, archive: archive, results: results);
    runtime.Register(new BenchWorkspace()); runtime.Start();
    runtime.TrySubmit(Suite.Message("telemetry", ["future-decoder"], [0, 255]));
    runtime.TrySubmit(Suite.Message("test", ["bench"], "invalid json"u8.ToArray()));
    await runtime.DrainAsync();
    Suite.Equal(2, archive.Read().Count); Suite.Equal(0, results.Query(new()).Count);
    runtime.TrySubmit(Suite.Message("orchestrator", ["bench"], [])); await runtime.DrainAsync();
    Suite.Check(errors.Snapshot().Any(e => e.Body.Code == "RAW_STORAGE_FAILED"));
    Suite.Check(errors.Snapshot().Any(e => e.Body.Code == "WORKSPACE_FAILED"));
    Suite.Equal(0L, runtime.LifetimeStats.References);
    Suite.Equal(2, archive.Read().Count);
});
await suite.Test("raw disabled remains usable and raw ACL requires every original workspace", async () =>
{
    var settings = new Settings { RpcPort = 0, ViewPort = 0, DataDirectory = suite.Path("no-raw"), RetainRaw = false };
    await using (var host = new DsnApplication(settings))
    {
        await host.StartAsync();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(host.ViewAddress) };
        Suite.Equal(HttpStatusCode.Conflict, (await client.GetAsync("/raw")).StatusCode);
        host.Runtime.TrySubmit(Suite.Message("test", ["echo"], [1])); await host.Runtime.DrainAsync();
        Suite.Equal(1, (await client.GetFromJsonAsync<JsonElement>("/view?workspaces=echo&fields=id")).GetArrayLength());
    }
    await using (var host = new DsnApplication(new Settings { RpcPort = 0, ViewPort = 0, DataDirectory = suite.Path("unknown-raw") }))
    {
        await host.StartAsync(); host.Runtime.TrySubmit(Suite.Message("telemetry", ["future-decoder"], [0, 255])); await host.Runtime.DrainAsync();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(host.ViewAddress) };
        Suite.Equal(1, (await client.GetFromJsonAsync<JsonElement>("/raw")).GetProperty("items").GetArrayLength());
        Suite.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/raw/1/replay", new { workspaces = new[] { "hex" } })).StatusCode);
        await host.Runtime.DrainAsync();
        Suite.Equal("00ff", (await client.GetFromJsonAsync<JsonElement>("/view?workspaces=hex&fields=payload_hex"))[0].GetProperty("payload_hex").GetString());
    }
    var secured = new Settings { RpcPort = 0, ViewPort = 0, DataDirectory = suite.Path("raw-scopes"), Users = new() {
        ["reader"] = new("reader-token-123456", ["echo"]) } };
    await using (var host = new DsnApplication(secured))
    {
        await host.StartAsync(); host.Runtime.TrySubmit(Suite.Message("test", ["echo", "hex"], [2])); await host.Runtime.DrainAsync();
        using var client = new HttpClient(new HttpClientHandler { UseProxy = false }) { BaseAddress = new(host.ViewAddress) };
        client.DefaultRequestHeaders.Authorization = new("Bearer", "reader-token-123456");
        var page = await client.GetFromJsonAsync<JsonElement>("/raw");
        Suite.Equal(0, page.GetProperty("items").GetArrayLength()); Suite.Equal(1L, page.GetProperty("nextAfterId").GetInt64());
    }
});

suite.Finish();
