using System.Text.Json;

namespace Dsn.Contracts;

public sealed record Envelope(int Version, string SourceId, JsonElement? SourceDescription,
    string Time, string EventType, IReadOnlyList<string> Workspace);

/// <summary>Payload ownership transfers to the sink for this call; the producer must not mutate or reuse it afterwards.</summary>
public sealed record InboundMessage(Envelope Envelope, byte[] Payload);

public interface IMessageSink
{
    /// <summary>Immediate admission result, not a processing/storage acknowledgement. False means discarded.</summary>
    bool TrySubmit(InboundMessage message);
}
