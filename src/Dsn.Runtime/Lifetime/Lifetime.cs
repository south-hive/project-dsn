using Dsn.Contracts;

namespace Dsn.Runtime;

internal sealed class Lifetime(int maxMessages = 4096, long maxBytes = 64 * 1024 * 1024)
{
    private readonly int messageLimit = maxMessages > 0 ? maxMessages : throw new ArgumentOutOfRangeException(nameof(maxMessages));
    private readonly long byteLimit = maxBytes > 0 ? maxBytes : throw new ArgumentOutOfRangeException(nameof(maxBytes));
    private readonly object gate = new();
    private long created, reclaimed, checkouts, checkins, bytes;
    public LifetimeStats Stats { get { lock (gate) return new(created, reclaimed, checkouts, checkins, bytes); } }
    internal OwnedMessage? Create(InboundMessage message)
    {
        lock (gate)
        {
            if (created - reclaimed >= messageLimit || bytes + message.Payload.Length > byteLimit) return null;
            created++; bytes += message.Payload.Length;
            return new(this, message.Envelope, message.Payload);
        }
    }
    internal void Acquired() { lock (gate) checkouts++; }
    internal void Released(int? length) { lock (gate) { checkins++; if (length is int n) { reclaimed++; bytes -= n; } } }
}
public sealed record LifetimeStats(long Created, long Reclaimed, long Checkouts, long Checkins, long Bytes)
{
    public long References => Created + Checkouts - Checkins;
}
internal sealed class OwnedMessage(Lifetime owner, Envelope envelope, byte[] payload)
{
    private readonly object gate = new();
    private byte[]? bytes = payload;
    private int references = 1;
    internal readonly string Id = Guid.NewGuid().ToString("N");
    internal readonly Envelope Envelope = envelope;
    internal Lease Checkout()
    {
        lock (gate) { if (bytes is null) throw new ObjectDisposedException(nameof(OwnedMessage)); references++; owner.Acquired(); return new(this); }
    }
    internal void Release()
    {
        lock (gate)
        {
            if (references <= 0) throw new InvalidOperationException("Reference underflow");
            int? length = null;
            if (--references == 0) { length = bytes!.Length; bytes = null; }
            owner.Released(length);
        }
    }
    internal T Read<T>(Func<byte[], T> read) { lock (gate) return read(bytes ?? throw new ObjectDisposedException(nameof(OwnedMessage))); }
}
