using Dsn.Contracts;

namespace Dsn.Core;

internal interface IQueuePolicy { OwnedMessage Take(Queue<OwnedMessage> queue); }
internal sealed class NoPolicy : IQueuePolicy { public OwnedMessage Take(Queue<OwnedMessage> queue) => queue.Dequeue(); }
internal interface IWorkspaceExecutor { Task ExecuteAsync(OwnedMessage message, IReadOnlyDictionary<string, IWorkspace> registry, CancellationToken token); }
internal sealed class SequentialExecutor(IErrorSink errors) : IWorkspaceExecutor
{
    public async Task ExecuteAsync(OwnedMessage message, IReadOnlyDictionary<string, IWorkspace> registry, CancellationToken token)
    {
        try
        {
            foreach (var name in message.Envelope.Workspace.Distinct(StringComparer.Ordinal))
            {
                if (token.IsCancellationRequested) break;
                if (!registry.TryGetValue(name, out var workspace)) { errors.Report(new("UNKNOWN_WORKSPACE", "runtime", message.Envelope.SourceId, name)); continue; }
                using var context = new MessageContext(message);
                try { await workspace.ProcessAsync(context, token); }
                catch (Exception e) { errors.Report(new("WORKSPACE_FAILED", "runtime", message.Envelope.SourceId, name, e.GetType().Name)); }
            }
        }
        finally { message.Release(); }
    }
}
public sealed class DsnRuntime : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly Queue<OwnedMessage> queue = new();
    private readonly Dictionary<string, IWorkspace> registry = new(StringComparer.Ordinal);
    private readonly IQueuePolicy policy = new NoPolicy();
    private readonly IWorkspaceExecutor executor;
    private readonly SemaphoreSlim wake = new(0);
    private readonly CancellationTokenSource processing = new();
    private readonly IErrorSink errors;
    private readonly int capacity;
    private readonly long maxQueueBytes;
    private Task? loop;
    private long queueBytes;
    private bool stopping, active;
    public Lifetime Lifetime { get; }
    public DsnRuntime(IErrorSink errors, int capacity = 1024, long maxQueueBytes = 16 * 1024 * 1024, int maxMessages = 4096, long maxLiveBytes = 64 * 1024 * 1024)
    {
        if (capacity < 1 || maxQueueBytes < 1 || maxMessages < 1 || maxLiveBytes < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.errors = errors; this.capacity = capacity; this.maxQueueBytes = maxQueueBytes;
        Lifetime = new(maxMessages, maxLiveBytes); executor = new SequentialExecutor(errors);
    }
    public bool Register(IWorkspace workspace)
    {
        lock (gate)
        {
            if (loop is not null || stopping) throw new InvalidOperationException("Registry is closed");
            if (!Names.Valid(workspace.Name) || !registry.TryAdd(workspace.Name, workspace)) { errors.Report(new("REGISTRATION_REJECTED", "registry", Workspace: workspace.Name)); return false; }
            return true;
        }
    }
    public void Start() { lock (gate) { if (loop is not null || stopping) throw new InvalidOperationException("Runtime already started/stopped"); loop = Task.Run(RunAsync); } }
    public bool Submit(DecodedMessage message)
    {
        lock (gate)
        {
            if (loop is null || stopping) return false;
            if (queue.Count >= capacity || queueBytes + message.Payload.Length > maxQueueBytes) { errors.Report(new("QUEUE_FULL", "runtime", message.Envelope.SourceId)); return false; }
            var owned = Lifetime.Create(message);
            if (owned is null) { errors.Report(new("MEMORY_FULL", "runtime", message.Envelope.SourceId)); return false; }
            queue.Enqueue(owned); queueBytes += message.Payload.Length; wake.Release(); return true;
        }
    }
    private async Task RunAsync()
    {
        while (true)
        {
            await wake.WaitAsync();
            OwnedMessage? message;
            lock (gate)
            {
                if (queue.Count == 0) { if (stopping) return; continue; }
                message = policy.Take(queue); queueBytes -= message.Read(b => b.Length); active = true;
            }
            try { await executor.ExecuteAsync(message, registry, processing.Token); }
            finally { lock (gate) active = false; }
        }
    }
    public async Task DrainAsync(CancellationToken token = default)
    {
        while (true) { token.ThrowIfCancellationRequested(); lock (gate) if (queue.Count == 0 && !active) return; await Task.Delay(5, token); }
    }
    public async Task<bool> StopAsync(TimeSpan timeout)
    {
        Task? task;
        lock (gate) { stopping = true; task = loop; wake.Release(); }
        if (task is null) return true;
        if (await Task.WhenAny(task, Task.Delay(timeout)) == task) { await task; return true; }
        processing.Cancel();
        lock (gate) { while (queue.TryDequeue(out var m)) m.Release(); queueBytes = 0; wake.Release(); }
        errors.Report(new("SHUTDOWN_INCOMPLETE", "runtime"));
        return false; // Active calls retain leases and storage until they actually return.
    }
    public async ValueTask DisposeAsync()
    {
        await StopAsync(TimeSpan.FromSeconds(5));
        if (loop is not null) await loop;
        processing.Dispose(); wake.Dispose();
    }
}
