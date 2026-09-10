namespace Dsn.Contracts;

public sealed record RawMessage(long Id, string MessageId, string ReceivedAt, Envelope Envelope, byte[] Payload);
public interface IRawArchive
{
    ValueTask AppendAsync(string messageId, InboundMessage message, CancellationToken token = default);
    IReadOnlyList<RawMessage> Read(long afterId = 0, int limit = 100);
}
