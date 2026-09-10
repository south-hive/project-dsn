using Dsn.Contracts;

namespace Dsn.Runtime;

internal sealed class MessageContext(OwnedMessage message, string? workspace = null, IRecordStore? results = null, string? processorVersion = null) : IMessageContext, IDisposable
{
    private readonly object gate = new();
    private readonly HashSet<Lease> leases = [];
    private bool closed;
    public IPayloadLease Checkout() { lock (gate) { ObjectDisposedException.ThrowIf(closed, this); var lease = message.Checkout(); leases.Add(lease); return lease; } }
    public bool Checkin(IPayloadLease lease)
    {
        lock (gate) { if (lease is not Lease own || !leases.Contains(own)) throw new ArgumentException("Foreign lease"); return own.Return(); }
    }
    public ValueTask EmitAsync(IReadOnlyDictionary<string, System.Text.Json.JsonElement> fields, CancellationToken token = default)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (results is null || workspace is null) throw new InvalidOperationException("No result sink configured");
            var values = fields.ToDictionary(p => p.Key, p => p.Value.Clone());
            foreach (var p in Fields.From(("message_id", message.Id), ("processing_id", message.ProcessingId),
                ("replay_id", message.ReplayId), ("source_id", message.Envelope.SourceId), ("time", message.Envelope.Time),
                ("event_type", message.Envelope.EventType), ("processor_version", processorVersion))) values[p.Key] = p.Value;
            return results.AppendAsync(new(workspace, values), token);
        }
    }
    public void Dispose() { lock (gate) { closed = true; foreach (var lease in leases) lease.Return(); } }
}
