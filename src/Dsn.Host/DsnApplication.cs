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
    private readonly SqliteStore store;
    private readonly IRawArchive? archive;
    public PipelineRouter Pipeline { get; }
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
        store = new(Path.Combine(settings.DataDirectory, "dsn.db"), settings.JournalBytes, settings.RecordCapacity, settings.RawBytes, settings.RawCapacity);
        try { store.ImportLegacy(settings.DataDirectory); } catch { store.Dispose(); throw; }
        archive = settings.RetainRaw ? store : null;
        Pipeline = new(store, Errors, new ConsoleSink());
        Runtime = new(Errors, settings.QueueCapacity, settings.QueueBytes, settings.MaxMessages, settings.LiveBytes, archive, Pipeline);
        Ingress = new(new NotificationProtocol(settings.PayloadBytes), Runtime, Errors, settings.MaxSessions, settings.FrameBytes);
        admin = new(Errors, store);
        try
        {
            var services = new WorkspaceServices(Pipeline, Errors);
            var loader = new PluginLoader();
            var plugins = settings.Plugins.Length == 0
                ? new[] { Path.Combine(AppContext.BaseDirectory, "plugins", "Dsn.Workspaces.Examples.dll") }
                : settings.Plugins;
            foreach (var path in plugins) loader.Load(path, Runtime, services, filters: Pipeline);
            Pipeline.Configure(settings.Pipelines, Runtime.Workspaces);
            var definitions = new ViewDefinitions(store);
            var view = new ViewService(store);
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ApplicationName = typeof(DsnApplication).Assembly.FullName });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(o => { o.Limits.MaxRequestBodySize = 16384; o.Listen(System.Net.IPAddress.Parse(settings.Bind), settings.ViewPort); });
            web = builder.Build();
            web.MapViewEndpoints(settings, store, view, definitions, archive, Runtime, Pipeline);
        }
        catch { store.Dispose(); throw; }
    }
    public async Task StartAsync()
    {
        try
        {
            Runtime.Start(); await web.StartAsync(); Ingress.Start(settings.IngressBind, settings.RpcPort);
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

internal sealed class ConsoleSink : IRecordStore
{
    public ValueTask AppendAsync(RecordInput record, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Console.Error.WriteLine(System.Text.Json.JsonSerializer.Serialize(record));
        return ValueTask.CompletedTask;
    }
}
