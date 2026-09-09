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
await suite.Test("feature assembly dependencies and public ownership boundary", async () =>
{
    var libraries = new[] { typeof(DsnRuntime).Assembly, typeof(NotificationProtocol).Assembly, typeof(JournalStore).Assembly,
        typeof(ErrorSink).Assembly, typeof(ViewService).Assembly, typeof(EchoPlugin).Assembly };
    foreach (var assembly in libraries)
    {
        var dependencies = assembly.GetReferencedAssemblies().Where(a => a.Name!.StartsWith("Dsn.")).Select(a => a.Name).ToArray();
        Suite.Equal("Dsn.Contracts", string.Join(',', dependencies));
    }
    var exported = typeof(DsnRuntime).Assembly.GetExportedTypes().Select(t => t.Name).Order().ToArray();
    Suite.Equal("DsnRuntime,LifetimeStats", string.Join(',', exported));
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
suite.Finish();
