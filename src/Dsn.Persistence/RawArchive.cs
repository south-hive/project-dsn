using System.Text.Json;
using Dsn.Contracts;

namespace Dsn.Persistence;

// Separate quotas and journal: unknown Workspace payloads can still be retained.
public sealed class RawArchive(string path, long maxBytes, int maxRecords) : IRawArchive, IDisposable
{
    private readonly JournalStore journal = new(path, maxBytes, maxRecords);
    public ValueTask AppendAsync(string messageId, InboundMessage message, CancellationToken token = default) =>
        journal.AppendAsync(new("raw", Fields.From(("message_id", messageId),
            ("received_at", message.ReceivedAt),
            ("envelope", JsonSerializer.Serialize(message.Envelope, JsonFormat.Options)),
            ("payload", Convert.ToBase64String(message.Payload)))), token);
    public IReadOnlyList<RawMessage> Read(long afterId = 0, int limit = 100) =>
        journal.Query(new(AfterId: afterId, Limit: limit)).Select(r => new RawMessage(r.Id,
            r.Fields["message_id"].GetString()!, r.Fields["received_at"].GetString()!,
            JsonSerializer.Deserialize<Envelope>(r.Fields["envelope"].GetString()!, JsonFormat.Options)!,
            Convert.FromBase64String(r.Fields["payload"].GetString()!))).ToArray();
    public void Dispose() => journal.Dispose();
}
