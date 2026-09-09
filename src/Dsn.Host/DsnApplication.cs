using Dsn.Contracts;
using Dsn.Runtime;
using Dsn.Ingress;
using Dsn.Persistence;
using Dsn.Diagnostics;
using Dsn.View;
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
    public TcpNotificationReceiver Ingress { get; }
    public string ViewAddress => web.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    public DsnApplication(Settings settings)
    {
        settings.Validate(); this.settings = settings;
        Errors = new(settings.ErrorCapacity);
        store = new(Path.Combine(settings.DataDirectory, "records.ndjson"), settings.JournalBytes, settings.RecordCapacity);
        Runtime = new(Errors, settings.QueueCapacity, settings.QueueBytes, settings.MaxMessages, settings.LiveBytes);
        Ingress = new(new NotificationProtocol(settings.PayloadBytes), Runtime, Errors, settings.MaxSessions, settings.FrameBytes);
        admin = new(Errors, store);
        try
        {
            var services = new WorkspaceServices(store, Errors);
            var loader = new PluginLoader();
            var plugins = settings.Plugins.Length == 0
                ? new[] { Path.Combine(AppContext.BaseDirectory, "plugins", "Dsn.Workspaces.Examples.dll") }
                : settings.Plugins;
            foreach (var path in plugins) loader.Load(path, Runtime, services);
            var definitions = new ViewDefinitions(Path.Combine(settings.DataDirectory, "views.json"));
            var view = new ViewService(store);
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ApplicationName = typeof(DsnApplication).Assembly.FullName });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(o => { o.Limits.MaxRequestBodySize = 16384; o.Listen(System.Net.IPAddress.Parse(settings.Bind), settings.ViewPort); });
            web = builder.Build();
            web.MapViewEndpoints(settings, store, view, definitions);
        }
        catch { store.Dispose(); throw; }
    }
    public async Task StartAsync()
    {
        try
        {
            Runtime.Start(); await web.StartAsync(); Ingress.Start(settings.Bind, settings.RpcPort);
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
        await Ingress.DisposeAsync();
        await web.StopAsync(TimeSpan.FromSeconds(settings.ShutdownSeconds));
        adminStop.Cancel(); if (adminLoop is not null) await adminLoop;
        if (!await Runtime.StopAsync(TimeSpan.FromSeconds(settings.ShutdownSeconds)))
            Console.Error.WriteLine("DSN shutdown incomplete: waiting for active Workspace; owned payload and storage remain alive.");
        await Runtime.DisposeAsync();
        try { await admin.PublishAsync(); } catch (IOException) { Console.Error.WriteLine("DSN final Admin snapshot could not be stored."); }
        store.Dispose(); await web.DisposeAsync(); adminStop.Dispose();
    }
}
