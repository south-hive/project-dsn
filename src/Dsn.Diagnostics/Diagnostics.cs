using Dsn.Contracts;

namespace Dsn.Diagnostics;

public sealed class ErrorSink(int capacity = 1024) : IErrorSink
{
    private readonly int limit = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    private readonly object gate = new();
    private readonly Dictionary<ErrorBody, ErrorAggregate> errors = [];
    private long revision, dropped;
    public long Revision { get { lock (gate) return revision; } }
    public long Dropped { get { lock (gate) return dropped; } }
    public void Report(ErrorBody body)
    {
        var now = DateTimeOffset.UtcNow;
        lock (gate)
        {
            if (errors.TryGetValue(body, out var old)) errors[body] = old with { LastSeen = now, Count = old.Count + 1 };
            else if (errors.Count < limit) errors.Add(body, new(body, now, now, 1));
            else { dropped++; return; }
            revision++;
        }
    }
    public ErrorAggregate[] Snapshot() { lock (gate) return errors.Values.ToArray(); }
}
public sealed class AdminWorkspace(ErrorSink errors, IRecordStore records)
{
    private long lastRevision = -1;
    public async Task PublishAsync(CancellationToken token = default)
    {
        var revision = errors.Revision;
        if (revision == lastRevision) return;
        foreach (var e in errors.Snapshot())
            await records.AppendAsync(new("admin", Fields.From(("code", e.Body.Code), ("component", e.Body.Component),
                ("source_id", e.Body.SourceId), ("workspace_name", e.Body.Workspace), ("detail", e.Body.Detail),
                ("first_seen", e.FirstSeen), ("last_seen", e.LastSeen), ("count", e.Count), ("snapshot_revision", revision))), token);
        lastRevision = revision;
    }
}
