using System.Security.Cryptography;
using System.Text;
using Dsn.Contracts;
using Dsn.Core;
using Dsn.Workspaces;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Dsn.Host;

public sealed class DsnApplication : IAsyncDisposable
{
    private readonly Settings settings;
    private readonly JournalStore store;
    private readonly AdminWorkspace admin;
    private readonly CancellationTokenSource adminStop = new();
    private readonly WebApplication web;
    private Task? adminLoop;
    private bool disposed;
    public ErrorSink Errors { get; }
    public DsnRuntime Runtime { get; }
    public RpcServer Rpc { get; }
    public string ViewAddress => web.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    public DsnApplication(Settings settings)
    {
        settings.Validate(); this.settings = settings;
        Errors = new(settings.ErrorCapacity);
        store = new(Path.Combine(settings.DataDirectory, "records.ndjson"), settings.JournalBytes, settings.RecordCapacity);
        Runtime = new(Errors, settings.QueueCapacity, settings.QueueBytes, settings.MaxMessages, settings.LiveBytes);
        Rpc = new(new Protocol(settings.PayloadBytes), m => Runtime.Submit(m), Errors, settings.MaxSessions, settings.FrameBytes);
        admin = new(Errors, store);
        try
        {
            var services = new WorkspaceServices(store, Errors);
            if (settings.Plugins.Length == 0)
            {
                Runtime.Register(new EchoPlugin().Create(services)); Runtime.Register(new HexPlugin().Create(services));
            }
            else
            {
                var loader = new PluginLoader();
                foreach (var path in settings.Plugins) loader.Load(path, Runtime, services);
            }
            var definitions = new ViewDefinitions(Path.Combine(settings.DataDirectory, "views.json"));
            var view = new ViewService(store);
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ApplicationName = typeof(DsnApplication).Assembly.FullName });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(o => { o.Limits.MaxRequestBodySize = 16384; o.Listen(System.Net.IPAddress.Parse(settings.Bind), settings.ViewPort); });
            web = builder.Build();
            web.Use(async (context, next) =>
            {
                string? user = null;
                if (settings.Users.Count == 0) user = "local";
                else
                {
                    var authorization = context.Request.Headers.Authorization.ToString();
                    if (authorization.StartsWith("Bearer ", StringComparison.Ordinal))
                    {
                        var supplied = SHA256.HashData(Encoding.UTF8.GetBytes(authorization[7..]));
                        foreach (var p in settings.Users)
                            if (CryptographicOperations.FixedTimeEquals(supplied, SHA256.HashData(Encoding.UTF8.GetBytes(p.Value.Token)))) user = p.Key;
                    }
                }
                if (user is null) { context.Response.StatusCode = 401; return; }
                context.Items["user"] = user;
                try { await next(context); }
                catch (Exception e) when (e is ArgumentException or System.Text.Json.JsonException or BadHttpRequestException or FormatException or OverflowException)
                { if (!context.Response.HasStarted) { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = "Invalid View request" }); } }
                catch (IOException)
                { if (!context.Response.HasStarted) { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { error = "Storage unavailable" }); } }
            });
            string User(HttpContext c) => (string)c.Items["user"]!;
            string[] Allowed(HttpContext c) => settings.Users.Count == 0 ? store.Fields().Keys.Concat(["echo", "hex", "admin"]).Distinct().ToArray() : settings.Users[User(c)].Workspaces;
            bool Authorized(HttpContext c, string[] w) => w.All(Allowed(c).Contains);
            string[] Targets(HttpContext c) => c.Request.Query.TryGetValue("workspaces", out var w) ? w.ToString().Split(',') : Allowed(c);
            long After(HttpContext c) => c.Request.Query.TryGetValue("afterId", out var a) ? long.Parse(a.ToString()) : 0;
            int Limit(HttpContext c) => c.Request.Query.TryGetValue("limit", out var l) ? int.Parse(l.ToString()) : 100;
            web.MapGet("/health", () => Results.Ok(new { status = "ready" }));
            web.MapGet("/fields", (HttpContext c) => Results.Ok(store.Fields().Where(p => Allowed(c).Contains(p.Key)).ToDictionary()));
            web.MapGet("/view", (HttpContext c) =>
            {
                var w = Targets(c);
                if (!Authorized(c, w)) return Results.StatusCode(403);
                var f = c.Request.Query.TryGetValue("fields", out var fields) ? fields.ToString().Split(',') : new[] { "id", "workspace" };
                return Results.Ok(view.Read(new(w, f), After(c), Limit(c)));
            });
            web.MapGet("/export", (HttpContext c) =>
            {
                var w = Targets(c);
                return !Authorized(c, w) ? Results.StatusCode(403) : Results.Text(store.Export(new(w, After(c), Limit(c))), "application/x-ndjson");
            });
            web.MapGet("/views", (HttpContext c) => Results.Ok(definitions.List(User(c))));
            web.MapPut("/views/{name}", (HttpContext c, string name, ViewDefinition definition) =>
            {
                if (definition.Workspaces is null) throw new ArgumentException("Missing workspaces");
                if (!Authorized(c, definition.Workspaces)) return Results.StatusCode(403);
                view.Validate(definition); definitions.Put(User(c), name, definition); return Results.NoContent();
            });
            web.MapGet("/views/{name}", (HttpContext c, string name) =>
            {
                if (!definitions.List(User(c)).TryGetValue(name, out var definition)) return Results.NotFound();
                return !Authorized(c, definition.Workspaces) ? Results.StatusCode(403) : Results.Ok(view.Read(definition, After(c), Limit(c)));
            });
        }
        catch { store.Dispose(); throw; }
    }
    public async Task StartAsync()
    {
        try
        {
            Runtime.Start(); await web.StartAsync(); Rpc.Start(settings.Bind, settings.RpcPort);
            adminLoop = Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                try
                {
                    while (await timer.WaitForNextTickAsync(adminStop.Token))
                        try { await admin.PublishAsync(adminStop.Token); }
                        catch (IOException) { Errors.Report(new("ADMIN_STORAGE_FAILED", "admin")); }
                }
                catch (OperationCanceledException) when (adminStop.IsCancellationRequested) { }
            });
        }
        catch { await DisposeAsync(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        if (disposed) return; disposed = true;
        await Rpc.DisposeAsync();
        await web.StopAsync(TimeSpan.FromSeconds(settings.ShutdownSeconds));
        adminStop.Cancel(); if (adminLoop is not null) await adminLoop;
        if (!await Runtime.StopAsync(TimeSpan.FromSeconds(settings.ShutdownSeconds)))
            Console.Error.WriteLine("DSN shutdown incomplete: waiting for active Workspace; owned payload and storage remain alive.");
        await Runtime.DisposeAsync();
        try { await admin.PublishAsync(); } catch (IOException) { Console.Error.WriteLine("DSN final Admin snapshot could not be stored."); }
        store.Dispose(); await web.DisposeAsync(); adminStop.Dispose();
    }
}
