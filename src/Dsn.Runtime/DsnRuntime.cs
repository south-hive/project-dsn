using Dsn.Contracts;

namespace Dsn.Runtime;

public sealed class DsnRuntime : IMessageSink, IWorkspaceRegistration, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly FifoMessageQueue queue;
    private readonly WorkspaceRegistry registry;
    private readonly SequentialScheduler scheduler = new();
    private readonly Lifetime lifetime;
    private readonly SemaphoreSlim wake = new(0);
    private readonly CancellationTokenSource processing = new();
    private readonly IErrorSink errors;
    private readonly IRawArchive? archive;
    public string[] Workspaces => registry.Names;
    private Task? loop;
    private bool stopping, activeMessage, disposed;
    public LifetimeStats LifetimeStats => lifetime.Stats;

    public DsnRuntime(IErrorSink errors, int capacity = 1024, long maxQueueBytes = 16 * 1024 * 1024,
        int maxMessages = 4096, long maxLiveBytes = 64 * 1024 * 1024, IRawArchive? archive = null, IRecordStore? results = null)
    {
        if (capacity < 1 || maxQueueBytes < 1 || maxMessages < 1 || maxLiveBytes < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.errors = errors; this.archive = archive;
        lifetime = new(maxMessages, maxLiveBytes);
        registry = new(errors, results);
        queue = new(capacity, maxQueueBytes);
    }
    public bool Register(IWorkspace workspace)
    {
        lock (gate)
        {
            if (loop is not null || stopping) throw new InvalidOperationException("Registry is closed");
            return registry.Register(workspace);
        }
    }
    public void Start()
    {
        lock (gate)
        {
            if (loop is not null || stopping) throw new InvalidOperationException("Runtime already started/stopped");
            loop = Task.Run(RunAsync);
        }
    }
    public bool TrySubmit(InboundMessage message)
    {
        lock (gate)
        {
            if (loop is null || stopping) return false;
            if (!queue.CanAccept(message.Payload.Length)) { errors.Report(new("QUEUE_FULL", "runtime", message.Envelope.SourceId)); return false; }
            var owned = lifetime.Create(message);
            if (owned is null) { errors.Report(new("MEMORY_FULL", "runtime", message.Envelope.SourceId)); return false; }
            queue.Add(owned); wake.Release(); return true;
        }
    }
    private async Task RunAsync()
    {
        while (true)
        {
            await wake.WaitAsync();
            OwnedMessage message;
            lock (gate)
            {
                if (queue.Empty) { if (stopping) return; continue; }
                message = queue.Take(); activeMessage = true;
            }
            // The root belongs to this dispatch, never to a scheduling strategy or plugin.
            try
            {
                if (archive is not null && message.ReplayId is null)
                    await archive.AppendAsync(message.Id, new(message.Envelope, message.Read(b => b.ToArray())) { ReceivedAt = message.ReceivedAt }, processing.Token);
                await scheduler.RunAsync(registry.Resolve(message), processing.Token);
            }
            catch (Exception e)
            {
                // When enabled, archive must succeed before processing: no silent loss of replay evidence.
                errors.Report(new("RAW_STORAGE_FAILED", "collector", message.Envelope.SourceId, Detail: e.GetType().Name));
            }
            finally
            {
                message.Release();
                lock (gate) activeMessage = false;
            }
        }
    }
    public async Task DrainAsync(CancellationToken token = default)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            lock (gate) if (queue.Empty && !activeMessage) return;
            await Task.Delay(5, token);
        }
    }
    public async Task<bool> StopAsync(TimeSpan timeout)
    {
        Task? task;
        lock (gate)
        {
            if (disposed) return true;
            stopping = true; task = loop; wake.Release();
        }
        if (task is null) return true;
        if (await Task.WhenAny(task, Task.Delay(timeout)) == task) { await task; return true; }
        processing.Cancel();
        lock (gate) { queue.Discard(); wake.Release(); }
        errors.Report(new("SHUTDOWN_INCOMPLETE", "runtime"));
        return false;
    }
    public async ValueTask DisposeAsync()
    {
        lock (gate) if (disposed) return;
        if (!stopping) await StopAsync(TimeSpan.FromSeconds(5));
        if (loop is not null) await loop;
        lock (gate)
        {
            if (disposed) return;
            disposed = true; processing.Dispose(); wake.Dispose();
        }
    }
}
